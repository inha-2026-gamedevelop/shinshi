// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary>얼러트, 컨펌, 토스트를 한곳에서 열어 주는 진입점이며 시작할 때 모든 팝업을 닫는다. 열린 팝업은 Esc로 닫는다</summary>
    public sealed class UiPopupHub : MonoBehaviour
    {
        // 팝업을 띄울 때마다 창 닫기 처리기를 맨 뒤로 다시 등록해 뒤의 창보다 먼저 Esc를 받는다.
        // 컨펌은 Esc를 취소로 돌려준다

        /****************************************
        *                Fields
        ****************************************/

        [Header("얼러트 (소형, 중형, 대형 순서)")]
        [SerializeField] private AlertPopupView[] _alerts = null;

        [Header("컨펌 (소형, 중형, 대형 순서)")]
        [SerializeField] private ConfirmPopupView[] _confirms = null;

        [Header("토스트")]
        [SerializeField] private ToastMessageView _toast = null;

        [Header("단축키")]
        [SerializeField] private HotkeyRouter _router = null;

        private Func<bool> _onEscape = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _onEscape = OnEscape;
            CloseAll();
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

        public void ShowAlert(EPopupSize size, string message)
        {
            AlertPopupView view = Pick(_alerts, size);

            if (view != null)
            {
                view.Show(message);
                RegisterEscape();
            }
        }

        /// <summary>확인이면 true, 취소이면 false가 onResult로 한 번 전달된다</summary>
        public void ShowConfirm(EPopupSize size, string message, Action<bool> onResult)
        {
            ConfirmPopupView view = Pick(_confirms, size);

            if (view != null)
            {
                view.Show(message, onResult);
                RegisterEscape();
            }
        }

        public void ShowToast(string message)
        {
            if (_toast != null)
            {
                _toast.Show(message);
            }
        }

        // 시작 정리와 화면 전환용이라 닫기 소리 없이 끈다. Awake에서 열린 채 저장된 창이 닫히며 소리가 나면 안 된다
        public void CloseAll()
        {
            if (_alerts != null)
            {
                for (int i = 0; i < _alerts.Length; ++i)
                {
                    if (_alerts[i] != null)
                    {
                        _alerts[i].gameObject.SetActive(false);
                    }
                }
            }

            if (_confirms != null)
            {
                for (int i = 0; i < _confirms.Length; ++i)
                {
                    if (_confirms[i] != null)
                    {
                        _confirms[i].gameObject.SetActive(false);
                    }
                }
            }
        }

        private void RegisterEscape()
        {
            if ((_router != null) && (_onEscape != null))
            {
                _router.Register(EHotkeyAction.CloseWindow, _onEscape);
            }
        }

        // 버튼으로 이미 다 닫혔으면 뒤의 창에 넘긴다. 취소 결과를 받은 쪽이 새 팝업을 띄우면 그 팝업이 다시 등록하도록 먼저 푼다
        private bool OnEscape()
        {
            _router.Unregister(EHotkeyAction.CloseWindow, _onEscape);
            bool closed = false;
            if (_confirms != null)
            {
                for (int i = 0; i < _confirms.Length; ++i)
                {
                    if ((_confirms[i] != null) && (_confirms[i].gameObject.activeInHierarchy))
                    {
                        _confirms[i].Cancel();
                        closed = true;
                    }
                }
            }

            if (_alerts != null)
            {
                for (int i = 0; i < _alerts.Length; ++i)
                {
                    if ((_alerts[i] != null) && (_alerts[i].gameObject.activeInHierarchy))
                    {
                        _alerts[i].Close();
                        closed = true;
                    }
                }
            }

            return closed;
        }

        private static T Pick<T>(T[] views, EPopupSize size) where T : class
        {
            int index = (int)size;

            if ((views == null) || (index >= views.Length))
            {
                return null;
            }

            return views[index];
        }
    }
}
