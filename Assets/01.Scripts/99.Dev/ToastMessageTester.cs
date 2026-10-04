#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 프로젝트
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary>Dev_ToastMessage 씬에서 토스트, 얼러트, 컨펌을 키로 시험한다</summary>
    public sealed class ToastMessageTester : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int BURST_COUNT = 5;

        [SerializeField] private UiPopupHub _hub     = null;
        [SerializeField] private string[]   _samples = null;

        private int _nextIndex = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        // 1 토스트, 2 토스트 연속, 3 닫기, 4/5/6 얼러트 소형/중형/대형, 7/8/9 컨펌 소형/중형/대형
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ShowToast();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                for (int i = 0; i < BURST_COUNT; ++i)
                {
                    ShowToast();
                }
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                CloseAll();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                ShowAlert(EPopupSize.Small);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                ShowAlert(EPopupSize.Medium);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                ShowAlert(EPopupSize.Large);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                ShowConfirm(EPopupSize.Small);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha8))
            {
                ShowConfirm(EPopupSize.Medium);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha9))
            {
                ShowConfirm(EPopupSize.Large);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void ShowToast()
        {
            if (_hub != null)
            {
                _hub.ShowToast(NextSample());
            }
        }

        public void ShowAlert(EPopupSize size)
        {
            if (_hub != null)
            {
                _hub.ShowAlert(size, NextSample());
            }
        }

        public void ShowConfirm(EPopupSize size)
        {
            if (_hub != null)
            {
                _hub.ShowConfirm(size, NextSample(), OnConfirmResult);
            }
        }

        public void CloseAll()
        {
            if (_hub != null)
            {
                _hub.CloseAll();
            }
        }

        private void OnConfirmResult(bool result) { Debug.Log($"[ToastMessageTester] 컨펌 결과: {result}"); }

        private string NextSample()
        {
            if ((_samples == null) || (_samples.Length == 0))
            {
                return string.Empty;
            }

            string sample = _samples[_nextIndex % _samples.Length];
            ++_nextIndex;
            return sample;
        }
    }
}
#endif
