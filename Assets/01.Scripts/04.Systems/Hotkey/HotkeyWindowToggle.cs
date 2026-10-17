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

        // 비워 두면 지금처럼 바로 닫는다. ICloseGuard를 구현한 컴포넌트만 받는다
        [SerializeField] private MonoBehaviour _closeGuard = null;

        private Func<bool>  _onToggle   = null;
        private Func<bool>  _onClose    = null;
        private Action      _closeNow   = null;
        private ICloseGuard _guard      = null;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => (_window != null) && (_window.activeSelf);

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
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

        /// <summary> HUD 메뉴 버튼처럼 키 없이 여닫을 때 쓴다. 닫을 때는 닫기 가드를 거친다 </summary>
        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        /// <summary> 열거나 닫는다. 닫기는 가드가 있으면 가드가 정한다 </summary>
        public void SetOpen(bool open)
        {
            EnsureInitialized();
            if (_window == null)
            {
                return;
            }

            if (!open)
            {
                RequestClose();
                return;
            }

            _window.SetActive(true);
            if (_router != null)
            {
                // 다시 등록하면 맨 뒤로 가서 창 닫기 키를 가장 먼저 받는다
                _router.Register(EHotkeyAction.CloseWindow, _onClose);
            }
        }

        /// <summary> 닫기를 요청한다, 바로 닫혔으면 참. 가드가 확인을 띄우면 창은 열린 채 거짓 </summary>
        public bool RequestClose()
        {
            EnsureInitialized();
            if (!IsOpen)
            {
                return true;
            }

            if ((_guard != null) && (!_guard.RequestClose(_closeNow)))
            {
                return false;
            }

            CloseNow();
            return true;
        }

        // 꺼진 오브젝트나 에디터에서 Awake보다 먼저 메뉴 버튼이 부를 수 있어 처음 쓰일 때도 만든다
        private void EnsureInitialized()
        {
            if (_onClose != null)
            {
                return;
            }

            _onToggle = OnToggle;
            _onClose  = OnClose;
            _closeNow = CloseNow;
            _guard    = _closeGuard as ICloseGuard;
            if ((_closeGuard != null) && (_guard == null))
            {
                Debug.LogError($"[Hotkey] {name}: 닫기 가드가 ICloseGuard를 구현하지 않아 바로 닫는다", this);
            }
        }

        private void CloseNow()
        {
            if (_window != null)
            {
                _window.SetActive(false);
            }

            if (_router != null)
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
            }
        }

        private bool OnToggle()
        {
            SetOpen(!IsOpen);
            return true;
        }

        // 창의 닫기 버튼으로 이미 닫혔으면 다음 창에 넘긴다. 가드가 확인을 띄웠으면 Esc를 여기서 쓰고 등록은 남긴다
        private bool OnClose()
        {
            if (!IsOpen)
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onClose);
                return false;
            }

            RequestClose();
            return true;
        }
    }
}
