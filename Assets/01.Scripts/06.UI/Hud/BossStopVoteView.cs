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
    /// <summary> 보스 맵의 토벌 중단 버튼과 투표 창을 그린다. 요청과 판정은 BossStopVoteBinder와 서버가 한다 </summary>
    public sealed class BossStopVoteView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string COUNT_FORMAT = "찬성 {0} / {1}";
        private const string TIME_FORMAT  = "{0}초";
        private const string WAITING_TEXT = "투표 완료 - 다른 파티원을 기다리는 중";
        private const string BALLOT_TEXT  = "파티원 전원이 찬성하면 보상 없이 토벌을 마칩니다";

        [SerializeField] private Button     _openButton     = null;
        [SerializeField] private GameObject _panel          = null;
        [SerializeField] private TMP_Text   _countText      = null;
        [SerializeField] private TMP_Text   _timeText       = null;
        [SerializeField] private TMP_Text   _statusText     = null;
        [SerializeField] private Button     _agreeButton    = null;
        [SerializeField] private Button     _disagreeButton = null;

        // 파티원 수만큼 켜고 찬성한 수만큼 칠한다. 누가 찬성했는지는 서버가 보내지 않는다
        [SerializeField] private Image[]    _marks          = null;
        [SerializeField] private Color      _agreeColor     = new Color(0.42f, 0.78f, 0.46f, 1f);
        [SerializeField] private Color      _pendingColor   = new Color(0.32f, 0.34f, 0.38f, 1f);

        private int _shownSeconds = -1;

        public event Action OpenClicked;
        public event Action AgreeClicked;
        public event Action DisagreeClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            AddListener(_openButton, OnOpen);
            AddListener(_agreeButton, OnAgree);
            AddListener(_disagreeButton, OnDisagree);
        }

        private void OnDisable()
        {
            RemoveListener(_openButton, OnOpen);
            RemoveListener(_agreeButton, OnAgree);
            RemoveListener(_disagreeButton, OnDisagree);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 열린 투표를 보인다. 아직 표를 내지 않았으면 찬반 버튼을, 냈으면 대기 안내를 보인다 </summary>
        public void ShowVote(int agreeCount, int requiredCount, bool hasVoted)
        {
            SetActive(_panel, true);
            if (_countText != null)
            {
                _countText.text = string.Format(COUNT_FORMAT, agreeCount, requiredCount);
            }

            if (_statusText != null)
            {
                _statusText.text = hasVoted ? WAITING_TEXT : BALLOT_TEXT;
            }

            SetActive(_agreeButton, !hasVoted);
            SetActive(_disagreeButton, !hasVoted);
            if (_marks == null)
            {
                return;
            }

            for (int i = 0; i < _marks.Length; ++i)
            {
                if (_marks[i] == null)
                {
                    continue;
                }

                _marks[i].gameObject.SetActive(i < requiredCount);
                _marks[i].color = (i < agreeCount) ? _agreeColor : _pendingColor;
            }
        }

        /// <summary> 남은 시간을 초 단위로 그린다. 같은 초면 글자를 다시 만들지 않는다 </summary>
        public void SetRemainingSeconds(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
            if ((_timeText == null) || (whole == _shownSeconds))
            {
                return;
            }

            _shownSeconds  = whole;
            _timeText.text = string.Format(TIME_FORMAT, whole);
        }

        public void HideVote()
        {
            SetActive(_panel, false);
            _shownSeconds = -1;
        }

        /// <summary> 투표가 열려 있거나 전투가 끝나면 새 제안 버튼을 막는다 </summary>
        public void SetOpenInteractable(bool interactable)
        {
            if ((_openButton != null) && (_openButton.interactable != interactable))
            {
                _openButton.interactable = interactable;
            }
        }

        private void OnOpen()     { OpenClicked?.Invoke(); }
        private void OnAgree()    { AgreeClicked?.Invoke(); }
        private void OnDisagree() { DisagreeClicked?.Invoke(); }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
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
