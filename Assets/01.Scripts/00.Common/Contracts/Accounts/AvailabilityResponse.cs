// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 아이디, 캐릭터 이름 사용 가능 여부. 입력 중 안내용이며 조회와 생성 사이 경합은 생성 요청이 확정한다 </summary>
    [DataContract]
    public sealed class AvailabilityResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "available")]        private bool _available;

        // 사용할 수 없으면 ContractCodes.RejectReason(형식 오류 또는 사용 중), 사용할 수 있으면 NONE
        [DataMember(Name = "reasonCode")]       private int  _reasonCode;
        [DataMember(Name = "serverTimeUnixMs")] private long _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public bool Available        => _available;
        public int  ReasonCode       => _reasonCode;
        public long ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AvailabilityResponse(bool available, int reasonCode, long serverTimeUnixMs)
        {
            _available        = available;
            _reasonCode       = reasonCode;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private AvailabilityResponse() { }
    }
}
