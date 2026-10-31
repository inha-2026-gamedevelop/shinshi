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
        private RectTransform _rect;
        private Vector2 _homePosition;
        private Vector2 _rollFrom;
        private Vector2 _rollTo;
        private Quaternion _baseRotation;
        private float _rollElapsed;
        private bool _rolling;
        private const float ROLL_SECONDS = 0.24f;
        private const float ROLL_TILT = 28f;

        private void Awake()
        {
            _rect = transform as RectTransform;
            if (_rect != null)
            {
                _homePosition = _rect.anchoredPosition;
                _baseRotation = _rect.localRotation;
            }
        }

        private void Start()
        {
            if (_defaultItem)
            {
                Activate();
            }
        }

        private void Update()
        {
            if ((!_rolling) || (_rect == null))
            {
                return;
            }
            _rollElapsed = Mathf.Min(ROLL_SECONDS, _rollElapsed + Time.unscaledDeltaTime);
            float progress = _rollElapsed / ROLL_SECONDS;
            float eased = progress * progress * (3f - (2f * progress));
            _rect.anchoredPosition = Vector2.Lerp(_rollFrom, _rollTo, eased);
            float direction = Mathf.Sign(_rollTo.y - _rollFrom.y);
            _rect.localRotation = _baseRotation * Quaternion.Euler(direction * ROLL_TILT * Mathf.Sin(progress * Mathf.PI), 0f, 0f);
            if (progress >= 1f)
            {
                _rect.localRotation = _baseRotation;
                _rolling = false;
            }
        }

        private void OnDisable()
        {
            if ((_rolling) && (_rect != null))
            {
                _rect.anchoredPosition = _rollTo;
                _rect.localRotation = _baseRotation;
            }
            _rolling = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if ((!_rolling) && ((_other == null) || (!_other._rolling)))
            {
                Activate();
            }
        }
        public void OnSelect(BaseEventData eventData) => Activate();
        public void OnPointerExit(PointerEventData eventData) { }
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
            if ((_button == null) || (_label == null) || (_other == null) || (_rect == null) || (_other._rect == null) || (_active))
            {
                return;
            }

            _active = true;
            _other._active = false;
            int topIndex = Mathf.Max(transform.GetSiblingIndex(), _other.transform.GetSiblingIndex());
            transform.SetSiblingIndex(topIndex);
            Vector2 top = _defaultItem ? _homePosition : _other._homePosition;
            Vector2 bottom = _defaultItem ? _other._homePosition : _homePosition;
            BeginRoll(top);
            _other.BeginRoll(bottom);
            RefreshColor();
            _other.RefreshColor();
        }

        private void BeginRoll(Vector2 target)
        {
            _rollFrom = _rect.anchoredPosition;
            _rollTo = target;
            _rollElapsed = 0f;
            _rolling = (_rollFrom - _rollTo).sqrMagnitude > 0.001f;
            if (!_rolling)
            {
                _rect.localRotation = _baseRotation;
            }
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
