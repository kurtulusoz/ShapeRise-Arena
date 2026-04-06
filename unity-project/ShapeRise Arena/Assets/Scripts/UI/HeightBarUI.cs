using UnityEngine;
using UnityEngine.UI;
using ShapeRise.Networking;

namespace ShapeRise.UI
{
    /// <summary>
    /// Reads PlayerHeights SyncList from GameController and drives height bar UI.
    /// No server calls needed — SyncList is automatically kept in sync by Mirror.
    /// </summary>
    public class HeightBarUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameController _gameController;

        [Header("Per-player UI (index = player slot)")]
        [SerializeField] private Slider[] _bars;   // max 4
        [SerializeField] private Text[]   _labels; // optional labels like "P1: 420"

        private void Update()
        {
            if (_gameController == null) return;

            float target = _gameController.HeightTarget;
            if (target <= 0f) return;

            for (int i = 0; i < _bars.Length; i++)
            {
                if (_bars[i] == null) continue;

                float h = _gameController.PlayerHeights.Count > i
                    ? _gameController.PlayerHeights[i]
                    : 0f;

                _bars[i].value = h / target;

                if (_labels != null && i < _labels.Length && _labels[i] != null)
                    _labels[i].text = $"P{i + 1}: {Mathf.FloorToInt(h)}";
            }
        }
    }
}
