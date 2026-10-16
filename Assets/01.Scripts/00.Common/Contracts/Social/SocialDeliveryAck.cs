// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 클라이언트가 수신 확인한 전달 행. 수신 캐릭터는 서버가 보낸 기록에서 정한다 </summary>
    [DataContract]
    public sealed class SocialDeliveryAck
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "deliveryId")]           private long   _deliveryId;
        [DataMember(Name = "recipientCharacterId")] private string _recipientCharacterId;

        /****************************************
        *              Properties
        ****************************************/

        public long   DeliveryId           => _deliveryId;
        public string RecipientCharacterId => _recipientCharacterId;

        /****************************************
        *                Methods
        ****************************************/

        public SocialDeliveryAck(long deliveryId, string recipientCharacterId)
        {
            _deliveryId           = deliveryId;
            _recipientCharacterId = recipientCharacterId;
        }

        private SocialDeliveryAck() { }
    }
}
