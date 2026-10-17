// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 길드 채팅 결과. 성공이면 호출 인스턴스에 있는 길드원(보낸 사람 포함)마다 남은 행(새 행 포함)을 수신자별 오름차순으로 준다 </summary>
    [DataContract]
    public sealed class SocialGuildChatResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "result")]          private SocialActionResponse _result;
        // 보낸 캐릭터의 저장된 이름
        [DataMember(Name = "senderName")]      private string               _senderName;
        // targetCharacterId, deliveryId 순
        [DataMember(Name = "localDeliveries")] private SocialDeliveryRow[]  _localDeliveries;

        /****************************************
        *              Properties
        ****************************************/

        public SocialActionResponse Result          => _result;
        public string               SenderName      => _senderName;
        public SocialDeliveryRow[]  LocalDeliveries => _localDeliveries;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildChatResponse(SocialActionResponse result, string senderName, SocialDeliveryRow[] localDeliveries)
        {
            _result          = result;
            _senderName      = senderName;
            _localDeliveries = localDeliveries;
        }

        private SocialGuildChatResponse() { }
    }
}
