// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 선택 리스트에서 고른 칸의 카드만 밝게 칠한다 </summary>
    public sealed class CharacterSelectListHighlight : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private CharacterSelectSelection _selection = null;
        [SerializeField] private Image[]                  _cards     = null;

        // 프로젝트가 Linear 색 공간이라 알파가 화면에서 생각보다 밝게 섞인다. 기본 카드는 흰색을 0.04로 낮게 두고,
        // 고른 카드는 밝기만 올리면 차이가 묻혀서 직업 글자와 같은 금색 계열로 구분한다
        [SerializeField] private Color _selectedColor = new Color(0.86f, 0.78f, 0.58f, 0.22f);
        [SerializeField] private Color _normalColor   = new Color(1f, 1f, 1f, 0.04f);

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
                _cards[i].color = (i == selectedSlot) ? _selectedColor : _normalColor;
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
