// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    // 가결 판정과 출정 종료는 서버가 하며, 그 뒤 결과 창과 마을 귀환은 출정 상태를 보는 기존 흐름이 맡는다
    /// <summary> 보스 맵의 토벌 중단 버튼과 투표 창을 토벌 중단 투표 요청과 서버 상태에 잇는다 </summary>
    public sealed class BossStopVoteBinder : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PROPOSE_TEXT = "토벌 중단 투표를 시작할까요?\n파티원 전원이 찬성하면 보상 없이 토벌을 마칩니다.";
        private const string PASSED_TEXT  = "파티원 전원이 찬성해 토벌을 중단합니다.";
        private const string FAILED_TEXT  = "토벌 중단 투표가 부결되었습니다.";
        private const string OFFLINE_TEXT = "서버에 연결되어 있지 않아 투표할 수 없습니다.";

        [SerializeField] private BossStopVoteView _view   = null;
        [SerializeField] private UiPopupHub       _popups = null;

        private bool                 _isConfirming = false;
        private ESortieStopVoteState _lastState    = ESortieStopVoteState.None;
        private float                _endsAt       = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _isConfirming = false;
            SortieClient.StopVoteChanged += OnStopVoteChanged;
            if (_view != null)
            {
                _view.OpenClicked     += OnOpenClicked;
                _view.AgreeClicked    += OnAgreeClicked;
                _view.DisagreeClicked += OnDisagreeClicked;
                Apply(SortieClient.StopVote, false);
            }
        }

        private void OnDisable()
        {
            SortieClient.StopVoteChanged -= OnStopVoteChanged;
            if (_view != null)
            {
                _view.OpenClicked     -= OnOpenClicked;
                _view.AgreeClicked    -= OnAgreeClicked;
                _view.DisagreeClicked -= OnDisagreeClicked;
            }
        }

        private void Update()
        {
            if (_view == null)
            {
                return;
            }

            // 보스가 쓰러지거나 전멸해 출정이 끝나면 열린 투표 창도 닫는다
            bool running = (SortieClient.HasStatus) && (SortieClient.Current.Phase == ESortiePhase.Running);
            bool isOpen  = SortieClient.StopVote.State == ESortieStopVoteState.Open;
            _view.SetOpenInteractable((running) && (!isOpen) && (!_isConfirming));
            if ((isOpen) && (!running))
            {
                _view.HideVote();
                return;
            }

            if (isOpen)
            {
                _view.SetRemainingSeconds(_endsAt - Time.unscaledTime);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnOpenClicked()
        {
            if (_isConfirming)
            {
                return;
            }

            if (_popups == null)
            {
                Send(true);
                return;
            }

            _isConfirming = true;
            _popups.ShowConfirm(EPopupSize.Small, PROPOSE_TEXT, accepted =>
            {
                _isConfirming = false;
                if (accepted)
                {
                    Send(true);
                }
            });
        }

        private void OnAgreeClicked()    { Send(true); }
        private void OnDisagreeClicked() { Send(false); }

        private void OnStopVoteChanged(SortieStopVoteStateMessage vote)
        {
            if (vote.Reject != ERejectReason.None)
            {
                ShowToast(RejectText.Describe(vote.Reject));
            }

            Apply(vote, true);
        }

        private void Apply(SortieStopVoteStateMessage vote, bool announce)
        {
            ESortieStopVoteState previous = _lastState;
            _lastState = vote.State;
            if (vote.State == ESortieStopVoteState.Open)
            {
                _endsAt = Time.unscaledTime + vote.RemainingSeconds;
                _view.ShowVote(vote.AgreeCount, vote.RequiredCount, vote.MyChoice != ESortieStopVoteChoice.None);
                _view.SetRemainingSeconds(vote.RemainingSeconds);
                return;
            }

            _view.HideVote();
            if ((!announce) || (previous != ESortieStopVoteState.Open))
            {
                return;
            }

            if (vote.State == ESortieStopVoteState.Passed)
            {
                ShowToast(PASSED_TEXT);
            }
            else if (vote.State == ESortieStopVoteState.Failed)
            {
                ShowToast(FAILED_TEXT);
            }
        }

        private void Send(bool agree)
        {
            if (!SortieClient.RequestStopVote(agree))
            {
                ShowToast(OFFLINE_TEXT);
            }
        }

        private void ShowToast(string message)
        {
            if ((_popups != null) && (!string.IsNullOrEmpty(message)))
            {
                _popups.ShowToast(message);
            }
        }
    }
}
