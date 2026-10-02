// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    // 접속 집합은 마감 순간 던전 서버가 확정한 값이다. API는 저장한 값으로만 분배하고 재시도 때 다시 계산하지 않는다
    /// <summary> 경매 품목 마감, 종료 접속 집합을 저장하고 낙찰, 분배, 소각 또는 유찰을 한 번에 확정한다 </summary>
    [DataContract]
    public sealed class AuctionCloseRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]             private string   _operationId;
        [DataMember(Name = "connectedParticipantIds")] private string[] _connectedParticipantIds;

        // 던전 서버 시계가 확정한 종료 시각(유닉스 ms)
        [DataMember(Name = "endedAtUnixMs")]           private long     _endedAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string   OperationId             => _operationId;
        public string[] ConnectedParticipantIds => _connectedParticipantIds;
        public long     EndedAtUnixMs           => _endedAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AuctionCloseRequest(string operationId, string[] connectedParticipantIds, long endedAtUnixMs)
        {
            _operationId             = operationId;
            _connectedParticipantIds = connectedParticipantIds;
            _endedAtUnixMs           = endedAtUnixMs;
        }

        private AuctionCloseRequest() { }
    }
}
