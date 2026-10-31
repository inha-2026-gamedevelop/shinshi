using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shinshi.UI
{
    public sealed class TitleMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler, ICanvasRaycastFilter
    {
        [SerializeField] private Button _button = null;
        [SerializeField] private Image _label = null;
        [SerializeField] private TitleMenuItem _other = null;
        [SerializeField] private bool _defaultItem = false;
        [SerializeField] private Color _activeColor = Color.white;
        [SerializeField] private Color _inactiveColor = new Color(0.43f, 0.43f, 0.43f, 1f);

        private bool _active;

        private void Start()
        {
            if (_defaultItem)
            {
                Activate();
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => Activate();
        public void OnSelect(BaseEventData eventData) => Activate();
        public void OnPointerExit(PointerEventData eventData) => RestoreDefault();
        public void OnDeselect(BaseEventData eventData) => RestoreDefault();

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if ((_other == null) || !(_other.transform is RectTransform otherRect)
                || !RectTransformUtility.RectangleContainsScreenPoint(otherRect, screenPoint, eventCamera))
            {
                return true;
            }

            Vector2 ownCenter = RectTransformUtility.WorldToScreenPoint(eventCamera, transform.position);
            Vector2 otherCenter = RectTransformUtility.WorldToScreenPoint(eventCamera, otherRect.position);
            float ownDistance = Mathf.Abs(screenPoint.y - ownCenter.y);
            float otherDistance = Mathf.Abs(screenPoint.y - otherCenter.y);
            return (ownDistance < otherDistance) || ((ownDistance == otherDistance) && _defaultItem);
        }

        private void Activate()
        {
            if ((_button == null) || (_label == null) || (_other == null))
            {
                return;
            }

            _active = true;
            _other._active = false;
            int topIndex = Mathf.Max(transform.GetSiblingIndex(), _other.transform.GetSiblingIndex());
            transform.SetSiblingIndex(topIndex);
            RefreshColor();
            _other.RefreshColor();
        }

        private void RestoreDefault()
        {
            TitleMenuItem item = _defaultItem ? this : _other;
            if (item != null)
            {
                item.Activate();
            }
        }

        private void RefreshColor()
        {
            if ((_button == null) || (_label == null))
            {
                return;
            }

            _label.color = _active ? _activeColor : _inactiveColor;
        }
    }
}
