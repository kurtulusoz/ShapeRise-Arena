using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using ShapeRise.Networking.Data;

namespace ShapeRise.Networking
{
    /// <summary>
    /// Fetches the server list from a remote JSON endpoint and selects the best server.
    ///
    /// Server selection scoring:
    ///   +100  preferred region matches
    ///   +10   sweet-spot fill: 2 slots remain (avoids empty AND full servers)
    ///    -n   penalise fully-filled servers (filtered out, but guard kept)
    ///
    /// After FetchBestServer() coroutine completes, exactly one of these events is fired:
    ///   OnServerResolved(ServerEntry)  → contains address/port to connect to
    ///   OnFetchFailed(errorMessage)    → caller should show error / fallback
    /// </summary>
    public class RemoteConfigFetcher : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────
        [Header("Endpoint")]
        [Tooltip("Full HTTPS URL to the servers.json file you host.")]
        [SerializeField] private string _configUrl = "https://config.shaperise.io/servers.json";

        [Tooltip("Request timeout in seconds.")]
        [SerializeField] private float _timeoutSeconds = 10f;

        [Header("Preferences")]
        [Tooltip("ISO region code: EU, NA, ASIA …")]
        [SerializeField] private string _preferredRegion = "EU";

        // ── Events ───────────────────────────────────────────────────
        public event Action<ServerEntry> OnServerResolved;
        public event Action<string>      OnFetchFailed;

        // ── Public API ───────────────────────────────────────────────

        /// <summary>Run as a coroutine from NetworkAddressProvider.</summary>
        public IEnumerator FetchBestServer()
        {
            if (string.IsNullOrWhiteSpace(_configUrl))
            {
                Fail("Config URL is not set in RemoteConfigFetcher.");
                yield break;
            }

            Debug.Log($"[RemoteConfig] Fetching {_configUrl}");

            using UnityWebRequest request = UnityWebRequest.Get(_configUrl);
            request.timeout              = Mathf.Max(1, Mathf.RoundToInt(_timeoutSeconds));

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail($"Network error: {request.error} (URL: {_configUrl})");
                yield break;
            }

            ParseAndResolve(request.downloadHandler.text);
        }

        // ── Parsing ──────────────────────────────────────────────────

        private void ParseAndResolve(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Fail("Remote config returned empty body.");
                return;
            }

            ServerListConfig config;
            try
            {
                config = JsonUtility.FromJson<ServerListConfig>(json);
            }
            catch (Exception ex)
            {
                Fail($"JSON parse error: {ex.Message}");
                return;
            }

            if (config == null || config.servers == null || config.servers.Length == 0)
            {
                Fail("Remote config contains no servers.");
                return;
            }

            // Version guard: block outdated clients from connecting
            if (!IsClientVersionCompatible(config.minClientVersion, out string versionError))
            {
                Fail(versionError);
                return;
            }

            ServerEntry best = SelectBestServer(config.servers);
            if (best == null)
            {
                Fail("No available servers (all full or version mismatch).");
                return;
            }

            Debug.Log($"[RemoteConfig] Selected: {best}");
            OnServerResolved?.Invoke(best);
        }

        // ── Server selection ─────────────────────────────────────────

        private ServerEntry SelectBestServer(ServerEntry[] servers)
        {
            string clientVersion = Application.version;
            ServerEntry best     = null;
            int         bestScore = int.MinValue;

            foreach (var server in servers)
            {
                if (server == null)                           continue;
                if (server.IsFull)                            continue;
                if (!server.IsVersionCompatible(clientVersion)) continue;

                int score = ScoreServer(server);
                if (score > bestScore)
                {
                    bestScore = score;
                    best      = server;
                }
            }

            return best;
        }

        private int ScoreServer(ServerEntry server)
        {
            int score = 0;

            // Region affinity
            if (string.Equals(server.region, _preferredRegion, StringComparison.OrdinalIgnoreCase))
                score += 100;

            // Fill sweet spot: penalise near-empty and near-full equally
            // Optimal = 2 slots remaining (fastest match without isolating a player)
            int slotsRemaining = server.maxPlayers - server.playerCount;
            score += 10 - Mathf.Abs(slotsRemaining - 2);

            return score;
        }

        // ── Version check ────────────────────────────────────────────

        private static bool IsClientVersionCompatible(string minVersion, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(minVersion)) return true;

            if (!Version.TryParse(minVersion, out Version min))  return true;   // Unparsable → allow
            if (!Version.TryParse(Application.version, out Version client)) return true;

            if (client < min)
            {
                error = $"Client v{Application.version} is outdated. Minimum required: v{minVersion}. Please update.";
                return false;
            }

            return true;
        }

        // ── Helpers ──────────────────────────────────────────────────

        private void Fail(string message)
        {
            Debug.LogError($"[RemoteConfig] {message}");
            OnFetchFailed?.Invoke(message);
        }
    }
}
