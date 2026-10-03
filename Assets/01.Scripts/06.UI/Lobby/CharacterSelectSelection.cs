// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 선택 화면에서 지금 고른 칸. 카메라 시점과 리스트 하이라이트는 SelectionChanged를 듣고 따라간다 </summary>
    public sealed class CharacterSelectSelection : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int NO_SELECTION = -1;

        private int _selectedSlot = NO_SELECTION;

        /// <summary> 고른 칸이 바뀌면 새 칸 번호로 한 번 불린다 </summary>
        public event Action<int> SelectionChanged;

        public int SelectedSlot => _selectedSlot;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 칸을 고른다, 음수와 이미 고른 칸은 무시한다 </summary>
        public void Select(int slotIndex)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            // 칸 수는 여기서 모른다. 칸별 배열을 가진 쪽이 범위 밖 번호를 각자 무시한다
            if ((slotIndex < 0) || (slotIndex == _selectedSlot))
            {
                return;
            }

            _selectedSlot = slotIndex;
            SelectionChanged?.Invoke(_selectedSlot);
        }
    }
}
