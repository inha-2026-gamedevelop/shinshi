// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary> 출시 메뉴의 안내, 환경설정, 캐릭터 선택과 종료를 연결한다 </summary>
    public sealed class ReleaseMenuActions : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private HotkeyRouter _router   = null;
        [SerializeField] private SettingsView _settings = null;
        [SerializeField] private UiPopupHub   _popups   = null;
        [SerializeField] private AlertPopupView[]   _alerts   = null;
        [SerializeField] private ConfirmPopupView[] _confirms = null;

        private Func<bool> _onClose = null;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _onClose = ClosePopup;
        }

        private void OnDisable()
        {
            if (_router != null)
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void OpenSettings() { _settings.Show(); }

        /// <summary> 거래 상대는 월드 우클릭으로 선택하며 메뉴에서는 절차를 안내한다 </summary>
        public void OpenTradeHelp()
        {
            _popups.ShowAlert(EPopupSize.Small, "거래할 플레이어를 마우스 오른쪽 버튼으로 누른 뒤 거래하기를 선택하세요.");
            RegisterPopup();
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
            RegisterPopup();
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
            RegisterPopup();
        }

        private void RegisterPopup()
        {
            if (_onClose == null)
            {
                _onClose = ClosePopup;
            }
            _router.Register(EHotkeyAction.CloseWindow, _onClose);
        }

        private bool ClosePopup()
        {
            bool wasOpen = false;
            foreach (AlertPopupView view in _alerts)
            {
                wasOpen |= (view != null) && (view.gameObject.activeInHierarchy);
            }
            foreach (ConfirmPopupView view in _confirms)
            {
                wasOpen |= (view != null) && (view.gameObject.activeInHierarchy);
            }
            if (wasOpen)
            {
                _popups.CloseAll();
            }
            _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            return wasOpen;
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
