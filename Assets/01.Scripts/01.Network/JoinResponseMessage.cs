// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 서버 인증기의 인증 결과, 거절이면 서버가 잠시 뒤 연결을 끊는다 </summary>
    public readonly struct JoinResponseMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ERejectReason _reason;
        private readonly int           _participantId;

        /****************************************
        *              Properties
        ****************************************/

        // None이면 승인이다
        public ERejectReason Reason => _reason;

        // 승인 때 서버가 정한 참가자 번호, 거절이면 0
        public int ParticipantId => _participantId;

        public bool IsAccepted => _reason == ERejectReason.None;

        /****************************************
        *                Methods
        ****************************************/

        public JoinResponseMessage(ERejectReason reason, int participantId)
        {
            _reason        = reason;
            _participantId = participantId;
        }
    }
}
