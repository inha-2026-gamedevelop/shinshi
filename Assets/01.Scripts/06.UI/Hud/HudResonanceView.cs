// Unity
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>
    /// 신령사 전용 공명 게이지와 강림 비용 표시. Z/X/C 칸의 아이콘과 쿨타임은 HudSkillSlotView 쪽이 맡고,
    /// 이 뷰는 공명이 모자랄 때 칸을 흐리게 하고 필요한 공명 수치를 보여 준다
    /// </summary>
    public sealed class HudResonanceView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   SLOT_COUNT      = 3;
        private const float DISABLED_ALPHA  = 0.45f;
        private const string VALUE_FORMAT   = "{0:N0} / {1:N0}";
        private const string COST_FORMAT    = "{0}";

        [Header("게이지")]
        [SerializeField] private RectTransform _fill       = null;
        [SerializeField] private TMP_Text      _valueLabel = null;

        [Header("강림 칸 (Z, X, C 순서)")]
        [SerializeField] private CanvasGroup[] _slotGroups = null;
        [SerializeField] private TMP_Text[]    _costLabels = null;

        private int _current = -1;
        private int _max     = -1;

        private readonly int[]  _costs      = new int[SLOT_COUNT];
        private readonly bool[] _coolingNow = new bool[SLOT_COUNT];

        /****************************************
        *                Methods
        ****************************************/

        public void SetResonance(int current, int max)
        {
            if ((current == _current) && (max == _max))
            {
                return;
            }

            _current = current;
            _max     = max;

            float ratio = (max > 0) ? Mathf.Clamp01((float)current / max) : 0f;

            if (_fill != null)
            {
                Vector2 anchor = _fill.anchorMax;
                anchor.x = ratio;
                _fill.anchorMax = anchor;
            }

            if (_valueLabel != null)
            {
                _valueLabel.text = string.Format(VALUE_FORMAT, current, max);
            }

            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                ApplyAvailability(i);
            }
        }

        public void SetAscensionCost(int index, int cost)
        {
            if ((index < 0) || (index >= SLOT_COUNT))
            {
                return;
            }

            _costs[index] = cost;

            if ((_costLabels != null) && (index < _costLabels.Length) && (_costLabels[index] != null))
            {
                _costLabels[index].text = string.Format(COST_FORMAT, cost);
            }

            ApplyAvailability(index);
        }

        /// <summary>강림은 공명이 충분하고 공용 쿨다운이 끝났을 때만 쓸 수 있다</summary>
        public void SetAscensionCooling(int index, bool cooling)
        {
            if ((index < 0) || (index >= SLOT_COUNT))
            {
                return;
            }

            _coolingNow[index] = cooling;
            ApplyAvailability(index);
        }

        private void ApplyAvailability(int index)
        {
            if ((_slotGroups == null) || (index >= _slotGroups.Length) || (_slotGroups[index] == null))
            {
                return;
            }

            bool available = (_current >= _costs[index]) && !_coolingNow[index];
            _slotGroups[index].alpha = available ? 1f : DISABLED_ALPHA;
        }
    }
}
