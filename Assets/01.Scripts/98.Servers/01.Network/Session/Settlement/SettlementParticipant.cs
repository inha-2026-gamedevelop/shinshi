// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 정산안에 넣을 참가자 한 명, Reason은 개인 이탈 원인이 있으면 그것이고 없으면 출정 종료 원인이다 </summary>
    public readonly struct SettlementParticipant
    {
        /****************************************
        *              Properties
        ****************************************/

        public string            ParticipantId { get; }
        public ESessionEndReason Reason        { get; }

        /****************************************
        *                Methods
        ****************************************/

        public SettlementParticipant(string participantId, ESessionEndReason reason)
        {
            ParticipantId = participantId;
            Reason        = reason;
        }
    }
}
