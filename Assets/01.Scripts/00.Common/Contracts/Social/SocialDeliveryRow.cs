// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 전달함 한 행. 로컬 전달과 꺼내기가 같은 모양으로 저장 내용 그대로 준다. Unity 서버는 deliveryId로 수신 확인 대기에 넣는다 </summary>
    [DataContract]
    public sealed class SocialDeliveryRow
    {
        /****************************************
        *                Fields
        ****************************************/

        // 0이 아닌 양수. 클라이언트 중복 제거 키
        [DataMember(Name = "deliveryId")]        private long   _deliveryId;
        [DataMember(Name = "targetCharacterId")] private string _targetCharacterId;
        // SocialCodes.DeliveryKind
        [DataMember(Name = "kind")]              private int    _kind;
        // kind 0, 1이면 SocialCodes.ChatChannel, kind 2이면 SocialCodes.Notice
        [DataMember(Name = "channel")]           private int    _channel;
        // 채팅은 보낸 캐릭터, 알림은 행위 캐릭터. 없으면 빈 문자열
        [DataMember(Name = "senderCharacterId")] private string _senderCharacterId;
        // 채팅은 보낸 이름, 알림은 행위 캐릭터 이름(이름 없는 캐릭터는 빈 문자열)
        [DataMember(Name = "senderName")]        private string _senderName;
        // 채팅은 본문, 알림은 길드 이름(없으면 빈 문자열)
        [DataMember(Name = "payload")]           private string _payload;
        [DataMember(Name = "createdAtUnixMs")]   private long   _createdAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public long   DeliveryId        => _deliveryId;
        public string TargetCharacterId => _targetCharacterId;
        public int    Kind              => _kind;
        public int    Channel           => _channel;
        public string SenderCharacterId => _senderCharacterId;
        public string SenderName        => _senderName;
        public string Payload           => _payload;
        public long   CreatedAtUnixMs   => _createdAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public SocialDeliveryRow(long deliveryId, string targetCharacterId, int kind, int channel, string senderCharacterId, string senderName, string payload, long createdAtUnixMs)
        {
            _deliveryId        = deliveryId;
            _targetCharacterId = targetCharacterId;
            _kind              = kind;
            _channel           = channel;
            _senderCharacterId = senderCharacterId;
            _senderName        = senderName;
            _payload           = payload;
            _createdAtUnixMs   = createdAtUnixMs;
        }

        private SocialDeliveryRow() { }
    }
}
