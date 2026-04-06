using UnityEngine;
using UnityEngine.UI;
using ShapeRise.Networking;

namespace ShapeRise.UI
{
    /// <summary>
    /// UI panel for runtime server address override.
    ///
    /// Inspector wiring:
    ///   _provider     → NetworkAddressProvider (auto-found if null)
    ///   _addressField → InputField  (e.g. "eu1.shaperise.io")
    ///   _portField    → InputField  (e.g. "7777")  — optional, defaults to 7777
    ///   _connectBtn   → Button      (triggers manual connect)
    ///   _autoBtn      → Button      (re-runs auto-resolution)
    ///   _statusText   → Text        (feedback: resolving / connected / error)
    ///   _modeLabel    → Text        (shows current AddressMode)
    ///
    /// The panel listens to NetworkAddressProvider events so it always
    /// reflects state even when connection is triggered from other sources.
    /// </summary>
    public class NetworkAddressUI : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────
        [Header("Provider (auto-found if null)")]
        [SerializeField] private NetworkAddressProvider _provider;

        [Header("Input Fields")]
        [SerializeField] private InputField _addressField;
        [SerializeField] private InputField _portField;      // Optional

        [Header("Buttons")]
        [SerializeField] private Button _connectBtn;          // Manual connect
        [SerializeField] private Button _autoBtn;             // Re-run auto resolve

        [Header("Labels")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _modeLabel;
        [SerializeField] private Text _currentAddressLabel;  // Read-only display

        // ── Lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            if (_provider == null)
                _provider = NetworkAddressProvider.Instance
                         ?? FindFirstObjectByType<NetworkAddressProvider>();

            if (_provider == null)
            {
                SetStatus("ERROR: NetworkAddressProvider not found in scene.");
                this.enabled = false;
                return;
            }

            SubscribeToProvider();

            _connectBtn?.onClick.AddListener(OnConnectClicked);
            _autoBtn?.onClick.AddListener(OnAutoClicked);
        }

        private void Start()
        {
            // Populate fields with whatever was already resolved
            SyncFieldsFromProvider();
            UpdateModeLabel(_provider.CurrentMode);
        }

        private void OnDestroy()
        {
            if (_provider == null) return;
            _provider.OnAddressResolved  -= OnAddressResolved;
            _provider.OnResolutionFailed -= OnResolutionFailed;
            _provider.OnModeChanged      -= UpdateModeLabel;
        }

        // ── Event wiring ─────────────────────────────────────────────

        private void SubscribeToProvider()
        {
            _provider.OnAddressResolved  += OnAddressResolved;
            _provider.OnResolutionFailed += OnResolutionFailed;
            _provider.OnModeChanged      += UpdateModeLabel;
        }

        // ── Button handlers ──────────────────────────────────────────

        private void OnConnectClicked()
        {
            string address = _addressField != null ? _addressField.text.Trim() : string.Empty;

            if (string.IsNullOrEmpty(address))
            {
                SetStatus("Error: Address field is empty.");
                return;
            }

            ushort port = 7777;
            if (_portField != null && !string.IsNullOrEmpty(_portField.text.Trim()))
            {
                if (!ushort.TryParse(_portField.text.Trim(), out port))
                {
                    SetStatus("Error: Port must be a number between 1 and 65535.");
                    return;
                }
            }

            SetStatus($"Connecting to {address}:{port}…");
            SetButtonsInteractable(false);
            _provider.OverrideAndConnect(address, port);
        }

        private void OnAutoClicked()
        {
            SetStatus("Auto-resolving…");
            SetButtonsInteractable(false);
            _provider.ResolveAndConnect();
        }

        // ── Provider event handlers ──────────────────────────────────

        private void OnAddressResolved(string address, ushort port)
        {
            if (_addressField != null) _addressField.text = address;
            if (_portField    != null) _portField.text    = port.ToString();

            if (_currentAddressLabel != null)
                _currentAddressLabel.text = $"{address}:{port}";

            SetStatus($"Connected → {address}:{port}");
            SetButtonsInteractable(true);
        }

        private void OnResolutionFailed(string error)
        {
            SetStatus($"Failed: {error}");
            SetButtonsInteractable(true);
        }

        private void UpdateModeLabel(AddressMode mode)
        {
            if (_modeLabel != null)
                _modeLabel.text = $"Mode: {mode}";
        }

        // ── Helpers ──────────────────────────────────────────────────

        private void SyncFieldsFromProvider()
        {
            if (!string.IsNullOrEmpty(_provider.CurrentAddress))
            {
                if (_addressField != null) _addressField.text = _provider.CurrentAddress;
                if (_portField    != null) _portField.text    = _provider.CurrentPort.ToString();

                if (_currentAddressLabel != null)
                    _currentAddressLabel.text = $"{_provider.CurrentAddress}:{_provider.CurrentPort}";
            }

            if (_provider.IsResolving)
                SetStatus("Resolving…");
        }

        private void SetStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
            Debug.Log($"[NetworkAddressUI] {message}");
        }

        private void SetButtonsInteractable(bool state)
        {
            if (_connectBtn != null) _connectBtn.interactable = state;
            if (_autoBtn    != null) _autoBtn.interactable    = state;
        }
    }
}
