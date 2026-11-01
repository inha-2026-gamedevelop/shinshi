// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 출시 메뉴의 안내, 환경설정, 캐릭터 선택과 종료를 연결한다 </summary>
    public sealed class ReleaseMenuActions : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LEAVE_CONFIRM_TEXT   = "현재 접속을 종료하고 캐릭터 선택으로 돌아갈까요?";
        private const string ABANDON_CONFIRM_TEXT = "진행 중인 출정을 포기하고 캐릭터 선택으로 돌아갈까요? 보상은 받을 수 없습니다.";

        // 팝업의 Esc 닫기는 UiPopupHub가 맡는다
        [SerializeField] private SettingsView _settings = null;
        [SerializeField] private UiPopupHub   _popups   = null;

        /****************************************
        *                Methods
        ****************************************/

        public void OpenSettings() { _settings.Show(); }

        /// <summary> 거래 상대는 월드 우클릭으로 선택하며 메뉴에서는 절차를 안내한다 </summary>
        public void OpenTradeHelp()
        {
            _popups.ShowAlert(EPopupSize.Small, "거래할 플레이어를 마우스 오른쪽 버튼으로 누른 뒤 거래하기를 선택하세요.");
        }

        public void ConfirmCharacterSelect()
        {
            // 던전에서 진행 중인 출정은 포기를 먼저 보내고 나간다(그대로 끊으면 재접속 유예 동안 다시 입장할 수 없다)
            bool   abandons = (NetworkManager.singleton is ShinshiNetworkManager manager) && (manager.WillAbandonSortieOnLeave);
            string message  = abandons ? ABANDON_CONFIRM_TEXT : LEAVE_CONFIRM_TEXT;
            _popups.ShowConfirm(EPopupSize.Small, message, accepted =>
            {
                if (accepted)
                {
                    ReturnToCharacterSelect();
                }
            });
        }

        public void ConfirmQuit()
        {
            _popups.ShowConfirm(EPopupSize.Small, "게임을 종료할까요?", accepted =>
            {
                if (accepted)
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            });
        }

        // 끊기, 출정 포기와 이탈 저장 대기, 씬 이동은 매니저가 맡는다. 이미 서버를 옮기는 중이면 매니저가 무시한다
        private static void ReturnToCharacterSelect()
        {
            if (NetworkManager.singleton is ShinshiNetworkManager manager)
            {
                manager.LeaveToCharacterSelect();
                return;
            }

            LobbyFlow.ReturnToCharacterSelect(string.Empty);
        }
    }
}
