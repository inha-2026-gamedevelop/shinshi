// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 던전 진행 패널의 포기 버튼을 출정 포기 요청에 잇는다. 확인을 받은 뒤 보내고, 거절되면 이유를 알린다 </summary>
    // 포기 확정과 귀환 가능 여부는 서버가 출정 상태로 내려 준다. 그 뒤의 결과 창과 마을 귀환은 SortieResultBinder가 맡는다
    public sealed class DungeonAbandonBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CONFIRM_TEXT       = "출정을 포기하고 마을로 돌아갑니다. 보상은 받을 수 없습니다.";
        private const string NOT_CONNECTED_TEXT = "서버에 연결되어 있지 않아 포기할 수 없다.";

        [SerializeField] private DungeonProgressView _progressView = null;
        [SerializeField] private UiPopupHub          _popups       = null;

        private bool _isConfirming = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _isConfirming = false;
            SortieClient.AbandonResult += OnAbandonResult;
            if (_progressView != null)
            {
                _progressView.AbandonClicked += OnAbandonClicked;
            }
        }

        private void OnDisable()
        {
            SortieClient.AbandonResult -= OnAbandonResult;
            if (_progressView != null)
            {
                _progressView.AbandonClicked -= OnAbandonClicked;
            }
        }

        private void Update()
        {
            if (_progressView != null)
            {
                _progressView.SetAbandonInteractable(!SortieClient.IsAbandonPending);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnAbandonClicked()
        {
            if ((_isConfirming) || (SortieClient.IsAbandonPending))
            {
                return;
            }

            if (_popups == null)
            {
                SendAbandon();
                return;
            }

            _isConfirming = true;
            _popups.ShowConfirm(EPopupSize.Small, CONFIRM_TEXT, OnConfirmed);
        }

        private void OnConfirmed(bool confirmed)
        {
            _isConfirming = false;
            if (confirmed)
            {
                SendAbandon();
            }
        }

        private void SendAbandon()
        {
            if (!SortieClient.RequestAbandon())
            {
                ShowToast(NOT_CONNECTED_TEXT);
            }
        }

        private void OnAbandonResult(SortieAbandonResultMessage message)
        {
            if (message.Reason != ERejectReason.None)
            {
                ShowToast(RejectText.Describe(message.Reason));
            }
        }

        private void ShowToast(string message)
        {
            if (_popups != null)
            {
                _popups.ShowToast(message);
            }
        }
    }
}
