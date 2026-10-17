// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 관리 쓰기 결과. Committed는 효과가 확정됐거나 이미 목표 상태였다는 뜻이다(상태 기준 멱등). Rejected에는 reasonCode가 있다 </summary>
    [DataContract]
    public sealed class SocialActionResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        // ContractCodes.OperationStatus의 COMMITTED 또는 REJECTED
        [DataMember(Name = "status")]              private int      _status;
        // REJECTED일 때 ContractCodes.RejectReason(소셜은 1600대)
        [DataMember(Name = "reasonCode")]          private int      _reasonCode;
        [DataMember(Name = "serverTimeUnixMs")]    private long     _serverTimeUnixMs;
        // 같은 트랜잭션에서 알림 전달함 행(kind 2)을 넣은 상대 캐릭터 ID. 접속 중인 상대에게만 넣는다
        [DataMember(Name = "changedCharacterIds")] private string[] _changedCharacterIds;

        /****************************************
        *              Properties
        ****************************************/

        public int      Status              => _status;
        public int      ReasonCode          => _reasonCode;
        public long     ServerTimeUnixMs    => _serverTimeUnixMs;
        public string[] ChangedCharacterIds => _changedCharacterIds;

        /****************************************
        *                Methods
        ****************************************/

        public SocialActionResponse(int status, int reasonCode, long serverTimeUnixMs, string[] changedCharacterIds)
        {
            _status              = status;
            _reasonCode          = reasonCode;
            _serverTimeUnixMs    = serverTimeUnixMs;
            _changedCharacterIds = changedCharacterIds;
        }

        private SocialActionResponse() { }
    }
}
