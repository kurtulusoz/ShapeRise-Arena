using System;
using UnityEngine;

namespace ShapeRise.Networking.Data
{
    /// <summary>
    /// Single server entry in the remote config JSON.
    ///
    /// Expected JSON format hosted at your config endpoint:
    /// {
    ///   "minClientVersion": "1.0.0",
    ///   "servers": [
    ///     {
    ///       "address":     "eu1.shaperise.io",
    ///       "port":         7777,
    ///       "region":      "EU",
    ///       "version":     "1.0.0",
    ///       "playerCount":  2,
    ///       "maxPlayers":   4
    ///     }
    ///   ]
    /// }
    /// </summary>
    [Serializable]
    public class ServerEntry
    {
        public string address;
        public ushort port;
        public string region;
        public string version;
        public int    playerCount;
        public int    maxPlayers;

        public bool IsFull => playerCount >= maxPlayers;

        public bool IsVersionCompatible(string clientVersion)
        {
            if (string.IsNullOrEmpty(version)) return true;
            return string.Equals(version, clientVersion, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() =>
            $"{address}:{port} [{region}] {playerCount}/{maxPlayers} v{version}";
    }

    /// <summary>Root of the remote servers.json config.</summary>
    [Serializable]
    public class ServerListConfig
    {
        /// <summary>Minimum client version required to connect. Older clients are rejected.</summary>
        public string        minClientVersion;
        public ServerEntry[] servers;
    }
}
