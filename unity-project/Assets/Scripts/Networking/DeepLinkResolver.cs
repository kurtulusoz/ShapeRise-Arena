using System;
using UnityEngine;

namespace ShapeRise.Networking
{
    /// <summary>
    /// Pure-static, MonoBehaviour-free deep link parser.
    /// Replaces DeepLinkHandler.cs (Application.deepLinkActivated is now
    /// managed by NetworkAddressProvider — see that class).
    ///
    /// Supported URL formats:
    ///   shaperise://join/192.168.1.10           → address=192.168.1.10,  port=7777 (default)
    ///   shaperise://join/192.168.1.10:7778      → address=192.168.1.10,  port=7778
    ///   shaperise://join/eu1.shaperise.io       → address=eu1.shaperise.io, port=7777
    ///   shaperise://join/eu1.shaperise.io:7778  → address=eu1.shaperise.io, port=7778
    ///   shaperise://join/[::1]:7778             → IPv6 address, port=7778
    /// </summary>
    public static class DeepLinkResolver
    {
        private const string Scheme     = "shaperise://join/";
        private const ushort DefaultPort = 7777;

        public readonly struct ResolveResult
        {
            public readonly bool   Success;
            public readonly string Address;
            public readonly ushort Port;
            public readonly string Error;

            public ResolveResult(string address, ushort port)
            {
                Success = true;
                Address = address;
                Port    = port;
                Error   = null;
            }

            public ResolveResult(string error)
            {
                Success = false;
                Address = null;
                Port    = 0;
                Error   = error;
            }
        }

        public static ResolveResult Resolve(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return new ResolveResult("URL is null or empty.");

            if (!url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
                return new ResolveResult($"Unknown URL scheme. Expected '{Scheme}', got: {url}");

            // Strip scheme and leading/trailing slashes
            string payload = url.Substring(Scheme.Length).Trim('/', ' ');

            if (string.IsNullOrEmpty(payload))
                return new ResolveResult("Empty address payload in URL.");

            return payload.StartsWith("[")
                ? ParseIPv6(payload)
                : ParseAddressAndPort(payload);
        }

        // ── Parsers ──────────────────────────────────────────────────

        /// <summary>Parses [::1] or [::1]:7778</summary>
        private static ResolveResult ParseIPv6(string payload)
        {
            int closingBracket = payload.IndexOf(']');
            if (closingBracket < 0)
                return new ResolveResult($"Malformed IPv6 address: {payload}");

            string address = payload.Substring(0, closingBracket + 1);
            if (!IsValidAddress(address))
                return new ResolveResult($"Invalid IPv6 address: {address}");

            ushort port = DefaultPort;
            string rest = payload.Substring(closingBracket + 1);

            if (!string.IsNullOrEmpty(rest))
            {
                if (!rest.StartsWith(":") || rest.Length < 2)
                    return new ResolveResult($"Malformed port section after IPv6: {rest}");

                if (!ushort.TryParse(rest.Substring(1), out port))
                    return new ResolveResult($"Invalid port number: {rest.Substring(1)}");
            }

            return new ResolveResult(address, port);
        }

        /// <summary>Parses host:port or host (IPv4 / hostname).</summary>
        private static ResolveResult ParseAddressAndPort(string payload)
        {
            int lastColon = payload.LastIndexOf(':');
            string address;
            ushort port = DefaultPort;

            if (lastColon > 0 && ushort.TryParse(payload.Substring(lastColon + 1), out ushort parsedPort))
            {
                // Has explicit port
                address = payload.Substring(0, lastColon);
                port    = parsedPort;
            }
            else
            {
                address = payload;
            }

            if (!IsValidAddress(address))
                return new ResolveResult($"Invalid address: {address}");

            return new ResolveResult(address, port);
        }

        // ── Validation ───────────────────────────────────────────────

        /// <summary>
        /// Allows: alphanumeric, dots, hyphens (hostname), brackets + colons (IPv6).
        /// Max 253 chars (DNS spec).
        /// Prevents injection via URL.
        /// </summary>
        private static bool IsValidAddress(string addr)
        {
            if (string.IsNullOrEmpty(addr) || addr.Length > 253) return false;

            foreach (char c in addr)
            {
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-'
                                              && c != '[' && c != ']' && c != ':')
                    return false;
            }

            return true;
        }

        /// <summary>Generate a shareable deep link from an address.</summary>
        public static string Build(string address, ushort port = DefaultPort)
        {
            bool hasPort = port != DefaultPort;
            return hasPort ? $"{Scheme}{address}:{port}" : $"{Scheme}{address}";
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("ShapeRise/Test DeepLink Parse")]
        private static void TestParse()
        {
            string[] samples =
            {
                "shaperise://join/192.168.1.10",
                "shaperise://join/192.168.1.10:8888",
                "shaperise://join/eu1.shaperise.io",
                "shaperise://join/eu1.shaperise.io:7778",
                "shaperise://join/[::1]:7778",
                "shaperise://join/",
                "wrong://join/bad",
            };

            foreach (var url in samples)
            {
                var r = Resolve(url);
                Debug.Log(r.Success
                    ? $"[DeepLinkResolver] OK  {url} → {r.Address}:{r.Port}"
                    : $"[DeepLinkResolver] ERR {url} → {r.Error}");
            }
        }
#endif
    }
}
