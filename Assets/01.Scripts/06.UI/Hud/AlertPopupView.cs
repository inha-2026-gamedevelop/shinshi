// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>확인 버튼 하나로 닫는 알림창이며 닫을 때 호출자에게 결과를 돌려주지 않는다</summary>
    public sealed class AlertPopupView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("표시")]
        [SerializeField] private Button   _confirmButton = null;
        [SerializeField] private TMP_Text _messageLabel  = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.AddListener(Close);
            }
        }

        private void OnDisable()
        {
            if (_confirmButton != null)
            {
                _confirmButton.onClick.RemoveListener(Close);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Show(string message)
        {
            if (_messageLabel != null)
            {
                _messageLabel.text = message;
            }

            gameObject.SetActive(true);
            UiSound.Play(EUiSoundGroup.Alert, EUiSoundAction.Open);
        }

        // 이미 닫힌 창을 다시 닫을 때는 소리를 내지 않는다
        public void Close()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            gameObject.SetActive(false);
            UiSound.Play(EUiSoundGroup.Alert, EUiSoundAction.Close);
        }
    }
}
