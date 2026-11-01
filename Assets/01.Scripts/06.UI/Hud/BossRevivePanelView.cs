// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 보스전에서 쓰러졌을 때 부활 대기 시간과 부활 버튼을 그린다. 요청과 판정은 BossReviveBinder와 서버가 한다 </summary>
    public sealed class BossRevivePanelView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string DEAD_TITLE     = "사망하였습니다.";
        private const string SPECTATE_TITLE = "관전 중";
        private const string TIME_FORMAT    = "{0}초";
        private const string READY_TIME     = "부활 가능";
        private const string WAIT_TEXT      = "대기 시간이 끝나면\n부활할 수 있습니다.";
        private const string READY_TEXT     = "부활 버튼을 누르면\n안전한 지점에서 부활합니다.";
        private const string REQUEST_TEXT   = "부활할 안전한 지점을\n찾고 있습니다.";
        private const string SPECTATE_TEXT  = "파티 공용 부활 횟수를 모두 사용했습니다.\n전투가 끝날 때까지 관전합니다.";
        private const string COUNT_FORMAT   = "공용 x {0}";
        private const string FOOTER_TEXT    = "부활 시 파티 공용 부활 횟수가 1회 소모됩니다.";
        private const int    UNLIMITED      = -1;

        [SerializeField] private GameObject _panel        = null;
        [SerializeField] private TMP_Text   _titleText    = null;
        [SerializeField] private TMP_Text   _timeText     = null;
        [SerializeField] private TMP_Text   _bodyText     = null;
        [SerializeField] private Button     _reviveButton = null;
        [SerializeField] private TMP_Text   _countText    = null;
        [SerializeField] private TMP_Text   _footerText   = null;

        private EReviveViewState _state   = EReviveViewState.Hidden;
        private int              _seconds = -1;
        private int              _revives = int.MinValue;

        public event Action ReviveClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (_reviveButton != null)
            {
                _reviveButton.onClick.AddListener(OnRevive);
            }

            Apply();
        }

        private void OnDisable()
        {
            if (_reviveButton != null)
            {
                _reviveButton.onClick.RemoveListener(OnRevive);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 매 프레임 불러도 단계, 남은 초, 횟수가 같으면 글자를 다시 만들지 않는다. 일반전처럼 횟수 제한이 없으면 sharedRevives는 -1이다
        /// <summary> 단계와 남은 대기 시간(초), 남은 공용 부활 횟수를 그린다 </summary>
        public void Show(EReviveViewState state, float waitSeconds, int sharedRevives)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(waitSeconds));
            if ((state == _state) && (seconds == _seconds) && (sharedRevives == _revives))
            {
                return;
            }

            _state   = state;
            _seconds = seconds;
            _revives = sharedRevives;
            Apply();
        }

        public void Hide()
        {
            Show(EReviveViewState.Hidden, 0f, _revives);
        }

        private void Apply()
        {
            bool visible = _state != EReviveViewState.Hidden;
            SetActive(_panel, visible);
            if (!visible)
            {
                return;
            }

            bool spectating = _state == EReviveViewState.Spectating;
            SetText(_titleText, spectating ? SPECTATE_TITLE : DEAD_TITLE);
            SetActive(_timeText, !spectating);
            SetText(_timeText, (_state == EReviveViewState.Waiting) ? string.Format(TIME_FORMAT, _seconds) : READY_TIME);
            SetText(_bodyText, BodyText(_state));

            SetActive(_reviveButton, !spectating);
            if (_reviveButton != null)
            {
                _reviveButton.interactable = _state == EReviveViewState.Ready;
            }

            bool limited = (!spectating) && (_revives != UNLIMITED);
            SetActive(_countText, limited);
            SetText(_countText, string.Format(COUNT_FORMAT, Mathf.Max(0, _revives)));
            SetActive(_footerText, limited);
            SetText(_footerText, FOOTER_TEXT);
        }

        private static string BodyText(EReviveViewState state)
        {
            switch (state)
            {
                case EReviveViewState.Ready:
                    return READY_TEXT;
                case EReviveViewState.Requesting:
                    return REQUEST_TEXT;
                case EReviveViewState.Spectating:
                    return SPECTATE_TEXT;
                default:
                    return WAIT_TEXT;
            }
        }

        private void OnRevive()
        {
            if (_state == EReviveViewState.Ready)
            {
                ReviveClicked?.Invoke();
            }
        }

        private static void SetText(TMP_Text target, string value)
        {
            if ((target != null) && (target.text != value))
            {
                target.text = value;
            }
        }

        private static void SetActive(Component target, bool active)
        {
            if ((target != null) && (target.gameObject.activeSelf != active))
            {
                target.gameObject.SetActive(active);
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if ((target != null) && (target.activeSelf != active))
            {
                target.SetActive(active);
            }
        }
    }
}
