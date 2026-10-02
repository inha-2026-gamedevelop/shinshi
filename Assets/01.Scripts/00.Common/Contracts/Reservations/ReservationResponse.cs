// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 예약 결과와 배정 던전, result가 COMMITTED일 때만 나머지 필드가 있다 </summary>
    [DataContract]
    public sealed class ReservationResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "result")]          private OperationResult       _result;
        [DataMember(Name = "sessionId")]       private string                _sessionId;
        [DataMember(Name = "instanceId")]      private string                _instanceId;
        [DataMember(Name = "address")]         private string                _address;
        [DataMember(Name = "port")]            private int                   _port;

        // 이 시각까지 /start가 없으면 API가 예약 전체를 해제한다
        [DataMember(Name = "expiresAtUnixMs")] private long                  _expiresAtUnixMs;
        [DataMember(Name = "participants")]    private ReservedParticipant[] _participants;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult       Result          => _result;
        public string                SessionId       => _sessionId;
        public string                InstanceId      => _instanceId;
        public string                Address         => _address;
        public int                   Port            => _port;
        public long                  ExpiresAtUnixMs => _expiresAtUnixMs;
        public ReservedParticipant[] Participants    => _participants;

        /****************************************
        *                Methods
        ****************************************/

        public ReservationResponse(OperationResult result, string sessionId, string instanceId, string address,
            int port, long expiresAtUnixMs, ReservedParticipant[] participants)
        {
            _result          = result;
            _sessionId       = sessionId;
            _instanceId      = instanceId;
            _address         = address;
            _port            = port;
            _expiresAtUnixMs = expiresAtUnixMs;
            _participants    = participants;
        }

        private ReservationResponse() { }
    }
}
