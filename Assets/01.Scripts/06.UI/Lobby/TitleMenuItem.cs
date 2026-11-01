using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shinshi.UI
{
    public sealed class TitleMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
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

        public void OnPointerEnter(PointerEventData eventData)
        {
            Activate();
        }
        public void OnSelect(BaseEventData eventData) => Activate();
        public void OnPointerExit(PointerEventData eventData) => RestoreDefault();
        public void OnDeselect(BaseEventData eventData) => RestoreDefault();

        private void Activate()
        {
            if ((_button == null) || (_label == null) || (_other == null) || (_active))
            {
                return;
            }

            _active = true;
            _other._active = false;
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
