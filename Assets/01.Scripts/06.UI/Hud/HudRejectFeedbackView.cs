// Unity
using UnityEngine;

// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>
    /// 기술, 물약, 부활 요청이 거절됐을 때 사유를 한 줄 토스트로 보여 준다.
    /// 거절 이벤트를 받는 쪽(LocalPlayerView 구독자)이 Show를 부른다
    /// </summary>
    public sealed class HudRejectFeedbackView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 같은 사유가 이 시간 안에 반복되면 다시 띄우지 않는다. 키를 누르고 있을 때 문구가 쌓이는 것을 막는다
        private const float REPEAT_BLOCK_SECONDS = 0.8f;

        [SerializeField] private ToastMessageView _toast = null;

        private ERejectReason _lastReason = ERejectReason.None;
        private float        _lastTime   = float.NegativeInfinity;

        /****************************************
        *                Methods
        ****************************************/

        public void Show(ERejectReason reason)
        {
            string text = RejectText.Describe(reason);

            if ((_toast == null) || string.IsNullOrEmpty(text))
            {
                return;
            }

            float now = Time.unscaledTime;

            if ((reason == _lastReason) && ((now - _lastTime) < REPEAT_BLOCK_SECONDS))
            {
                return;
            }

            _lastReason = reason;
            _lastTime   = now;
            _toast.Show(text);
        }
    }
}
