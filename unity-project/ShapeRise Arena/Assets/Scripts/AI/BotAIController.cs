using Mirror;
using UnityEngine;
using System.Collections.Generic;
using ShapeRise.Core;
using ShapeRise.Gameplay;

namespace ShapeRise.AI
{
    public class BotAIController : NetworkBehaviour
    {
        // ── Config ───────────────────────────────────────────────────
        [Header("AI Tuning")]
        [SerializeField] private float _decisionDelay = 1.2f;
        [SerializeField] private float _errorRate = 0.175f; // %17.5 spec

        [Header("Grid")]
        [SerializeField] private int _gridCols = 9;
        [SerializeField] private float _arenaMinX = -4.5f;
        [SerializeField] private float _arenaMaxX = 4.5f;

        [Header("References")]
        [SerializeField] private ShapeSpawner _spawner;

        // ── Internal state ───────────────────────────────────────────
        private ShapeController _pendingShape;
        private float _thinkTimer;
        private bool _deciding;

        // Senior Notu: Tuple isimlendirmesi (type, color) hatayı giderir.
        private List<(ShapeType type, ShapeColor color)>[] _colStacksCache;
        private List<(ShapeType type, ShapeColor color)>[] ColStacks
        {
            get
            {
                if (_colStacksCache == null)
                {
                    _colStacksCache = new List<(ShapeType type, ShapeColor color)>[_gridCols];
                    for (int i = 0; i < _gridCols; i++)
                        _colStacksCache[i] = new List<(ShapeType type, ShapeColor color)>(8);
                }
                return _colStacksCache;
            }
        }

        // ── Lifecycle ────────────────────────────────────────────────

        public override void OnStartServer()
        {
            GameEvents.OnMergeConfirmed += OnMergeConfirmed;
        }

        public override void OnStopServer()
        {
            GameEvents.OnMergeConfirmed -= OnMergeConfirmed;
        }

        [Server]
        public void AssignShape(ShapeController shape)
        {
            _pendingShape = shape;
            _thinkTimer = 0f;
            _deciding = true;
        }

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
            bool intentionalError = Random.value < _errorRate;
            int chosenCol;

            if (intentionalError)
            {
                chosenCol = Random.Range(0, _gridCols);
                Debug.Log($"[BotAI] intentional error, col={chosenCol}");
            }
            else
            {
                chosenCol = FindBestColumn(_pendingShape.ShapeType, _pendingShape.ShapeColor);
                Debug.Log($"[BotAI] optimal col={chosenCol}");
            }

            float dropX = ColToWorldX(chosenCol);
            float dropRot = intentionalError ? Random.Range(0, 4) * 90f : 0f;

            // Senior Notu: CmdDrop sunucu tarafında direkt çağrılabilir.
            _pendingShape.CmdDrop(dropX, dropRot);

            ColStacks[chosenCol].Add((_pendingShape.ShapeType, _pendingShape.ShapeColor));
            _pendingShape = null;
        }

        [Server]
        private int FindBestColumn(ShapeType type, ShapeColor color)
        {
            int bestCol = 0;
            int bestScore = int.MinValue;

            for (int col = 0; col < _gridCols; col++)
            {
                int score = ScoreColumn(col, type, color);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCol = col;
                }
            }
            return bestCol;
        }

        [Server]
        private int ScoreColumn(int col, ShapeType type, ShapeColor color)
        {
            var stack = ColStacks[col];
            if (stack.Count == 0) return 1;

            // Senior Notu: .type ve .color artık isimlendirilmiş tuple ile çalışır.
            var top = stack[stack.Count - 1];
            if (top.type == type && top.color == color) return 10;
            if (top.type == type || top.color == color) return 2;

            return -stack.Count;
        }

        private void OnMergeConfirmed(uint netIdA, uint netIdB, ShapeType type, ShapeColor color)
        {
            for (int col = 0; col < _gridCols; col++)
            {
                var stack = ColStacks[col];
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    // Senior Notu: Eşleşme kontrolü isimlendirilmiş tuple ile güncellendi.
                    if (stack[i].type == type && stack[i].color == color)
                    {
                        stack.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        private float ColToWorldX(int col)
        {
            float t = _gridCols > 1 ? (float)col / (_gridCols - 1) : 0.5f;
            return Mathf.Lerp(_arenaMinX, _arenaMaxX, t);
        }
    }
}