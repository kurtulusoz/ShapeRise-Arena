using UnityEngine;
using Mirror;
using System;

namespace ShapeRise.DeepLink
{
    /// <summary>
    /// DEPRECATED — Superseded by NetworkAddressProvider + DeepLinkResolver.
    ///
    /// Application.deepLinkActivated is now managed exclusively by
    /// NetworkAddressProvider to prevent double-registration.
    ///
    /// This class is kept for reference only. Remove it from all GameObjects
    /// in your scenes and do not attach it to any new objects.
    /// </summary>
    [System.Obsolete("Use NetworkAddressProvider + DeepLinkResolver instead.")]
    public class DeepLinkHandler : MonoBehaviour
    {
        private const string SCHEME = "shaperise://join/";

        [Header("Network")]
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private ushort         _defaultPort    = 7777;

        // ── Lifecycle ────────────────────────────────────────────────

        private void Start()
        {
            Application.deepLinkActivated += OnDeepLink;

            // Handle deep link that was present at launch (iOS/Android cold start)
            if (!string.IsNullOrEmpty(Application.absoluteURL))
                OnDeepLink(Application.absoluteURL);
        }

        private void OnDestroy()
        {
            Application.deepLinkActivated -= OnDeepLink;
        }

        // ── Handler ──────────────────────────────────────────────────

        private void OnDeepLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            Debug.Log($"[DeepLink] URL received: {url}");

            if (!url.StartsWith(SCHEME, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[DeepLink] Unknown scheme: {url}");
                return;
            }

            // Extract and sanitize room ID
            string roomId = url.Substring(SCHEME.Length).Trim('/', ' ');
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogWarning("[DeepLink] Empty room ID — ignoring.");
                return;
            }

            // Validate: room IDs are alphanumeric (prevent injection)
            if (!IsValidRoomId(roomId))
            {
                Debug.LogWarning($"[DeepLink] Invalid room ID format: {roomId}");
                return;
            }

            JoinRoom(roomId);
        }

        private void JoinRoom(string roomId)
        {
            Debug.Log($"[DeepLink] Joining room: {roomId}");

            // TODO: resolve roomId → server IP via matchmaking REST API.
            // For the current dedicated-server phase, roomId IS the server address.
            _networkManager.networkAddress = roomId;
            _networkManager.StartClient();
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>Alphanumeric + hyphens only. Max 64 chars.</summary>
        private static bool IsValidRoomId(string id)
        {
            if (id.Length > 64) return false;
            foreach (char c in id)
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return false;
            return true;
        }

        /// <summary>Generate a shareable deep link for a given room ID.</summary>
        public static string BuildJoinUrl(string roomId) => $"{SCHEME}{roomId}";

#if UNITY_EDITOR
        [ContextMenu("Test Deep Link (Editor)")]
        private void TestDeepLink()
        {
            OnDeepLink("shaperise://join/test-room-123");
        }
#endif
    }
}
