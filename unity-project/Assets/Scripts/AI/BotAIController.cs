using Mirror;
using UnityEngine;
using System.Collections.Generic;
using ShapeRise.Core;
using ShapeRise.Gameplay;

namespace ShapeRise.AI
{
    /// <summary>
    /// Server-side Bot AI Controller.
    ///
    /// Decision algorithm (grid-based):
    ///   1. Divide arena into <_gridCols> columns.
    ///   2. Maintain a lightweight column grid: each cell stores (ShapeType, ShapeColor, count).
    ///   3. For the pending shape, score each column:
    ///        +10 if column top matches type AND color (perfect merge candidate)
    ///        + 2 if partial match
    ///        + 1 empty column (neutral — keeps tower spread low)
    ///   4. Pick highest scoring column.
    ///   5. Apply 17.5% error rate: intentional wrong column (keeps bot beatable).
    ///
    /// 60-second rule: Bot activation is handled by ArenaNetworkManager.FillWithBots().
    /// This script only handles decision-making once the bot is in-game.
    /// </summary>
    public class BotAIController : NetworkBehaviour
    {
        // ── Config ───────────────────────────────────────────────────
        [Header("AI Tuning")]
        [SerializeField] private float _decisionDelay = 1.2f;   // seconds to "think" before drop
        [SerializeField] private float _errorRate     = 0.175f;  // 17.5% — within 15-20% spec

        [Header("Grid")]
        [SerializeField] private int   _gridCols      = 9;
        [SerializeField] private float _arenaMinX     = -4.5f;
        [SerializeField] private float _arenaMaxX     = 4.5f;

        [Header("References")]
        [SerializeField] private ShapeSpawner _spawner;

        // ── Internal state ───────────────────────────────────────────
        private ShapeController _pendingShape;
        private float           _thinkTimer;
        private bool            _deciding;

        // Grid: column index → stack of (type, color)
        private readonly List<(ShapeType type, ShapeColor color)>[] _colStacks = null;
        private List<(ShapeType, ShapeColor)>[] ColStacks
        {
            get
            {
                // Lazy-init (can't use field initializer with serialized _gridCols)
                if (_colStacksCache == null)
                {
                    _colStacksCache = new List<(ShapeType, ShapeColor)>[_gridCols];
                    for (int i = 0; i < _gridCols; i++)
                        _colStacksCache[i] = new List<(ShapeType, ShapeColor)>(8);
                }
                return _colStacksCache;
            }
        }
        private List<(ShapeType, ShapeColor)>[] _colStacksCache;

        // ── Lifecycle ────────────────────────────────────────────────

        public override void OnStartServer()
        {
            GameEvents.OnMergeConfirmed += OnMergeConfirmed;
        }

        public override void OnStopServer()
        {
            GameEvents.OnMergeConfirmed -= OnMergeConfirmed;
        }

        // ── Assignment (called by server game flow) ──────────────────

        [Server]
        public void AssignShape(ShapeController shape)
        {
            _pendingShape = shape;
            _thinkTimer   = 0f;
            _deciding     = true;
        }

        // ── Decision loop ────────────────────────────────────────────

        private void Update()
        {
            if (!isServer || !_deciding || _pendingShape == null) return;

            _thinkTimer += Time.deltaTime;
            if (_thinkTimer < _decisionDelay) return;

            _deciding = false;
            MakeDecision();
        }

        [Server]
        private void MakeDecision()
        {
            bool  intentionalError = Random.value < _errorRate;
            int   chosenCol;

            if (intentionalError)
            {
                // Error: random column (avoids worst-case merge, makes bot beatable)
                chosenCol = Random.Range(0, _gridCols);
                Debug.Log($"[BotAI] netId={netId} → intentional error, col={chosenCol}");
            }
            else
            {
                // Optimal: find best scoring column
                chosenCol = FindBestColumn(_pendingShape.ShapeType, _pendingShape.ShapeColor);
                Debug.Log($"[BotAI] netId={netId} → optimal col={chosenCol}");
            }

            float dropX   = ColToWorldX(chosenCol);
            float dropRot = intentionalError ? Random.Range(0, 4) * 90f : 0f;

            _pendingShape.CmdDrop(dropX, dropRot);

            // Update internal grid model
            ColStacks[chosenCol].Add((_pendingShape.ShapeType, _pendingShape.ShapeColor));
            _pendingShape = null;
        }

        // ── Scoring ──────────────────────────────────────────────────

        [Server]
        private int FindBestColumn(ShapeType type, ShapeColor color)
        {
            int bestCol   = 0;
            int bestScore = int.MinValue;

            for (int col = 0; col < _gridCols; col++)
            {
                int score = ScoreColumn(col, type, color);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCol   = col;
                }
            }

            return bestCol;
        }

        [Server]
        private int ScoreColumn(int col, ShapeType type, ShapeColor color)
        {
            var stack = ColStacks[col];

            if (stack.Count == 0) return 1;  // Empty: low neutral priority

            // Score only the top of the stack (most likely merge candidate)
            var top = stack[stack.Count - 1];
            if (top.type == type && top.color == color) return 10; // Perfect match
            if (top.type == type || top.color == color) return 2;  // Partial match

            // Penalize tall columns slightly to discourage overflow
            return -stack.Count;
        }

        // ── Grid maintenance ─────────────────────────────────────────

        private void OnMergeConfirmed(uint netIdA, uint netIdB, ShapeType type, ShapeColor color)
        {
            // Remove one entry of the merged type+color from whichever column it's in
            for (int col = 0; col < _gridCols; col++)
            {
                var stack = ColStacks[col];
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i].type == type && stack[i].color == color)
                    {
                        stack.RemoveAt(i);
                        return; // Remove only one pair entry
                    }
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private float ColToWorldX(int col)
        {
            float t = _gridCols > 1 ? (float)col / (_gridCols - 1) : 0.5f;
            return Mathf.Lerp(_arenaMinX, _arenaMaxX, t);
        }
    }
}
