// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 대기 중인 초대 요청에 대한 서버 응답 판정 결과 </summary>
    public enum EInviteOutcome
    {
        Ignored,
        Accepted,
        Rejected
    }

    /// <summary> 초대 창이 보낸 요청 한 건의 대기 상태, 요청 ID가 정확히 같은 응답만 자기 응답으로 받는다 </summary>
    // 다른 화면이 보낸 초대의 응답이나 닫기, 시간 초과 뒤에 늦게 온 응답은 ID가 달라 무시된다
    public sealed class PartyInviteTracker
    {
        /****************************************
        *                Fields
        ****************************************/

        private bool   _pending   = false;
        private uint   _requestId = 0;
        private float  _sentAt    = 0f;
        private string _target    = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public bool   IsPending => _pending;
        public uint   RequestId => _requestId;
        public string Target    => _target;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 요청을 보내기 전에 부른다. 응답이 보내는 도중이나 직후에 와도 대기 상태가 이미 걸려 있다 </summary>
        public void Begin(uint requestId, string target, float now)
        {
            _pending   = true;
            _requestId = requestId;
            _sentAt    = now;
            _target    = target ?? string.Empty;
        }

        public void Reset()
        {
            _pending   = false;
            _requestId = 0;
            _target    = string.Empty;
        }

        public bool IsExpired(float now, float timeoutSeconds)
        {
            return (_pending) && ((now - _sentAt) >= timeoutSeconds);
        }

        /// <summary> 대기 중이고 초대 응답이며 요청 ID가 0이 아니고 정확히 같을 때만 소비한다. 소비하면 대기를 푼다 </summary>
        public EInviteOutcome Resolve(in PartyResultMessage result, out string target)
        {
            target = string.Empty;
            if ((!_pending) || (result.Action != EPartyAction.Invite) || (_requestId == 0) || (result.RequestId != _requestId))
            {
                return EInviteOutcome.Ignored;
            }

            target = _target;
            Reset();
            return (result.Reason == ERejectReason.None) ? EInviteOutcome.Accepted : EInviteOutcome.Rejected;
        }
    }
}
