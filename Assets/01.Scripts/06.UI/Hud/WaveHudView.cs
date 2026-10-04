// Unity
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>일반전 상단 표시. 현재 웨이브/전체, 남은 제한 시간, 남은 적 수를 보여 준다</summary>
    public sealed class WaveHudView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string WAVE_FORMAT      = "웨이브 {0}/{1}";
        private const string ENEMY_FORMAT     = "남은 적 {0}";
        private const string TIME_FORMAT      = "{0:00}:{1:00}";
        private const string INTERMISSION_FMT = "다음 웨이브까지 {0}";
        private const int    SECONDS_PER_MIN  = 60;

        // 이 시간 이하면 경고 표시를 켠다. 색만으로 위험을 알리지 않기 위해 글자 표시도 함께 쓴다
        private const float WARNING_SECONDS = 60f;

        [Header("표시")]
        [SerializeField] private TMP_Text   _waveLabel         = null;
        [SerializeField] private TMP_Text   _timeLabel         = null;
        [SerializeField] private TMP_Text   _enemyLabel        = null;
        [SerializeField] private TMP_Text   _intermissionLabel = null;
        [SerializeField] private GameObject _timeWarningMark   = null;

        [Header("색")]
        [SerializeField] private Color _normalColor  = new Color(0.85f, 0.87f, 0.9f, 0.9f);
        [SerializeField] private Color _warningColor = new Color(0.95f, 0.45f, 0.4f, 1f);

        private int _wave           = -1;
        private int _waveTotal      = -1;
        private int _remainSeconds  = -1;
        private int _enemies        = -1;
        private int _intermission   = -1;

        /****************************************
        *                Methods
        ****************************************/

        public void SetWave(int current, int total)
        {
            if ((current == _wave) && (total == _waveTotal))
            {
                return;
            }

            _wave      = current;
            _waveTotal = total;
            SetText(_waveLabel, string.Format(WAVE_FORMAT, current, total));
        }

        /// <summary>남은 초를 올림해 표시하며 같은 초면 글자를 다시 만들지 않는다</summary>
        public void SetTimeRemaining(float seconds)
        {
            int whole = Mathf.CeilToInt(Mathf.Max(0f, seconds));

            if (whole == _remainSeconds)
            {
                return;
            }

            _remainSeconds = whole;
            SetText(_timeLabel, string.Format(TIME_FORMAT, whole / SECONDS_PER_MIN, whole % SECONDS_PER_MIN));

            bool warning = (seconds <= WARNING_SECONDS);

            if (_timeLabel != null)
            {
                _timeLabel.color = warning ? _warningColor : _normalColor;
            }

            if (_timeWarningMark != null)
            {
                _timeWarningMark.SetActive(warning);
            }
        }

        public void SetEnemiesRemaining(int count)
        {
            if (count == _enemies)
            {
                return;
            }

            _enemies = count;
            SetText(_enemyLabel, string.Format(ENEMY_FORMAT, count));
        }

        /// <summary>웨이브 사이 정비 시간을 보여 준다. 0 이하이면 숨긴다</summary>
        public void SetIntermission(float seconds)
        {
            int whole = Mathf.CeilToInt(Mathf.Max(0f, seconds));

            if (whole == _intermission)
            {
                return;
            }

            _intermission = whole;

            if (_intermissionLabel == null)
            {
                return;
            }

            _intermissionLabel.gameObject.SetActive(whole > 0);

            if (whole > 0)
            {
                _intermissionLabel.text = string.Format(INTERMISSION_FMT, whole);
            }
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
