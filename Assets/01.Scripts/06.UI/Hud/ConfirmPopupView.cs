// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>확인은 true, 취소는 false를 호출자에게 한 번 돌려주고 닫는 확인창이다</summary>
    public sealed class ConfirmPopupView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("표시")]
        [SerializeField] private Button   _confirmButton = null;
        [SerializeField] private Button   _cancelButton  = null;
        [SerializeField] private TMP_Text _messageLabel  = null;

        private Action<bool> _onResult = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.AddListener(Accept);
            }

            if (_cancelButton != null)
            {
                _cancelButton.onClick.AddListener(Decline);
            }
        }

        private void OnDisable()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveListener(Accept);
            }

            if (_cancelButton != null)
            {
                _cancelButton.onClick.RemoveListener(Decline);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>이미 열려 있으면 이전 요청은 결과 없이 버리고 새 요청으로 바꾼다</summary>
        public void Show(string message, Action<bool> onResult)
        {
            _onResult = onResult;

            if (_messageLabel != null)
            {
                _messageLabel.text = message;
            }

            gameObject.SetActive(true);
            UiSound.Play(EUiSoundGroup.Confirm, EUiSoundAction.Open);
        }

        /// <summary>취소 버튼을 누른 것과 같다. Esc로 닫을 때 쓴다</summary>
        public void Cancel() { Finish(false); }

        private void Accept() { Finish(true); }

        private void Decline() { Finish(false); }

        private void Finish(bool result)
        {
            Action<bool> callback = _onResult;
            _onResult = null;
            gameObject.SetActive(false);
            UiSound.Play(EUiSoundGroup.Confirm, result ? EUiSoundAction.Confirm : EUiSoundAction.Cancel);

            if (callback != null)
            {
                callback(result);
            }
        }
    }
}
