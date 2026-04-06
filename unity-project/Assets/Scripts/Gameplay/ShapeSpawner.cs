using Mirror;
using UnityEngine;
using System.Collections.Generic;
using ShapeRise.Core;

namespace ShapeRise.Gameplay
{
    /// <summary>
    /// Server-authoritative shape spawner.
    ///
    /// - Runs only on the server (isServer guard everywhere).
    /// - Maintains per-lane spawn queues so each player always has a pending shape.
    /// - Fires GameEvents.OnShapeSpawned for MergeDetector and BotAI listeners.
    /// - Shapes are NetworkServer.Spawn'd so clients receive them automatically.
    ///
    /// Lane layout (default 4 lanes):
    ///   Lane 0 → x = -5.0  (P1)
    ///   Lane 1 → x = -1.8  (P2)
    ///   Lane 2 → x =  1.8  (P3)
    ///   Lane 3 → x =  5.0  (P4)
    /// </summary>
    public class ShapeSpawner : NetworkBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────
        [Header("Prefabs — index matches ShapeType enum (0=Tri,1=Sq,2=Rect,3=Circ)")]
        [SerializeField] private GameObject[] _shapePrefabs;

        [Header("Spawn Settings")]
        [SerializeField] private float _spawnHeight    = 9f;
        [SerializeField] private float _spawnInterval  = 3.5f;   // seconds between auto-spawns

        [Header("Lane X Positions (one per player slot)")]
        [SerializeField] private float[] _laneX = { -5f, -1.8f, 1.8f, 5f };

        // ── State (server only) ──────────────────────────────────────
        private bool  _active;
        private float _timer;

        // Registered player netIds per lane (lane → ownerNetId)
        private readonly Dictionary<int, uint> _laneOwners = new();

        // Live shapes (netId → controller) for external access
        private readonly Dictionary<uint, ShapeController> _liveShapes = new();

        private static readonly ShapeType[]  AllTypes  = (ShapeType[]) System.Enum.GetValues(typeof(ShapeType));
        private static readonly ShapeColor[] AllColors = (ShapeColor[])System.Enum.GetValues(typeof(ShapeColor));

        // ── Public API ───────────────────────────────────────────────

        [Server]
        public void RegisterLaneOwner(int lane, uint ownerNetId)
        {
            if (lane < 0 || lane >= _laneX.Length) return;
            _laneOwners[lane] = ownerNetId;
        }

        [Server]
        public void BeginSpawning()
        {
            _active = true;
            _timer  = _spawnInterval; // spawn immediately on first tick
            Debug.Log("[ShapeSpawner] Spawning started.");
        }

        [Server]
        public void StopSpawning()
        {
            _active = false;
            Debug.Log("[ShapeSpawner] Spawning stopped.");
        }

        // ── Update loop (server only) ────────────────────────────────

        private void Update()
        {
            if (!isServer || !_active) return;

            _timer += Time.deltaTime;
            if (_timer < _spawnInterval) return;

            _timer = 0f;
            SpawnForAllLanes();
        }

        // ── Spawn logic ──────────────────────────────────────────────

        [Server]
        private void SpawnForAllLanes()
        {
            for (int lane = 0; lane < _laneX.Length; lane++)
                SpawnRandom(lane);
        }

        /// <summary>Spawn a random shape in the given lane.</summary>
        [Server]
        public ShapeController SpawnRandom(int lane)
        {
            ShapeType  type  = AllTypes[Random.Range(0, AllTypes.Length)];
            ShapeColor color = AllColors[Random.Range(0, AllColors.Length)];
            return Spawn(lane, type, color);
        }

        /// <summary>Spawn a specific type/color in the given lane.</summary>
        [Server]
        public ShapeController Spawn(int lane, ShapeType type, ShapeColor color)
        {
            if (!ValidateLane(lane)) return null;

            int prefabIdx = (int)type;
            if (prefabIdx >= _shapePrefabs.Length || _shapePrefabs[prefabIdx] == null)
            {
                Debug.LogError($"[ShapeSpawner] Missing prefab for ShapeType.{type}");
                return null;
            }

            uint   ownerNetId = _laneOwners.TryGetValue(lane, out uint id) ? id : 0u;
            Vector3 pos       = new Vector3(_laneX[lane], _spawnHeight, 0f);
            GameObject go     = Instantiate(_shapePrefabs[prefabIdx], pos, Quaternion.identity);

            ShapeController shape = go.GetComponent<ShapeController>();
            shape.ServerInit(type, color, ownerNetId);
            NetworkServer.Spawn(go);

            _liveShapes[shape.netId] = shape;
            GameEvents.RaiseShapeSpawned(shape.netId, type, color, pos.x);

            Debug.Log($"[ShapeSpawner] Spawned {type}/{color} lane={lane} netId={shape.netId}");
            return shape;
        }

        // ── Cleanup ──────────────────────────────────────────────────

        [Server]
        public void RemoveShape(uint netId) => _liveShapes.Remove(netId);

        [Server]
        public bool TryGetShape(uint netId, out ShapeController shape)
            => _liveShapes.TryGetValue(netId, out shape);

        [Server]
        public int LiveShapeCount => _liveShapes.Count;

        // ── Helpers ──────────────────────────────────────────────────

        private bool ValidateLane(int lane)
        {
            if (lane >= 0 && lane < _laneX.Length) return true;
            Debug.LogError($"[ShapeSpawner] Invalid lane index {lane}");
            return false;
        }
    }
}
