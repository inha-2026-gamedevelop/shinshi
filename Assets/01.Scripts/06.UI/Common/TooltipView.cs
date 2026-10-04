// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>칸 옆에 제목과 설명을 띄우는 풍선이며 화면 밖으로 나가지 않게 위치를 보정한다</summary>
    public sealed class TooltipView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float SCREEN_MARGIN = 8f;

        [Header("표시")]
        [SerializeField] private RectTransform _panel      = null;
        [SerializeField] private TMP_Text      _titleLabel = null;
        [SerializeField] private TMP_Text      _bodyLabel  = null;
        [SerializeField] private Vector2       _offset     = new Vector2(16f, -16f);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake() { Hide(); }

        /****************************************
        *                Methods
        ****************************************/

        public void Show(string title, string body, Vector2 screenPoint)
        {
            if (_panel == null)
            {
                return;
            }

            if (_titleLabel != null)
            {
                _titleLabel.text = title;
            }

            if (_bodyLabel != null)
            {
                _bodyLabel.text = body;
            }

            _panel.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
            Place(screenPoint);
        }

        public void Hide()
        {
            if (_panel != null)
            {
                _panel.gameObject.SetActive(false);
            }
        }

        private void Place(Vector2 screenPoint)
        {
            Canvas canvas = _panel.GetComponentInParent<Canvas>();
            float scale = (canvas != null) ? canvas.scaleFactor : 1f;
            Vector2 size = _panel.rect.size * scale;
            Vector2 pivot = _panel.pivot;
            Vector2 target = screenPoint + (_offset * scale);

            float minX = SCREEN_MARGIN + (size.x * pivot.x);
            float maxX = Screen.width - SCREEN_MARGIN - (size.x * (1f - pivot.x));
            float minY = SCREEN_MARGIN + (size.y * pivot.y);
            float maxY = Screen.height - SCREEN_MARGIN - (size.y * (1f - pivot.y));

            target.x = Mathf.Clamp(target.x, minX, Mathf.Max(minX, maxX));
            target.y = Mathf.Clamp(target.y, minY, Mathf.Max(minY, maxY));
            _panel.position = target;
        }
    }
}
