// Unity
using UnityEngine;
using UnityEngine.EventSystems;

namespace Shinshi.UI
{
    /// <summary>창 제목줄에 붙여 마우스로 끌어 창을 옮긴다. 창 전체가 부모 영역 안에 남도록 막는다</summary>
    public sealed class UiWindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("참조")]
        [SerializeField] private RectTransform _window = null;

        private RectTransform _area          = null;
        private Vector2       _pointerOffset = Vector2.zero;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_window != null)
            {
                _area = _window.parent as RectTransform;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if ((_window == null) || (_area == null))
            {
                return;
            }

            // 잡은 지점이 창 기준점에서 얼마나 떨어졌는지 기억해 두어야 끌 때 창이 마우스 쪽으로 튀지 않는다
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                _pointerOffset = (Vector2)_window.localPosition - local;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if ((_window == null) || (_area == null))
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                return;
            }

            Vector2 position = ClampInsideArea(local + _pointerOffset);
            _window.localPosition = new Vector3(position.x, position.y, _window.localPosition.z);
        }

        /****************************************
        *                Methods
        ****************************************/

        private Vector2 ClampInsideArea(Vector2 position)
        {
            Rect    area  = _area.rect;
            Vector2 size  = _window.rect.size;
            Vector2 pivot = _window.pivot;

            float minX = area.xMin + (size.x * pivot.x);
            float maxX = area.xMax - (size.x * (1f - pivot.x));
            float minY = area.yMin + (size.y * pivot.y);
            float maxY = area.yMax - (size.y * (1f - pivot.y));

            // 창이 영역보다 크면 왼쪽 아래를 맞춘다
            position.x = (maxX < minX) ? minX : Mathf.Clamp(position.x, minX, maxX);
            position.y = (maxY < minY) ? minY : Mathf.Clamp(position.y, minY, maxY);
            return position;
        }
    }
}
