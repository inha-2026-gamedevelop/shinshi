// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>쓰러진 동안 부활 대기 시간, 부활 가능 안내, 관전 상태를 화면 가운데에 보여 주고 살아 있으면 숨긴다</summary>
    public sealed class ReviveStatusView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string WAIT_FORMAT   = "{0}초 뒤 부활할 수 있습니다";
        private const string READY_TEXT    = "F 키로 부활";
        private const string SPECTATE_TEXT = "관전 중";
        private const string REVIVES_FMT   = "남은 공용 부활 {0}";

        [Header("표시")]
        [SerializeField] private GameObject _root         = null;
        [SerializeField] private TMP_Text   _statusLabel  = null;
        [SerializeField] private TMP_Text   _revivesLabel = null;

        private EParticipantLifeState _state = EParticipantLifeState.Alive;
        private int                   _seconds = -1;
        private int                   _revives = -1;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable() { Apply(); }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>대기 시간이 0이면 부활 가능 안내를 보여 준다. 매 프레임 불러도 같은 값이면 글자를 만들지 않는다</summary>
        public void SetState(EParticipantLifeState state, float waitSeconds, int sharedRevives)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, waitSeconds));

            if ((state == _state) && (seconds == _seconds) && (sharedRevives == _revives))
            {
                return;
            }

            _state   = state;
            _seconds = seconds;
            _revives = sharedRevives;
            Apply();
        }

        private void Apply()
        {
            bool visible = (_state != EParticipantLifeState.Alive);

            if (_root != null)
            {
                _root.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = BuildStatusText();
            }

            if (_revivesLabel != null)
            {
                bool showRevives = (_state != EParticipantLifeState.Spectating);
                _revivesLabel.gameObject.SetActive(showRevives);

                if (showRevives)
                {
                    _revivesLabel.text = string.Format(REVIVES_FMT, _revives);
                }
            }
        }

        private string BuildStatusText()
        {
            if (_state == EParticipantLifeState.Spectating)
            {
                return SPECTATE_TEXT;
            }

            return (_seconds > 0) ? string.Format(WAIT_FORMAT, _seconds) : READY_TEXT;
        }
    }
}
