// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shinshi.UI
{
    /// <summary>마우스와 키보드 선택에 같은 항목 설명과 강조를 표시한다</summary>
    public sealed class SettingsHintTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private SettingsView _view       = null;
        [SerializeField] private Image        _background = null;
        [SerializeField] private string       _title      = string.Empty;
        [SerializeField, TextArea] private string _description = string.Empty;
        [SerializeField] private Color _normalColor = new Color(0.095f, 0.118f, 0.14f, 1f);
        [SerializeField] private Color _hoverColor  = new Color(0.145f, 0.15f, 0.16f, 1f);

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDisable() { RestoreColor(); }

        public void OnPointerEnter(PointerEventData eventData) { Highlight(); }
        public void OnSelect(BaseEventData eventData) { Highlight(); }
        public void OnPointerExit(PointerEventData eventData) { RestoreColor(); }

        /****************************************
        *                Methods
        ****************************************/

        private void Highlight()
        {
            if (_view != null)
            {
                _view.ShowHint(_title, _description);
            }
            if (_background != null)
            {
                _background.color = _hoverColor;
            }
        }

        private void RestoreColor()
        {
            if (_background != null)
            {
                _background.color = _normalColor;
            }
        }
    }
}
