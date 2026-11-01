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

        private const float  PERCENT_SCALE = 100f;
        private const string HEALTH_FORMAT = "{0}%";
        private const string PHASE_FORMAT  = "{0}단계 / {1}단계";
        private const string REVIVE_FORMAT = "공용 부활 {0}/{1}";
        private const string POTION_FORMAT = "물약 {0}/{1}";

        // 남은 시간이 이 이하이면 노란색, 더 줄어 아래 값 이하이면 빨간색으로 바꾼다
        private const float  CAUTION_SECONDS = 300f;
        private const float  DANGER_SECONDS  = 60f;

        [Header("보스")]
        [SerializeField] private TMP_Text      _nameLabel   = null;
        [SerializeField] private RectTransform _healthFill  = null;
        [SerializeField] private TMP_Text      _healthLabel = null;
        [SerializeField] private TMP_Text      _phaseLabel  = null;

        [Header("파티")]
        [SerializeField] private TMP_Text _reviveLabel = null;
        [SerializeField] private TMP_Text _potionLabel = null;

        [Header("제한 시간")]
        [SerializeField] private HudBrushTimer _timer     = null;
        [SerializeField] private HudBrushTimer _sealTimer = null;
        [SerializeField] private Color         _timeNormalColor = Color.white;
        [SerializeField] private Color         _timeCautionColor = Color.yellow;
        [SerializeField] private Color         _timeDangerColor  = Color.red;

        private int _healthPercent = -1;
        private int _phase         = -1;
        private int _phaseTotal    = -1;
        private int _revives       = -1;
        private int _reviveMax     = -1;
        private int _potionUsed    = -1;
        private int _potionMax     = -1;
        private int _remainSeconds = -1;

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
            int percent = Mathf.RoundToInt(clamped * PERCENT_SCALE);

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

        /// <summary>남은 제한 시간을 올림해 mm:ss로 표시하며 같은 초면 글자를 다시 만들지 않는다</summary>
        public void SetTimeRemaining(float seconds)
        {
            int whole = Mathf.CeilToInt(Mathf.Max(0f, seconds));

            if (whole == _remainSeconds)
            {
                return;
            }

            _remainSeconds = whole;

            if (_timer != null)
            {
                _timer.SetColor((seconds <= DANGER_SECONDS) ? _timeDangerColor
                    : ((seconds <= CAUTION_SECONDS) ? _timeCautionColor : _timeNormalColor));
                _timer.SetSeconds(whole);
            }
        }

        /// <summary>봉인 제한 시간 표시가 연결된 보스 HUD만 참이다. 무철처럼 연결이 없으면 봉인 뷰를 찾지 않는다</summary>
        public bool HasSealTimer => _sealTimer != null;

        /// <summary>월백 하드 25% 봉인의 제한 시간. 진행 중일 때만 보이며 항상 빨간색이다</summary>
        public void SetSealTimeRemaining(bool active, float seconds)
        {
            if (_sealTimer == null)
            {
                return;
            }

            if (_sealTimer.gameObject.activeSelf != active)
            {
                _sealTimer.gameObject.SetActive(active);
            }

            if (active)
            {
                _sealTimer.SetColor(_timeDangerColor);
                _sealTimer.SetSeconds(Mathf.CeilToInt(Mathf.Max(0f, seconds)));
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
