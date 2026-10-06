// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>얼러트, 컨펌, 토스트를 한곳에서 열어 주는 진입점이며 시작할 때 모든 팝업을 닫는다</summary>
    public sealed class UiPopupHub : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("얼러트 (소형, 중형, 대형 순서)")]
        [SerializeField] private AlertPopupView[] _alerts = null;

        [Header("컨펌 (소형, 중형, 대형 순서)")]
        [SerializeField] private ConfirmPopupView[] _confirms = null;

        [Header("토스트")]
        [SerializeField] private ToastMessageView _toast = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake() { CloseAll(); }

        /****************************************
        *                Methods
        ****************************************/

        public void ShowAlert(EPopupSize size, string message)
        {
            AlertPopupView view = Pick(_alerts, size);

            if (view != null)
            {
                view.Show(message);
            }
        }

        /// <summary>확인이면 true, 취소이면 false가 onResult로 한 번 전달된다</summary>
        public void ShowConfirm(EPopupSize size, string message, Action<bool> onResult)
        {
            ConfirmPopupView view = Pick(_confirms, size);

            if (view != null)
            {
                view.Show(message, onResult);
            }
        }

        public void ShowToast(string message)
        {
            if (_toast != null)
            {
                _toast.Show(message);
            }
        }

        public void CloseAll()
        {
            if (_alerts != null)
            {
                for (int i = 0; i < _alerts.Length; ++i)
                {
                    if (_alerts[i] != null)
                    {
                        _alerts[i].Close();
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
