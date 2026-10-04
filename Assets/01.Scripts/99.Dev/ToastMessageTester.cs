#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 프로젝트
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary>Dev_ToastMessage 씬에서 토스트 표시를 버튼과 키로 시험한다</summary>
    public sealed class ToastMessageTester : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int BURST_COUNT = 5;

        [SerializeField] private ToastMessageView _view = null;
        [SerializeField] private string[]         _samples = null;

        private int _nextIndex = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ShowNext();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ShowBurst();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                ClearAll();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void ShowNext()
        {
            if ((_view == null) || (_samples == null) || (_samples.Length == 0))
            {
                return;
            }

            _view.Show(_samples[_nextIndex % _samples.Length]);
            ++_nextIndex;
        }

        public void ShowBurst()
        {
            for (int i = 0; i < BURST_COUNT; ++i)
            {
                ShowNext();
            }
        }

        public void ClearAll()
        {
            if (_view != null)
            {
                _view.Clear();
            }
        }
    }
}
#endif
