// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary> 창이 켜져 있는 동안 Esc(창 닫기)로 그 창의 닫기 버튼을 누른다. 단축키 창 토글이 없는 창에 붙인다 </summary>
    public sealed class WindowEscapeButton : MonoBehaviour
    {
        // 버튼을 누른 것과 똑같이 처리해 닫기 전 확인, 거래 취소 요청, 정산 대기 안내 같은 창의 규칙을 그대로 따른다.
        // 켜질 때 등록해 나중에 연 창이 먼저 받는다. 버튼을 지금 못 누르면 Esc를 이 창에서 쓰고 아무것도 하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private HotkeyRouter _router      = null;
        [SerializeField] private Button       _closeButton = null;

        private Func<bool> _onEscape = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if ((_router == null) || (_closeButton == null))
            {
                Debug.LogError($"[UI] {name}: 라우터나 닫기 버튼이 없어 Esc로 닫지 않는다", this);
                return;
            }

            if (_onEscape == null)
            {
                _onEscape = OnEscape;
            }

            _router.Register(EHotkeyAction.CloseWindow, _onEscape);
        }

        private void OnDisable()
        {
            if ((_router != null) && (_onEscape != null))
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onEscape);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool OnEscape()
        {
            if ((_closeButton.isActiveAndEnabled) && (_closeButton.interactable))
            {
                _closeButton.onClick.Invoke();
            }

            return true;
        }
    }
}
