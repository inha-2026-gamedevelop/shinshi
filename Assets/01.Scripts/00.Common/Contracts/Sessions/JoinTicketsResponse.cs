// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 예약한 참가자 전원의 일회용 던전 입장권, 다시 받으면 이전 미교환 토큰은 폐기된다 </summary>
    [DataContract]
    public sealed class JoinTicketsResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "sessionId")]        private string       _sessionId;
        [DataMember(Name = "tickets")]          private JoinTicket[] _tickets;

        // ContractCodes.RejectReason, 0이면 승인
        [DataMember(Name = "reasonCode")]       private int          _reasonCode;
        [DataMember(Name = "serverTimeUnixMs")] private long         _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string       SessionId        => _sessionId;
        public JoinTicket[] Tickets          => _tickets;
        public int          ReasonCode       => _reasonCode;
        public long         ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public JoinTicketsResponse(string sessionId, JoinTicket[] tickets, int reasonCode, long serverTimeUnixMs)
        {
            _sessionId        = sessionId;
            _tickets          = tickets;
            _reasonCode       = reasonCode;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private JoinTicketsResponse() { }
    }
}
