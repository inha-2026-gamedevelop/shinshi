// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Systems
{
    /// <summary> 단축키로 창 하나를 열고 닫는다. 창 닫기 키를 누르면 가장 최근에 연 창부터 닫힌다 </summary>
    // 창이 꺼지면 이 컴포넌트의 등록도 끊기므로 창 자신이 아니라 항상 켜져 있는 오브젝트에 붙인다
    public sealed class HotkeyWindowToggle : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private HotkeyRouter  _router = null;
        [SerializeField] private EHotkeyAction _action = EHotkeyAction.None;
        [SerializeField] private GameObject    _window = null;

        private Func<bool> _onToggle = null;
        private Func<bool> _onClose  = null;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => (_window != null) && (_window.activeSelf);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _onToggle = OnToggle;
            _onClose  = OnClose;
        }

        private void OnEnable()
        {
            if ((_router == null) || (_window == null) || (_action == EHotkeyAction.None))
            {
                Debug.LogError($"[Hotkey] {name}: 라우터, 창, 동작 중 빠진 것이 있어 단축키를 등록하지 않는다", this);
                return;
            }

            _router.Register(_action, _onToggle);
            if (IsOpen)
            {
                _router.Register(EHotkeyAction.CloseWindow, _onClose);
            }
        }

        private void OnDisable()
        {
            if (_router == null)
            {
                return;
            }

            _router.Unregister(_action, _onToggle);
            _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> HUD 메뉴 버튼처럼 키 없이 여닫을 때 쓴다 </summary>
        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        public void SetOpen(bool open)
        {
            if (_window == null)
            {
                return;
            }

            _window.SetActive(open);
            if ((open) && (_router != null))
            {
                // 다시 등록하면 맨 뒤로 가서 창 닫기 키를 가장 먼저 받는다
                _router.Register(EHotkeyAction.CloseWindow, _onClose);
            }
        }

        private bool OnToggle()
        {
            SetOpen(!IsOpen);
            return true;
        }

        // 창의 닫기 버튼으로 이미 닫혔으면 다음 창에 넘긴다
        private bool OnClose()
        {
            bool wasOpen = IsOpen;
            if (wasOpen)
            {
                SetOpen(false);
            }

            _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            return wasOpen;
        }
    }
}
