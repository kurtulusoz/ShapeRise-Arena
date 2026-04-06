using Mirror;
using UnityEngine;
using ShapeRise.Gameplay;

namespace ShapeRise.Gameplay
{
    /// <summary>
    /// Owned by the client. Reads local input, sends Commands to
    /// the server-owned ShapeController.
    ///
    /// Flow:
    ///   1. Server spawns ShapeController, retains authority.
    ///   2. Server calls TargetAssignShape on the owning client.
    ///   3. Client moves cursor locally (no prediction needed — shape is kinematic).
    ///   4. On drop input: CmdDrop sent → ShapeController.CmdDrop executed on server.
    /// </summary>
    public class PlayerInputHandler : NetworkBehaviour
    {
        // ── Config ───────────────────────────────────────────────────
        [Header("Cursor")]
        [SerializeField] private float _moveSpeed   = 6f;
        [SerializeField] private float _arenaMinX   = -4.5f;
        [SerializeField] private float _arenaMaxX   = 4.5f;

        [Header("Rotation")]
        [SerializeField] private float _rotateSpeed = 120f; // degrees/sec

        // ── State (client-local) ─────────────────────────────────────
        private ShapeController _pendingShape;
        private float           _cursorX;
        private float           _rotZ;

        // ── Unity update ─────────────────────────────────────────────

        private void Update()
        {
            if (!isOwned || _pendingShape == null) return;

            HandleMove();
            HandleRotate();
            HandleDrop();
        }

        private void HandleMove()
        {
            float h  = Input.GetAxis("Horizontal");
            _cursorX = Mathf.Clamp(_cursorX + h * _moveSpeed * Time.deltaTime,
                                    _arenaMinX, _arenaMaxX);
        }

        private void HandleRotate()
        {
            if (Input.GetKey(KeyCode.Q)) _rotZ += _rotateSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) _rotZ -= _rotateSpeed * Time.deltaTime;
        }

        private void HandleDrop()
        {
            bool dropPressed = Input.GetKeyDown(KeyCode.Space)
                            || Input.GetMouseButtonDown(0);
            if (!dropPressed) return;

            // Snap rotation to nearest 90°
            float snappedRot = Mathf.Round(_rotZ / 90f) * 90f;
            _pendingShape.CmdDrop(_cursorX, snappedRot);
            _pendingShape = null;
        }

        // ── Called by server to give client its next shape ────────────

        [TargetRpc]
        public void TargetAssignShape(NetworkConnection target, NetworkIdentity shapeIdentity)
        {
            _pendingShape = shapeIdentity.GetComponent<ShapeController>();
            _cursorX      = shapeIdentity.transform.position.x;
            _rotZ         = 0f;
            Debug.Log($"[Input] Shape assigned: {_pendingShape.ShapeType}/{_pendingShape.ShapeColor}");
        }
    }
}
