using Mirror;
using UnityEngine;
using System.Collections.Generic;
using ShapeRise.Core;

namespace ShapeRise.Gameplay
{
    public class ShapeSpawner : NetworkBehaviour
    {
        [Header("Prefabs — index matches ShapeType enum (0=Tri,1=Sq,2=Rect,3=Circ)")]
        [SerializeField] private GameObject[] _shapePrefabs;

        [Header("Spawn Settings")]
        [SerializeField] private float _spawnHeight = 9f;
        [SerializeField] private float _spawnInterval = 3.5f;

        [Header("Lane X Positions (one per player slot)")]
        [SerializeField] private float[] _laneX = { -5f, -1.8f, 1.8f, 5f };

        private bool _active;
        private float _timer;

        private readonly Dictionary<int, uint> _laneOwners = new();
        private readonly Dictionary<uint, ShapeController> _liveShapes = new();

        private static readonly ShapeType[] AllTypes = (ShapeType[])System.Enum.GetValues(typeof(ShapeType));
        private static readonly ShapeColor[] AllColors = (ShapeColor[])System.Enum.GetValues(typeof(ShapeColor));

        // --- Server API ---

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
            _timer = _spawnInterval;
            Debug.Log("[ShapeSpawner] Spawning started.");
        }

        [Server]
        public void StopSpawning()
        {
            _active = false;
            Debug.Log("[ShapeSpawner] Spawning stopped.");
        }

        // --- Update loop ---

        private void Update()
        {
            // Senior Notu: isServer kontrolü burada yeterlidir. 
            // Metotların başındaki [Server] etiketi ise dışarıdan hatalı çağrıları engeller.
            if (!isServer || !_active) return;

            _timer += Time.deltaTime;
            if (_timer < _spawnInterval) return;

            _timer = 0f;
            SpawnForAllLanes();
        }

        [Server]
        private void SpawnForAllLanes()
        {
            for (int lane = 0; lane < _laneX.Length; lane++)
                SpawnRandom(lane);
        }

        [Server]
        public ShapeController SpawnRandom(int lane)
        {
            ShapeType type = AllTypes[Random.Range(0, AllTypes.Length)];
            ShapeColor color = AllColors[Random.Range(0, AllColors.Length)];
            return Spawn(lane, type, color);
        }

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

            uint ownerNetId = _laneOwners.TryGetValue(lane, out uint id) ? id : 0u;
            Vector3 pos = new Vector3(_laneX[lane], _spawnHeight, 0f);

            // Sunucu üzerinde nesneyi oluştur
            GameObject go = Instantiate(_shapePrefabs[prefabIdx], pos, Quaternion.identity);
            ShapeController shape = go.GetComponent<ShapeController>();

            // Önce başlat, sonra ağa yay (Spawn)
            shape.ServerInit(type, color, ownerNetId);
            NetworkServer.Spawn(go);

            _liveShapes[shape.netId] = shape;
            GameEvents.RaiseShapeSpawned(shape.netId, type, color, pos.x);

            return shape;
        }

        // --- Server Helpers ---

        [Server]
        public void RemoveShape(uint netId) => _liveShapes.Remove(netId);

        [Server]
        public bool TryGetShape(uint netId, out ShapeController shape)
            => _liveShapes.TryGetValue(netId, out shape);

        // Değişkenler üzerinde [Server] kullanılamadığı için property tercih ettik
        public int LiveShapeCount => isServer ? _liveShapes.Count : 0;

        private bool ValidateLane(int lane)
        {
            if (lane >= 0 && lane < _laneX.Length) return true;
            Debug.LogError($"[ShapeSpawner] Invalid lane index {lane}");
            return false;
        }
    }
}