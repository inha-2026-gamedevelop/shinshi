// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>소형(키 1), 대형(키 2) 물약의 남은 지참량과 공용 재사용 시간을 표시한다</summary>
    public sealed class HudPotionView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   SIZE_COUNT     = 2;
        private const float EMPTY_ALPHA    = 0.45f;

        [Header("소형, 대형 순서")]
        [SerializeField] private TMP_Text[]    _countLabels = null;
        [SerializeField] private CanvasGroup[] _groups      = null;
        [SerializeField] private HudSkillSlotView[] _slots  = null;

        private readonly int[] _counts = { -1, -1 };

        /****************************************
        *                Methods
        ****************************************/

        public void SetCount(EPotionSize size, int count)
        {
            int index = (int)size;

            if ((index < 0) || (index >= SIZE_COUNT) || (_counts[index] == count))
            {
                return;
            }

            _counts[index] = count;

            if ((_countLabels != null) && (index < _countLabels.Length) && (_countLabels[index] != null))
            {
                _countLabels[index].SetText("{0}", count);
            }

            if ((_groups != null) && (index < _groups.Length) && (_groups[index] != null))
            {
                _groups[index].alpha = (count > 0) ? 1f : EMPTY_ALPHA;
            }
        }

        /// <summary>두 종류가 공용 재사용 시간을 쓰므로 같은 값을 두 칸에 넣는다</summary>
        public void SetSharedCooldown(float remaining, float duration)
        {
            if (_slots == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i] != null)
                {
                    _slots[i].SetCooldown(remaining, duration);
                }
            }
        }
    }
}
