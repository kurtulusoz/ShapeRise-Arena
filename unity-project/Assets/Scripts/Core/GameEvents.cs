using System;

namespace ShapeRise.Core
{
    /// <summary>
    /// Lightweight server-side event bus.
    /// All events are raised on the server; clients receive state via
    /// SyncVars and ClientRpcs — never directly through this bus.
    /// </summary>
    public static class GameEvents
    {
        // Raised when server spawns a new shape
        public static event Action<uint, ShapeType, ShapeColor, float> OnShapeSpawned;

        // Raised when merge is confirmed (shapeNetId_A, shapeNetId_B, type, color)
        public static event Action<uint, uint, ShapeType, ShapeColor> OnMergeConfirmed;

        // Raised to apply height delta to a player (playerNetId, deltaPts)
        public static event Action<uint, float> OnHeightDelta;

        // Raised when a player wins
        public static event Action<uint> OnPlayerWon;

        // Raised when a bot slot is filled
        public static event Action<uint> OnBotFilled;

        public static void RaiseShapeSpawned(uint shapeNetId, ShapeType t, ShapeColor c, float x)
            => OnShapeSpawned?.Invoke(shapeNetId, t, c, x);

        public static void RaiseMergeConfirmed(uint netIdA, uint netIdB, ShapeType t, ShapeColor c)
            => OnMergeConfirmed?.Invoke(netIdA, netIdB, t, c);

        public static void RaiseHeightDelta(uint playerNetId, float delta)
            => OnHeightDelta?.Invoke(playerNetId, delta);

        public static void RaisePlayerWon(uint playerNetId)
            => OnPlayerWon?.Invoke(playerNetId);

        public static void RaiseBotFilled(uint botNetId)
            => OnBotFilled?.Invoke(botNetId);
    }
}
