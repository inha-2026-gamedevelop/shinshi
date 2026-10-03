// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 선택 리스트에서 고른 칸의 카드만 선택 스프라이트와 색으로 바꾼다 </summary>
    public sealed class CharacterSelectListHighlight : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private CharacterSelectSelection _selection = null;
        [SerializeField] private Image[]                  _cards     = null;

        // 스프라이트가 비어 있으면 카드의 현재 스프라이트를 그대로 두고 색만 바꾼다
        [SerializeField] private Sprite _selectedSprite = null;
        [SerializeField] private Sprite _normalSprite   = null;
        [SerializeField] private Color  _selectedColor  = Color.white;
        [SerializeField] private Color  _normalColor    = Color.white;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_selection == null) || (!HasCards()))
            {
                Debug.LogError("[CharacterSelect] 리스트 하이라이트에 선택 상태나 카드 이미지가 연결되지 않았다", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            _selection.SelectionChanged += ApplyHighlight;
            ApplyHighlight(_selection.SelectedSlot);
        }

        private void OnDisable() { _selection.SelectionChanged -= ApplyHighlight; }

        /****************************************
        *                Methods
        ****************************************/

        private void ApplyHighlight(int selectedSlot)
        {
            for (int i = 0; i < _cards.Length; ++i)
            {
                bool   isSelected = (i == selectedSlot);
                Sprite sprite     = isSelected ? _selectedSprite : _normalSprite;
                if (sprite != null)
                {
                    _cards[i].sprite = sprite;
                }
                _cards[i].color = isSelected ? _selectedColor : _normalColor;
            }
        }

        private bool HasCards()
        {
            if ((_cards == null) || (_cards.Length == 0))
            {
                return false;
            }

            for (int i = 0; i < _cards.Length; ++i)
            {
                if (_cards[i] == null)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
