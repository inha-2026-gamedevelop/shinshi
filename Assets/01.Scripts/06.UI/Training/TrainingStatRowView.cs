// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>정기 수련 한 항목의 투자 단계, 진행률, 증감 버튼을 보여주는 행</summary>
    public sealed class TrainingStatRowView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("표시")]
        [SerializeField] private TMP_Text _nameLabel     = null;
        [SerializeField] private TMP_Text _stepsLabel    = null;
        [SerializeField] private TMP_Text _pendingLabel  = null;
        [SerializeField] private TMP_Text _effectLabel   = null;
        [SerializeField] private Image    _barFill       = null;
        [SerializeField] private Image    _rowBackground = null;

        [Header("상호작용")]
        [SerializeField] private Button _minusButton  = null;
        [SerializeField] private Button _plusButton   = null;
        [SerializeField] private Button _selectButton = null;

        [Header("색")]
        [SerializeField] private Color _selectedColor = new Color(0.17f, 0.2f, 0.18f, 1f);
        [SerializeField] private Color _idleColor     = new Color(0.095f, 0.118f, 0.14f, 1f);

        private ETrainingStat _stat;

        /****************************************
        *              Properties
        ****************************************/

        public ETrainingStat Stat         => _stat;
        public Button        MinusButton  => _minusButton;
        public Button        PlusButton   => _plusButton;
        public Button        SelectButton => _selectButton;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>행이 표시할 수련 항목과 이름을 한 번 고정한다</summary>
        public void Bind(ETrainingStat stat, string label)
        {
            _stat = stat;

            if (_nameLabel != null)
            {
                _nameLabel.text = label;
            }
        }

        /// <summary>현재 표시 단계(확정 또는 대기 반영), 항목 상한, 대기 변화량, 효과 문구, 선택 여부를 반영한다</summary>
        public void SetDisplay(int steps, int statCap, int pendingDelta, string effectSummary, bool isSelected)
        {
            if (_stepsLabel != null)
            {
                _stepsLabel.text = $"{steps} / {statCap}";
            }

            if (_barFill != null)
            {
                _barFill.fillAmount = (statCap > 0) ? Mathf.Clamp01((float)steps / statCap) : 0f;
            }

            if (_pendingLabel != null)
            {
                bool hasPending = pendingDelta != 0;
                _pendingLabel.gameObject.SetActive(hasPending);
                _pendingLabel.text = hasPending ? $"+{pendingDelta}" : string.Empty;
            }

            if (_effectLabel != null)
            {
                _effectLabel.text = effectSummary;
            }

            if (_rowBackground != null)
            {
                _rowBackground.color = isSelected ? _selectedColor : _idleColor;
            }
        }

        public void SetInteractable(bool canDecrease, bool canIncrease)
        {
            if (_minusButton != null)
            {
                _minusButton.interactable = canDecrease;
            }

            if (_plusButton != null)
            {
                _plusButton.interactable = canIncrease;
            }
        }
    }
}
