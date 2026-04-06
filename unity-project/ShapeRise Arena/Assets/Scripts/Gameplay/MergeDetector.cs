using Mirror;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using ShapeRise.Core;

namespace ShapeRise.Gameplay
{
    /// <summary>
    /// Server-only lightweight merge detection system.
    ///
    /// Algorithm: Spatial Bucket Hash
    ///   - Arena divided into a grid of cells (_cellSize world units).
    ///   - Each Update pass: rebuild buckets → check pairs within same + adjacent cells.
    ///   - Complexity: O(n) rebuild + O(n * avg_per_cell) detection ≪ O(n²) brute force.
    ///
    /// Merge Rule (from game design):
    ///   ShapeType MUST match AND ShapeColor MUST match AND OwnerPlayerNetId MUST match.
    ///   Shapes belonging to different players cannot merge (they collide physically).
    ///
    /// On merge:
    ///   1. Both shapes marked IsMerged=true (stops physics, prevents re-detection)
    ///   2. GameEvents.RaiseMergeConfirmed → HeightTracker adds height reward
    ///   3. Short delay → NetworkServer.Destroy (lets clients play merge VFX)
    /// </summary>
    public class MergeDetector : NetworkBehaviour
    {
        // ── Config ───────────────────────────────────────────────────
        [Header("Detection")]
        [SerializeField] private float _mergeRadius    = 0.55f;  // world units
        [SerializeField] private float _checkInterval  = 0.08f;  // seconds (≈12 checks/s)
        [SerializeField] private float _cellSize       = 1.1f;   // spatial bucket size

        [Header("Reward")]
        [SerializeField] private float _mergeHeightPts = 50f;

        [Header("References")]
        [SerializeField] private ShapeSpawner _spawner;

        // ── Internal state (server only) ─────────────────────────────
        private readonly Dictionary<long, List<uint>> _buckets     = new(64);
        private readonly HashSet<ulong>               _mergedPairs = new(32);
        private readonly List<uint>                   _toRemove    = new(8);

        private float _timer;

        // ── Lifecycle ────────────────────────────────────────────────

        public override void OnStartServer()
        {
            GameEvents.OnMergeConfirmed += OnMergeConfirmedCleanup;
        }

        public override void OnStopServer()
        {
            GameEvents.OnMergeConfirmed -= OnMergeConfirmedCleanup;
        }

        // ── Detection loop ───────────────────────────────────────────

        private void Update()
        {
            if (!isServer) return;

            _timer += Time.deltaTime;
            if (_timer < _checkInterval) return;
            _timer = 0f;

            RebuildBuckets();
            SweepForMerges();
        }

        [Server]
        private void RebuildBuckets()
        {
            _buckets.Clear();

            // Iterate over all spawned network objects that are ShapeControllers
            foreach (var kv in NetworkServer.spawned)
            {
                ShapeController shape = kv.Value.GetComponent<ShapeController>();
                if (shape == null || shape.IsMerged || !shape.IsDropped) continue;

                long key = BucketKey(shape.transform.position);
                if (!_buckets.TryGetValue(key, out var list))
                {
                    list = new List<uint>(4);
                    _buckets[key] = list;
                }
                list.Add(kv.Key);
            }
        }

        [Server]
        private void SweepForMerges()
        {
            foreach (var kv in _buckets)
            {
                List<uint> cell = kv.Value;

                // Check pairs within same bucket
                for (int i = 0; i < cell.Count; i++)
                    for (int j = i + 1; j < cell.Count; j++)
                        TryMerge(cell[i], cell[j]);

                // Check pairs with the 4 orthogonal neighbor buckets (avoids double-checking diagonals)
                int cx = (int)(kv.Key >> 32);
                int cy = (int)(kv.Key & 0xFFFFFFFF);
                CheckNeighbor(cell, cx + 1, cy);
                CheckNeighbor(cell, cx,     cy + 1);
                CheckNeighbor(cell, cx + 1, cy + 1);
                CheckNeighbor(cell, cx + 1, cy - 1);
            }
        }

        [Server]
        private void CheckNeighbor(List<uint> cellA, int nx, int ny)
        {
            long nKey = MakeKey(nx, ny);
            if (!_buckets.TryGetValue(nKey, out var cellB)) return;

            foreach (uint idA in cellA)
                foreach (uint idB in cellB)
                    TryMerge(idA, idB);
        }

        [Server]
        private void TryMerge(uint idA, uint idB)
        {
            if (idA == idB) return;

            ulong pair = PairKey(idA, idB);
            if (_mergedPairs.Contains(pair)) return;

            if (!NetworkServer.spawned.TryGetValue(idA, out var identA) ||
                !NetworkServer.spawned.TryGetValue(idB, out var identB)) return;

            var shapeA = identA.GetComponent<ShapeController>();
            var shapeB = identB.GetComponent<ShapeController>();

            if (!CanMerge(shapeA, shapeB)) return;

            _mergedPairs.Add(pair);
            ExecuteMerge(shapeA, shapeB);
        }

        // ── Merge logic ──────────────────────────────────────────────

        [Server]
        private bool CanMerge(ShapeController a, ShapeController b)
        {
            if (a == null || b == null)     return false;
            if (a.IsMerged || b.IsMerged)  return false;
            if (!a.IsDropped || !b.IsDropped) return false;

            // Same owner (different players cannot merge)
            if (a.OwnerPlayerNetId != b.OwnerPlayerNetId) return false;

            // Same type + same color
            if (a.MergeKey != b.MergeKey) return false;

            // Proximity check
            float dist = Vector2.Distance(a.transform.position, b.transform.position);
            return dist <= _mergeRadius;
        }

        [Server]
        private void ExecuteMerge(ShapeController shapeA, ShapeController shapeB)
        {
            uint owner = shapeA.OwnerPlayerNetId;

            shapeA.ServerMarkMerged();
            shapeB.ServerMarkMerged();

            _spawner?.RemoveShape(shapeA.netId);
            _spawner?.RemoveShape(shapeB.netId);

            GameEvents.RaiseMergeConfirmed(shapeA.netId, shapeB.netId, shapeA.ShapeType, shapeA.ShapeColor);
            GameEvents.RaiseHeightDelta(owner, _mergeHeightPts);

            Vector3 midpoint = (shapeA.transform.position + shapeB.transform.position) * 0.5f;
            RpcOnMergeVFX(midpoint, shapeA.ShapeColor);

            StartCoroutine(DestroyAfterDelay(shapeA.gameObject, shapeB.gameObject, 0.2f));

            Debug.Log($"[MergeDetector] MERGE {shapeA.ShapeType}/{shapeA.ShapeColor} " +
                      $"(netId {shapeA.netId} + {shapeB.netId}) owner={owner} +{_mergeHeightPts}pts");
        }

        private IEnumerator DestroyAfterDelay(GameObject a, GameObject b, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (a != null) NetworkServer.Destroy(a);
            if (b != null) NetworkServer.Destroy(b);
        }

        [Server]
        private void OnMergeConfirmedCleanup(uint netIdA, uint netIdB, ShapeType _, ShapeColor __)
        {
            // Cleanup stale pair keys periodically to prevent set growth
            // (pairs auto-expire when shapes are destroyed)
        }

        // ── ClientRpcs ───────────────────────────────────────────────

        [ClientRpc]
        private void RpcOnMergeVFX(Vector3 pos, ShapeColor color)
        {
            // Hook: spawn particle system, play merge sound, trigger camera shake
            Debug.Log($"[Client] Merge VFX at {pos}, color={color}");
        }

        // ── Spatial hashing helpers ──────────────────────────────────

        private long BucketKey(Vector3 p)
            => MakeKey(Mathf.FloorToInt(p.x / _cellSize), Mathf.FloorToInt(p.y / _cellSize));

        private static long MakeKey(int x, int y)
            => ((long)(uint)x << 32) | (uint)y;

        /// <summary>Order-independent pair key (prevents duplicate checks).</summary>
        private static ulong PairKey(uint a, uint b)
            => a < b ? ((ulong)a << 32) | b : ((ulong)b << 32) | a;
    }
}
