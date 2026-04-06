using Mirror;
using UnityEngine;
using ShapeRise.Core;

namespace ShapeRise.Gameplay
{
    /// <summary>
    /// Networked shape object. Server owns and controls all physics.
    /// Clients receive visual state via SyncVars.
    ///
    /// Drop flow:
    ///   1. Server spawns shape (kinematic, waiting)
    ///   2. Client's PlayerInputHandler sends CmdDrop → validated server-side
    ///   3. Server enables gravity, shape falls
    ///   4. MergeDetector detects collision, calls ServerMarkMerged()
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(NetworkTransform))]
    public class ShapeController : NetworkBehaviour
    {
        // ── Synced identity ──────────────────────────────────────────
        [SyncVar(hook = nameof(OnShapeTypeChanged))]
        public ShapeType ShapeType;

        [SyncVar(hook = nameof(OnShapeColorChanged))]
        public ShapeColor ShapeColor;

        [SyncVar]
        public uint OwnerPlayerNetId;

        [SyncVar]
        public bool IsMerged = false;

        [SyncVar]
        public bool IsDropped = false;

        // ── Config ───────────────────────────────────────────────────
        [Header("Physics")]
        [SerializeField] private float _fallSpeed        = 3f;
        [SerializeField] private float _arenaHalfWidth   = 4.5f;

        [Header("Visuals")]
        [SerializeField] private SpriteRenderer _sprite;

        private Rigidbody2D _rb;

        private static readonly Color[] ColorMap =
        {
            new Color(0.90f, 0.20f, 0.20f), // Red
            new Color(0.20f, 0.45f, 0.90f), // Blue
            new Color(0.20f, 0.78f, 0.35f), // Green
            new Color(0.95f, 0.85f, 0.15f), // Yellow
            new Color(0.60f, 0.15f, 0.85f)  // Purple
        };

        // ── Unity lifecycle ──────────────────────────────────────────

        private void Awake()
        {
            _rb          = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic; // always start static
        }

        public override void OnStartClient()
        {
            ApplyVisuals();
        }

        // ── Server initialization (called by ShapeSpawner) ───────────

        [Server]
        public void ServerInit(ShapeType type, ShapeColor color, uint ownerNetId)
        {
            ShapeType        = type;
            ShapeColor       = color;
            OwnerPlayerNetId = ownerNetId;
            IsMerged         = false;
            IsDropped        = false;
        }

        // ── Drop command (client sends, server validates & executes) ──

        /// <summary>
        /// Called by owning player's PlayerInputHandler.
        /// requiresAuthority=false: server retains authority over shape,
        /// but the owning client can still invoke this command.
        /// </summary>
        [Command(requiresAuthority = false)]
        public void CmdDrop(float xPos, float rotationZ,
                            NetworkConnectionToClient sender = null)
        {
            if (IsMerged || IsDropped) return;

            // Server-side validation: clamp X to arena bounds
            xPos = Mathf.Clamp(xPos, -_arenaHalfWidth, _arenaHalfWidth);

            transform.SetPositionAndRotation(
                new Vector3(xPos, transform.position.y, 0f),
                Quaternion.Euler(0f, 0f, rotationZ));

            _rb.bodyType      = RigidbodyType2D.Dynamic;
            _rb.linearVelocity = new Vector2(0f, -_fallSpeed);
            IsDropped         = true;

            RpcPlayDropEffect(xPos);
        }

        // ── Merge resolution (called by MergeDetector) ───────────────

        [Server]
        public void ServerMarkMerged()
        {
            IsMerged         = true;
            _rb.bodyType     = RigidbodyType2D.Kinematic;
            _rb.linearVelocity = Vector2.zero;
        }

        // ── ClientRpcs ───────────────────────────────────────────────

        [ClientRpc]
        private void RpcPlayDropEffect(float xPos)
        {
            // Hook: trigger drop sound / particle on the client
            Debug.Log($"[Client] Shape dropped at x={xPos:F2}");
        }

        // ── SyncVar hooks ────────────────────────────────────────────

        private void OnShapeTypeChanged(ShapeType _, ShapeType __)   => ApplyVisuals();
        private void OnShapeColorChanged(ShapeColor _, ShapeColor __) => ApplyVisuals();

        private void ApplyVisuals()
        {
            if (_sprite == null) return;
            int idx = (int)ShapeColor;
            if (idx >= 0 && idx < ColorMap.Length)
                _sprite.color = ColorMap[idx];
        }

        // ── Merge key ────────────────────────────────────────────────

        /// <summary>
        /// Compact key: high byte = ShapeType, low byte = ShapeColor.
        /// Two shapes can merge iff their keys are equal AND same owner.
        /// </summary>
        public ushort MergeKey => (ushort)((byte)ShapeType << 8 | (byte)ShapeColor);
    }
}
