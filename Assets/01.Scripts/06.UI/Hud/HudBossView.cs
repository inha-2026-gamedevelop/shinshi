// Unity
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>보스전 상단 표시. 보스 체력과 단계, 파티 공용 부활 횟수, 보스 물약 사용 횟수를 보여 준다</summary>
    public sealed class HudBossView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string HEALTH_FORMAT = "{0}%";
        private const string PHASE_FORMAT  = "{0}단계 / {1}단계";
        private const string REVIVE_FORMAT = "공용 부활 {0}/{1}";
        private const string POTION_FORMAT = "물약 {0}/{1}";

        [Header("보스")]
        [SerializeField] private TMP_Text      _nameLabel   = null;
        [SerializeField] private RectTransform _healthFill  = null;
        [SerializeField] private TMP_Text      _healthLabel = null;
        [SerializeField] private TMP_Text      _phaseLabel  = null;

        [Header("파티")]
        [SerializeField] private TMP_Text _reviveLabel = null;
        [SerializeField] private TMP_Text _potionLabel = null;

        private int   _healthPercent = -1;
        private int   _phase         = -1;
        private int   _phaseTotal    = -1;
        private int   _revives       = -1;
        private int   _reviveMax     = -1;
        private int   _potionUsed    = -1;
        private int   _potionMax     = -1;

        /****************************************
        *                Methods
        ****************************************/

        public void SetBossName(string bossName)
        {
            if (_nameLabel != null)
            {
                _nameLabel.text = bossName;
            }
        }

        /// <summary>비율은 0에서 1까지로 제한한다. 정수 퍼센트가 같으면 다시 그리지 않는다</summary>
        public void SetHealth(float ratio)
        {
            float clamped = Mathf.Clamp01(ratio);
            int percent = Mathf.RoundToInt(clamped * 100f);

            if (percent == _healthPercent)
            {
                return;
            }

            _healthPercent = percent;

            if (_healthFill != null)
            {
                Vector2 max = _healthFill.anchorMax;
                max.x = clamped;
                _healthFill.anchorMax = max;
            }

            if (_healthLabel != null)
            {
                _healthLabel.text = string.Format(HEALTH_FORMAT, percent);
            }
        }

        public void SetPhase(int current, int total)
        {
            if ((current == _phase) && (total == _phaseTotal))
            {
                return;
            }

            _phase      = current;
            _phaseTotal = total;

            if (_phaseLabel != null)
            {
                _phaseLabel.text = string.Format(PHASE_FORMAT, current, total);
            }
        }

        public void SetSharedRevives(int remaining, int max)
        {
            if ((remaining == _revives) && (max == _reviveMax))
            {
                return;
            }

            _revives   = remaining;
            _reviveMax = max;

            if (_reviveLabel != null)
            {
                _reviveLabel.text = string.Format(REVIVE_FORMAT, remaining, max);
            }
        }

        public void SetPotionUse(int used, int max)
        {
            if ((used == _potionUsed) && (max == _potionMax))
            {
                return;
            }

            _potionUsed = used;
            _potionMax  = max;

            if (_potionLabel != null)
            {
                _potionLabel.text = string.Format(POTION_FORMAT, used, max);
            }
        }
    }
}
