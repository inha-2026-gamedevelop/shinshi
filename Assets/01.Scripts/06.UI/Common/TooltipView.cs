// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>칸 옆에 제목과 설명을 띄우는 풍선이며 부모 영역 밖으로 나가지 않게 위치를 보정한다</summary>
    public sealed class TooltipView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float EDGE_MARGIN = 8f;

        [Header("표시")]
        [SerializeField] private RectTransform _panel      = null;
        [SerializeField] private TMP_Text      _titleLabel = null;
        [SerializeField] private TMP_Text      _bodyLabel  = null;
        [SerializeField] private Vector2       _offset     = new Vector2(16f, -16f);

        private RectTransform _area   = null;
        private Canvas        _canvas = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_panel != null)
            {
                _area = _panel.parent as RectTransform;
                Canvas canvas = _panel.GetComponentInParent<Canvas>(true);
                _canvas = (canvas != null) ? canvas.rootCanvas : null;
            }

            Hide();
        }

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

        // 화면 좌표를 그대로 position에 넣으면 Screen Space - Overlay에서만 맞는다.
        // HUD 캔버스를 카메라 기준으로 바꿔도 맞도록 부모 영역의 로컬 좌표로 바꿔서 놓는다
        private void Place(Vector2 screenPoint)
        {
            if ((_area == null) || (_canvas == null))
            {
                return;
            }

            Camera eventCamera = (_canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : _canvas.worldCamera;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screenPoint, eventCamera, out Vector2 local))
            {
                return;
            }

            Rect    bounds = _area.rect;
            Vector2 size   = _panel.rect.size;
            Vector2 pivot  = _panel.pivot;
            Vector2 target = local + _offset;

            float minX = bounds.xMin + EDGE_MARGIN + (size.x * pivot.x);
            float maxX = bounds.xMax - EDGE_MARGIN - (size.x * (1f - pivot.x));
            float minY = bounds.yMin + EDGE_MARGIN + (size.y * pivot.y);
            float maxY = bounds.yMax - EDGE_MARGIN - (size.y * (1f - pivot.y));

            target.x = Mathf.Clamp(target.x, minX, Mathf.Max(minX, maxX));
            target.y = Mathf.Clamp(target.y, minY, Mathf.Max(minY, maxY));
            _panel.localPosition = new Vector3(target.x, target.y, 0f);
        }
    }
}
