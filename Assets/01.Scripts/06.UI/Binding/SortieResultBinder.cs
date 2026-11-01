// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 던전 출정이 끝나면 결과 창을 띄우고, 귀환 버튼으로 마을에 다시 접속한다 </summary>
    // 마을 재접속은 씬을 다시 불러 이 개체가 사라지므로 귀환 흐름은 씬을 넘어 남는 네트워크 매니저가 돌린다.
    // 정산 뒤 던전이 연결을 닫을 때의 자동 귀환과 같은 흐름 하나를 쓰고, 마을 접속 실패의 캐릭터 선택 이동도 매니저가 맡는다
    public sealed class SortieResultBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private GameObject       _window = null;
        [SerializeField] private ResultScreenView _view   = null;
        [SerializeField] private UiPopupHub       _popups = null;

        private bool _isShown = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            SortieClient.StatusChanged  += OnStatusChanged;
            SortieClient.RewardReceived += OnRewardReceived;
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
            SortieClient.RewardReceived -= OnRewardReceived;
            if (_view != null)
            {
                _view.ReturnClicked -= OnReturnClicked;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 던전 클라이언트는 파티 방을 모른다. 마을에서 출정 직전에 기억해 둔 콘텐츠로 지역 이름을 만든다
        private static string RegionName()
        {
            if (!SortieContentMemory.TryRecall(out EPartyMode mode, out string contentId))
            {
                return string.Empty;
            }

            return DungeonEntryRules.ContentName(GameDB.IsInitialized ? GameDB.Progression : null, mode, contentId);
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
                _view.SetResult(BuildResult(in status));
                ESessionEndReason reason = SortieResultRules.ResolveReason(in status);
                if ((reason == ESessionEndReason.Timeout) || (reason == ESessionEndReason.AllDead))
                {
                    UiSound.Play(EUiSoundGroup.Result, EUiSoundAction.Fail);
                }
                else if (reason == ESessionEndReason.Success)
                {
                    UiSound.Play(EUiSoundGroup.Result, EUiSoundAction.Success);
                }
            }

            _view.SetSaveStatus(SortieResultRules.ToSaveStatus(status.SettlementState));
        }

        // 보상 내역은 출정 종료 정산안이 만들어진 뒤에 온다. 먼저 이탈해 창이 떠 있었으면 받은 값으로 다시 그린다
        private void OnRewardReceived(SortieRewardMessage reward)
        {
            if ((!_isShown) || (_view == null) || (!SortieClient.HasStatus))
            {
                return;
            }

            SortieStatusMessage status = SortieClient.Current;
            _view.SetResult(BuildResult(in status));
            _view.SetSaveStatus(SortieResultRules.ToSaveStatus(status.SettlementState));
        }

        private static ResultScreenData BuildResult(in SortieStatusMessage status)
        {
            if (!SortieClient.HasReward)
            {
                return SortieResultRules.Build(in status, RegionName());
            }

            SortieRewardMessage reward = SortieClient.Reward;
            return SortieResultRules.Build(in status, RegionName(), in reward, GameDB.IsInitialized ? GameDB.Rewards : null);
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

            // 이미 마을로 옮기는 중이거나 메뉴로 나가는 중이다
            var manager = NetworkManager.singleton as ShinshiNetworkManager;
            if ((manager != null) && (manager.IsTransferringServer))
            {
                return;
            }

            if ((manager == null) || (!manager.TryReturnToTown(OnReturnFinished)))
            {
                LobbyFlow.ReturnToCharacterSelect("마을로 돌아갈 정보가 없다. 캐릭터를 다시 고른다.");
            }
        }

        // 연결이 남은 실패(입장권 단계)만 안내한다. 연결이 없는 실패는 매니저가 캐릭터 선택으로 보낸다.
        // 이 개체는 마을 씬을 다시 불러 사라졌을 수 있다. 화면은 살아 있을 때만 만진다
        private void OnReturnFinished(ERejectReason reason)
        {
            if ((reason == ERejectReason.None) || (!NetworkClient.isConnected))
            {
                return;
            }

            if ((this != null) && (_popups != null))
            {
                _popups.ShowToast(RejectText.Describe(reason));
            }
        }
    }
}
