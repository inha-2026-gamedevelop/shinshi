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
            _popups.ShowConfirm(EPopupSize.Small, "현재 접속을 종료하고 캐릭터 선택으로 돌아갈까요?", accepted =>
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

        private static void ReturnToCharacterSelect()
        {
            // 의도한 종료를 접속 장애로 처리해 씬을 두 번 열지 않도록 기존 전환 플래그를 쓴다.
            if (NetworkManager.singleton is ShinshiNetworkManager manager)
            {
                if ((manager.IsTransferringServer) || (NetworkServer.active))
                {
                    return;
                }
                manager.IsTransferringServer = true;
                try
                {
                    manager.StopClient();
                }
                finally
                {
                    manager.IsTransferringServer = false;
                }
                ChatClient.ClearHistory();
            }
            LobbyFlow.ReturnToCharacterSelect(string.Empty);
        }
    }
}
