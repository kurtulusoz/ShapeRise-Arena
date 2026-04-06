using Mirror;
using UnityEngine;
using System.Collections.Generic;
using ShapeRise.Core;
using ShapeRise.Gameplay;

namespace ShapeRise.Networking
{
    /// <summary>
    /// Central game state coordinator (NetworkBehaviour).
    /// Spawned by ArenaNetworkManager when a match begins.
    ///
    /// Owns:
    ///   - SyncVar GameState (broadcast to all clients)
    ///   - SyncList PlayerHeights (auto-synced height bars)
    ///   - ClientRpc calls for countdown, game start, game over
    ///   - Win condition check
    /// </summary>
    public class GameController : NetworkBehaviour
    {
        // ── Synced state ─────────────────────────────────────────────
        [SyncVar(hook = nameof(OnGameStateChanged))]
        public GameState CurrentState = GameState.WaitingForPlayers;

        [SyncVar]
        public float CountdownRemaining;

        /// Index = player slot (0-3). Auto-synced to all clients.
        public readonly SyncList<float> PlayerHeights = new SyncList<float>();

        // ── Settings ─────────────────────────────────────────────────
        [SerializeField] private float _heightTarget    = 1000f;
        [SerializeField] private ShapeSpawner _spawner;

        // ── Server-side maps ─────────────────────────────────────────
        private readonly Dictionary<uint, int>        _netIdToSlot = new();
        private readonly Dictionary<uint, PlayerType> _playerTypes = new();
        private bool _gameOver;

        public float HeightTarget => _heightTarget;

        // ── Lifecycle ────────────────────────────────────────────────

        public override void OnStartServer()
        {
            for (int i = 0; i < 4; i++)
                PlayerHeights.Add(0f);

            GameEvents.OnHeightDelta += HandleHeightDelta;
            GameEvents.OnPlayerWon   += HandlePlayerWon;
        }

        public override void OnStopServer()
        {
            GameEvents.OnHeightDelta -= HandleHeightDelta;
            GameEvents.OnPlayerWon   -= HandlePlayerWon;
        }

        // ── Server API (called by ArenaNetworkManager) ───────────────

        [Server]
        public void ServerRegisterPlayer(uint playerNetId, int slot, PlayerType type)
        {
            if (slot < 0 || slot >= PlayerHeights.Count) return;
            _netIdToSlot[playerNetId] = slot;
            _playerTypes[playerNetId] = type;
            PlayerHeights[slot]       = 0f;
            Debug.Log($"[GameController] Registered player {playerNetId} → slot {slot} ({type})");
        }

        [Server]
        public void ServerSetState(GameState newState, float countdownSec)
        {
            CurrentState        = newState;
            CountdownRemaining  = countdownSec;

            switch (newState)
            {
                case GameState.Countdown:
                    RpcOnCountdown(countdownSec);
                    break;

                case GameState.Playing:
                    _spawner?.BeginSpawning();
                    RpcOnGameStarted();
                    break;

                case GameState.GameOver:
                    _spawner?.StopSpawning();
                    break;
            }
        }

        [Server]
        public void ServerEndGame(uint winnerNetId)
        {
            if (_gameOver) return;
            _gameOver           = true;
            CurrentState        = GameState.GameOver;
            _spawner?.StopSpawning();
            RpcOnGameOver(winnerNetId);
        }

        // ── Internal server handlers ─────────────────────────────────

        [Server]
        private void HandleHeightDelta(uint playerNetId, float delta)
        {
            if (_gameOver) return;
            if (!_netIdToSlot.TryGetValue(playerNetId, out int slot)) return;

            PlayerHeights[slot] = Mathf.Min(PlayerHeights[slot] + delta, _heightTarget);
            Debug.Log($"[GameController] Player {playerNetId} height → {PlayerHeights[slot]:F0}/{_heightTarget}");

            if (PlayerHeights[slot] >= _heightTarget)
                GameEvents.RaisePlayerWon(playerNetId);
        }

        [Server]
        private void HandlePlayerWon(uint playerNetId)
        {
            ServerEndGame(playerNetId);
        }

        // ── ClientRpcs ───────────────────────────────────────────────

        [ClientRpc]
        private void RpcOnCountdown(float duration)
        {
            Debug.Log($"[Client] Countdown: {duration}s");
            // UI: trigger countdown animation
        }

        [ClientRpc]
        private void RpcOnGameStarted()
        {
            Debug.Log("[Client] Game started!");
            // UI: hide lobby, show HUD
        }

        [ClientRpc]
        private void RpcOnGameOver(uint winnerNetId)
        {
            Debug.Log($"[Client] Game over. Winner netId={winnerNetId}");
            // UI: show result screen
        }

        // ── Client helper ────────────────────────────────────────────

        private void OnGameStateChanged(GameState oldVal, GameState newVal)
        {
            Debug.Log($"[Client] GameState: {oldVal} → {newVal}");
        }
    }
}
