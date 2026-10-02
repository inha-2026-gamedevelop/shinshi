// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 접속 임대 이관 결과 </summary>
    [DataContract]
    public sealed class LeaseTransferResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "participantId")]    private string _participantId;
        [DataMember(Name = "leaseId")]          private string _leaseId;
        [DataMember(Name = "expiresAtUnixMs")]  private long   _expiresAtUnixMs;

        // ContractCodes.RejectReason, 0이면 승인
        [DataMember(Name = "reasonCode")]       private int    _reasonCode;
        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string ParticipantId    => _participantId;
        public string LeaseId          => _leaseId;
        public long   ExpiresAtUnixMs  => _expiresAtUnixMs;
        public int    ReasonCode       => _reasonCode;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public LeaseTransferResponse(string participantId, string leaseId, long expiresAtUnixMs, int reasonCode,
            long serverTimeUnixMs)
        {
            _participantId    = participantId;
            _leaseId          = leaseId;
            _expiresAtUnixMs  = expiresAtUnixMs;
            _reasonCode       = reasonCode;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private LeaseTransferResponse() { }
    }
}
