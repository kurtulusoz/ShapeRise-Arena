using System;
using System.Collections;
using Mirror;
using kcp2k;
using UnityEngine;
using ShapeRise.Networking.Data;

namespace ShapeRise.Networking
{
    /// <summary>
    /// Central network address resolver for ShapeRise Arena.
    ///
    /// Modes (auto-detected unless overridden in Inspector):
    ///
    ///   Development  — Always used inside Unity Editor.
    ///                  Connects to localhost (127.0.0.1) instantly.
    ///
    ///   DeepLink     — An incoming shaperise://join/… URL is detected.
    ///                  Parsed by DeepLinkResolver; connects to extracted address/port.
    ///                  Falls back to Production on parse failure.
    ///
    ///   Production   — No deep link detected; running in a real build.
    ///                  RemoteConfigFetcher fetches servers.json, runs version check,
    ///                  scores servers, and connects to the best one.
    ///
    /// UI override: call OverrideAndConnect(address, port) from any UI script
    /// (e.g. NetworkAddressUI) to skip auto-resolution and connect immediately.
    ///
    /// Attach to: the same GameObject as ArenaNetworkManager.
    /// Required references: RemoteConfigFetcher (assign in Inspector).
    /// </summary>
    public class NetworkAddressProvider : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────
        public static NetworkAddressProvider Instance { get; private set; }

        // ── Inspector ────────────────────────────────────────────────
        [Header("Mode")]
        [Tooltip("If true, mode is auto-detected (Editor=Dev, DeepLink, else Production). " +
                 "If false, ForcedMode is always used.")]
        [SerializeField] private bool        _autoDetect = true;

        [Tooltip("Used only when AutoDetect is off.")]
        [SerializeField] private AddressMode _forcedMode  = AddressMode.Production;

        [Header("Development Settings")]
        [SerializeField] private string _devAddress  = "127.0.0.1";
        [SerializeField] private ushort _devPort     = 7777;

        [Header("References")]
        [SerializeField] private RemoteConfigFetcher _remoteConfigFetcher;

        // ── State ────────────────────────────────────────────────────
        public AddressMode CurrentMode    { get; private set; }
        public string      CurrentAddress { get; private set; }
        public ushort      CurrentPort    { get; private set; } = 7777;
        public bool        IsResolving    { get; private set; }

        // ── Events ───────────────────────────────────────────────────
        /// <summary>Fired when an address has been resolved and a connection attempt begins.</summary>
        public event Action<string, ushort>  OnAddressResolved;

        /// <summary>Fired when resolution fails in all modes/fallbacks.</summary>
        public event Action<string>          OnResolutionFailed;

        /// <summary>Fired when the active AddressMode changes.</summary>
        public event Action<AddressMode>     OnModeChanged;

        // ── Internal ─────────────────────────────────────────────────
        private string _pendingDeepLinkUrl;

        // Fields for production coroutine result capture
        private ServerEntry _resolvedEntry;
        private string      _resolutionError;

        // ── Unity lifecycle ──────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Register before Start so no deep link is missed on cold start
            Application.deepLinkActivated += OnDeepLinkReceived;
        }

        private void Start()
        {
            // Cold-start deep link (Android/iOS: app opened via link while not running)
            if (!string.IsNullOrEmpty(Application.absoluteURL))
                _pendingDeepLinkUrl = Application.absoluteURL;

            StartCoroutine(Resolve());
        }

        private void OnDestroy()
        {
            Application.deepLinkActivated -= OnDeepLinkReceived;
            Instance = null;
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Re-run address resolution from scratch.
        /// Safe to call multiple times (old coroutine is stopped first).
        /// </summary>
        public void ResolveAndConnect()
        {
            StopAllCoroutines();
            StartCoroutine(Resolve());
        }

        /// <summary>
        /// Immediately override resolved address and connect.
        /// Called from NetworkAddressUI or any runtime script.
        /// </summary>
        public void OverrideAndConnect(string address, ushort port = 7777)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                OnResolutionFailed?.Invoke("Address cannot be empty.");
                return;
            }

            Debug.Log($"[NetworkAddressProvider] Manual override → {address}:{port}");
            ApplyAndConnect(address, port, AddressMode.Development);
        }

        // ── Deep link handler ────────────────────────────────────────

        private void OnDeepLinkReceived(string url)
        {
            Debug.Log($"[NetworkAddressProvider] Deep link received: {url}");
            _pendingDeepLinkUrl = url;

            // Interrupt any ongoing resolution and restart with the deep link
            StopAllCoroutines();
            StartCoroutine(Resolve());
        }

        // ── Resolution pipeline ──────────────────────────────────────

        private IEnumerator Resolve()
        {
            IsResolving = true;

            AddressMode mode = DetermineMode();
            CurrentMode      = mode;
            OnModeChanged?.Invoke(mode);

            Debug.Log($"[NetworkAddressProvider] Mode: {mode}");

            switch (mode)
            {
                case AddressMode.Development:
                    yield return ResolveDevelopment();
                    break;

                case AddressMode.DeepLink:
                    string url  = _pendingDeepLinkUrl;
                    _pendingDeepLinkUrl = null;
                    yield return ResolveDeepLink(url);
                    break;

                case AddressMode.Production:
                    yield return ResolveProduction();
                    break;
            }

            IsResolving = false;
        }

        private AddressMode DetermineMode()
        {
            if (!_autoDetect) return _forcedMode;

            // Compile-time: always dev in editor
#if UNITY_EDITOR
            return AddressMode.Development;
#else
            // Runtime: check for pending deep link first
            if (!string.IsNullOrEmpty(_pendingDeepLinkUrl))
                return AddressMode.DeepLink;

            return AddressMode.Production;
#endif
        }

        // ── Mode implementations ─────────────────────────────────────

        private IEnumerator ResolveDevelopment()
        {
            Debug.Log($"[NetworkAddressProvider] Dev mode → {_devAddress}:{_devPort}");
            ApplyAndConnect(_devAddress, _devPort, AddressMode.Development);
            yield break;
        }

        private IEnumerator ResolveDeepLink(string url)
        {
            var result = DeepLinkResolver.Resolve(url);

            if (!result.Success)
            {
                Debug.LogWarning($"[NetworkAddressProvider] Deep link parse failed: {result.Error} — falling back to Production.");
                yield return ResolveProduction();
                yield break;
            }

            ApplyAndConnect(result.Address, result.Port, AddressMode.DeepLink);
        }

        private IEnumerator ResolveProduction()
        {
            if (_remoteConfigFetcher == null)
            {
                HandleFail("RemoteConfigFetcher is not assigned in NetworkAddressProvider.");
                yield break;
            }

            // Clear previous results
            _resolvedEntry   = null;
            _resolutionError = null;

            _remoteConfigFetcher.OnServerResolved += CaptureResolved;
            _remoteConfigFetcher.OnFetchFailed    += CaptureFailed;

            yield return _remoteConfigFetcher.FetchBestServer();

            // By this point FetchBestServer coroutine has finished and
            // exactly one of the two callbacks has been invoked synchronously.
            _remoteConfigFetcher.OnServerResolved -= CaptureResolved;
            _remoteConfigFetcher.OnFetchFailed    -= CaptureFailed;

            if (_resolutionError != null)
            {
                HandleFail(_resolutionError);
                yield break;
            }

            if (_resolvedEntry != null)
                ApplyAndConnect(_resolvedEntry.address, _resolvedEntry.port, AddressMode.Production);
        }

        private void CaptureResolved(ServerEntry entry) => _resolvedEntry   = entry;
        private void CaptureFailed(string error)        => _resolutionError = error;

        // ── Apply & connect ──────────────────────────────────────────

        private void ApplyAndConnect(string address, ushort port, AddressMode mode)
        {
            CurrentAddress = address;
            CurrentPort    = port;

            if (NetworkManager.singleton == null)
            {
                HandleFail("NetworkManager.singleton is null — cannot connect.");
                return;
            }

            NetworkManager.singleton.networkAddress = address;
            ApplyPortToTransport(port);

            Debug.Log($"[NetworkAddressProvider] Connecting → {address}:{port} ({mode})");
            OnAddressResolved?.Invoke(address, port);

            // Gracefully reconnect if already active
            if (NetworkClient.active)
                NetworkManager.singleton.StopClient();

            NetworkManager.singleton.StartClient();
        }

        private static void ApplyPortToTransport(ushort port)
        {
            if (NetworkManager.singleton == null) return;

            if (NetworkManager.singleton.transport is KcpTransport kcp)
                kcp.Port = port;
            else
                Debug.LogWarning("[NetworkAddressProvider] Transport is not KcpTransport. Port not applied.");
        }

        private void HandleFail(string message)
        {
            Debug.LogError($"[NetworkAddressProvider] {message}");
            OnResolutionFailed?.Invoke(message);
        }
    }

    public enum AddressMode
    {
        /// <summary>Always used in Unity Editor. Connects to localhost.</summary>
        Development,

        /// <summary>Connects to the address extracted from an incoming deep link URL.</summary>
        DeepLink,

        /// <summary>Resolves address from the remote servers.json config endpoint.</summary>
        Production
    }
}
