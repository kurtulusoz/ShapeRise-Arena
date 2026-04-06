using Mirror;
using UnityEngine;
using System.Collections.Generic;
using ShapeRise.Core;

namespace ShapeRise.Networking
{
    /// <summary>
    /// Server-authoritative NetworkManager for ShapeRise Arena.
    ///
    /// Responsibilities:
    ///   - Accept / reject player connections
    ///   - Track player slots and assign them to PlayerController prefabs
    ///   - Bot fill: after <_botFillTimeout> seconds with &lt; maxPlayers humans, inject bots
    ///   - Delegate game state (SyncVars / ClientRpcs) to GameController NetworkBehaviour
    ///
    /// Mirror note: NetworkManager extends MonoBehaviour, NOT NetworkBehaviour.
    /// SyncVar / ClientRpc live in GameController (separate NetworkBehaviour).
    /// </summary>
    public class ArenaNetworkManager : NetworkManager
    {
        // ── Inspector ────────────────────────────────────────────────
        [Header("Game Settings")]
        [SerializeField] private int   _maxPlayers      = 4;
        [SerializeField] private float _botFillTimeout  = 60f;
        [SerializeField] private float _countdownTime   = 5f;

        [Header("Prefabs")]
        [SerializeField] private GameObject _playerPrefab;
        [SerializeField] private GameObject _botPrefab;
        [SerializeField] private GameObject _gameControllerPrefab; // NetworkBehaviour prefab

        // ── Server state ─────────────────────────────────────────────
        private GameState  _state     = GameState.WaitingForPlayers;
        private float      _waitTimer;
        private GameController _gameController;

        // slot → connection mapping
        private readonly Dictionary<NetworkConnectionToClient, int> _connToSlot = new();
        private int _nextSlot;

        // ── Server lifecycle ─────────────────────────────────────────

        public override void OnStartServer()
        {
            base.OnStartServer();
            _state     = GameState.WaitingForPlayers;
            _waitTimer = 0f;
            _nextSlot  = 0;
            Debug.Log("[ArenaNetworkManager] Server started.");
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);

            if (_state != GameState.WaitingForPlayers)
            {
                Debug.LogWarning($"[Server] Rejected late join from {conn.address}");
                conn.Disconnect();
                return;
            }

            if (numPlayers >= _maxPlayers)
            {
                Debug.LogWarning("[Server] Server full. Rejecting connection.");
                conn.Disconnect();
            }
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            Transform startPos = GetStartPosition();
            Vector3 pos        = startPos != null ? startPos.position : Vector3.zero;
            Quaternion rot     = startPos != null ? startPos.rotation : Quaternion.identity;

            GameObject playerGO = Instantiate(_playerPrefab, pos, rot);
            NetworkServer.AddPlayerForConnection(conn, playerGO);

            int slot               = _nextSlot++;
            _connToSlot[conn]      = slot;
            uint netId             = playerGO.GetComponent<NetworkIdentity>().netId;

            _gameController?.ServerRegisterPlayer(netId, slot, PlayerType.Human);

            Debug.Log($"[Server] Player connected → slot {slot}, netId {netId}");
            TryStartCountdown();
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            _connToSlot.Remove(conn);
            base.OnServerDisconnect(conn);

            if (_state == GameState.Playing && numPlayers == 0)
                _gameController?.ServerEndGame(0);
        }

        // ── Game flow ────────────────────────────────────────────────

        private void TryStartCountdown()
        {
            if (numPlayers >= _maxPlayers)
                StartCountdown();
        }

        private void StartCountdown()
        {
            if (_state != GameState.WaitingForPlayers) return;
            _state = GameState.Countdown;

            SpawnGameController();
            _gameController.ServerSetState(GameState.Countdown, _countdownTime);
            Invoke(nameof(StartGame), _countdownTime);
            Debug.Log("[Server] Countdown started.");
        }

        private void StartGame()
        {
            _state = GameState.Playing;
            _gameController.ServerSetState(GameState.Playing, 0f);
            Debug.Log("[Server] Game started!");
        }

        private void Update()
        {
            if (!NetworkServer.active) return;
            if (_state != GameState.WaitingForPlayers) return;
            if (numPlayers == 0) return;

            _waitTimer += Time.deltaTime;
            if (_waitTimer >= _botFillTimeout)
                FillWithBots();
        }

        private void FillWithBots()
        {
            int needed = _maxPlayers - numPlayers;
            for (int i = 0; i < needed; i++)
            {
                Transform startPos  = GetStartPosition();
                Vector3   pos       = startPos != null ? startPos.position : Vector3.zero;
                Quaternion rot      = startPos != null ? startPos.rotation : Quaternion.identity;

                GameObject botGO    = Instantiate(_botPrefab, pos, rot);
                NetworkServer.Spawn(botGO);

                int  slot   = _nextSlot++;
                uint netId  = botGO.GetComponent<NetworkIdentity>().netId;
                _gameController?.ServerRegisterPlayer(netId, slot, PlayerType.Bot);

                GameEvents.RaiseBotFilled(netId);
                Debug.Log($"[Server] Bot spawned → slot {slot}, netId {netId}");
            }

            StartCountdown();
        }

        private void SpawnGameController()
        {
            if (_gameController != null) return;
            GameObject go = Instantiate(_gameControllerPrefab);
            NetworkServer.Spawn(go);
            _gameController = go.GetComponent<GameController>();
        }
    }
}
