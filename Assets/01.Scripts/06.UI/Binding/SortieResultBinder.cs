// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 던전 출정이 끝나면 결과 창을 띄우고, 귀환 버튼으로 마을에 다시 접속한다 </summary>
    // 마을 재접속은 씬을 다시 불러 이 개체가 사라지므로 귀환 흐름 코루틴은 씬을 넘어 남는 네트워크 매니저에서 돌린다
    public sealed class SortieResultBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private static SortieReturnFlow _returnFlow = null;

        [SerializeField] private GameObject       _window = null;
        [SerializeField] private ResultScreenView _view   = null;
        [SerializeField] private UiPopupHub       _popups = null;

        private string _contentId = null;
        private bool   _isShown   = false;

        /****************************************
        *              Unity Event
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 흐름이 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _returnFlow = null;
        }

        private void OnEnable()
        {
            SortieClient.StatusChanged  += OnStatusChanged;
            TownPartyClient.RoomChanged += OnRoomChanged;
            if (_view != null)
            {
                _view.ReturnClicked += OnReturnClicked;
            }

            if ((SortieClient.HasStatus) && (ClientRoles.IsDungeon()))
            {
                OnStatusChanged(SortieClient.Current);
            }
        }

        private void OnDisable()
        {
            SortieClient.StatusChanged  -= OnStatusChanged;
            TownPartyClient.RoomChanged -= OnRoomChanged;
            if (_view != null)
            {
                _view.ReturnClicked -= OnReturnClicked;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 출정 전 마지막 방의 콘텐츠 ID를 결과 창 지역 이름으로 쓴다
        private void OnRoomChanged(PartyRoomStateMessage room)
        {
            if (room.InRoom)
            {
                _contentId = room.ContentId;
            }
        }

        private void OnStatusChanged(SortieStatusMessage status)
        {
            if ((_window == null) || (_view == null) || (!SortieResultRules.ShouldShow(in status)))
            {
                return;
            }

            if (!_isShown)
            {
                _isShown = true;
                _window.SetActive(true);
                _view.SetResult(SortieResultRules.Build(in status, _contentId));
            }

            _view.SetSaveStatus(SortieResultRules.ToSaveStatus(status.SettlementState));
        }

        private void OnReturnClicked()
        {
            if ((!SortieClient.HasStatus) || (!SortieClient.Current.CanReturn))
            {
                if (_popups != null)
                {
                    _popups.ShowToast("정산이 끝난 뒤에 돌아갈 수 있다.");
                }

                return;
            }

            if ((_returnFlow != null) && (_returnFlow.IsRunning))
            {
                return;
            }

            var    manager       = NetworkManager.singleton as ShinshiNetworkManager;
            var    authenticator = (manager != null) ? manager.authenticator as ShinshiClientAuthenticator : null;
            string characterId   = LobbyFlow.ActiveCharacterId;
            if ((manager == null) || (authenticator == null) || (string.IsNullOrEmpty(characterId)))
            {
                LobbyFlow.ReturnToCharacterSelect("마을로 돌아갈 정보가 없다. 캐릭터를 다시 고른다.");
                return;
            }

            _returnFlow = new SortieReturnFlow(new LobbyApiClient(), manager, authenticator);
            manager.StartCoroutine(_returnFlow.Run(characterId, OnReturnFinished));
        }

        // 이 개체는 마을 씬을 다시 불러 사라졌을 수 있다. 화면은 살아 있을 때만 만진다
        private void OnReturnFinished(ERejectReason reason)
        {
            if (reason == ERejectReason.None)
            {
                return;
            }

            if (!NetworkClient.isConnected)
            {
                LobbyFlow.ReturnToCharacterSelect("마을 서버에 접속하지 못했다. 다시 게임 시작을 누른다.");
                return;
            }

            if ((this != null) && (_popups != null))
            {
                _popups.ShowToast(RejectText.Describe(reason));
            }
        }
    }
}
