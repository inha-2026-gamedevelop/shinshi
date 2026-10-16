// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 꺼낸 행. delivery_id 오름차순이며 수신자당 최대 20개, 전체 최대 200개다 </summary>
    [DataContract]
    public sealed class SocialDeliveryPullResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "serverTimeUnixMs")] private long                _serverTimeUnixMs;
        [DataMember(Name = "deliveries")]       private SocialDeliveryRow[] _deliveries;

        /****************************************
        *              Properties
        ****************************************/

        public long                ServerTimeUnixMs => _serverTimeUnixMs;
        public SocialDeliveryRow[] Deliveries       => _deliveries;

        /****************************************
        *                Methods
        ****************************************/

        public SocialDeliveryPullResponse(long serverTimeUnixMs, SocialDeliveryRow[] deliveries)
        {
            _serverTimeUnixMs = serverTimeUnixMs;
            _deliveries       = deliveries;
        }

        private SocialDeliveryPullResponse() { }
    }
}
