using Mirror;
using UnityEngine;
using ShapeRise.Core;

// Not: Eğer hala NetworkTransform hatası alırsan, Mirror sürümüne göre 
// 'using Mirror.Components;' eklemek gerekebilir ancak genellikle 'using Mirror;' yeterlidir.

namespace ShapeRise.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(NetworkIdentity))]
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
        [SerializeField] private float _fallSpeed = 3f;
        [SerializeField] private float _arenaHalfWidth = 4.5f;

        [Header("Visuals")]
        [SerializeField] private SpriteRenderer _sprite;

        private Rigidbody2D _rb;

        // Renk haritası merkezi bir yerden (Core) yönetilebilir ancak şimdilik burada kalabilir.
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
            _rb = GetComponent<Rigidbody2D>();
            // Başlangıçta fizik kapalı, sunucu komutu bekler.
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }

        public override void OnStartClient()
        {
            ApplyVisuals();
        }

        // ── Server initialization (called by ShapeSpawner) ───────────

        [Server]
        public void ServerInit(ShapeType type, ShapeColor color, uint ownerNetId)
        {
            ShapeType = type;
            ShapeColor = color;
            OwnerPlayerNetId = ownerNetId;
            IsMerged = false;
            IsDropped = false;
        }

        // ── Drop command (client sends, server validates & executes) ──

        [Command(requiresAuthority = false)]
        public void CmdDrop(float xPos, float rotationZ)
        {
            if (IsMerged || IsDropped) return;

            // Sunucu tarafı doğrulaması (Anti-cheat): Sınırları koru.
            xPos = Mathf.Clamp(xPos, -_arenaHalfWidth, _arenaHalfWidth);

            transform.SetPositionAndRotation(
                new Vector3(xPos, transform.position.y, 0f),
                Quaternion.Euler(0f, 0f, rotationZ));

            _rb.bodyType = RigidbodyType2D.Dynamic;
            // Not: Unity 6+ sürümlerinde 'linearVelocity' yerine 'velocity' de kullanılabilir.
            _rb.velocity = new Vector2(0f, -_fallSpeed); // linearVelocity -> velocity
            IsDropped = true;

            RpcPlayDropEffect(xPos);
        }

        // ── Merge resolution (called by MergeDetector) ───────────────

        [Server]
        public void ServerMarkMerged()
        {
            IsMerged = true;
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.velocity = Vector2.zero; // linearVelocity -> velocity
        }

        // ── ClientRpcs ───────────────────────────────────────────────

        [ClientRpc]
        private void RpcPlayDropEffect(float xPos)
        {
            Debug.Log($"[Client] Shape dropped at x={xPos:F2}");
        }

        // ── SyncVar hooks ────────────────────────────────────────────

        // Hook imzaları: Mirror sürümüne göre (OldValue, NewValue) şeklinde olmalıdır.
        private void OnShapeTypeChanged(ShapeType oldVal, ShapeType newVal) => ApplyVisuals();
        private void OnShapeColorChanged(ShapeColor oldVal, ShapeColor newVal) => ApplyVisuals();

        private void ApplyVisuals()
        {
            if (_sprite == null) return;
            int idx = (int)ShapeColor;
            if (idx >= 0 && idx < ColorMap.Length)
                _sprite.color = ColorMap[idx];
        }

        public ushort MergeKey => (ushort)((byte)ShapeType << 8 | (byte)ShapeColor);
    }
}