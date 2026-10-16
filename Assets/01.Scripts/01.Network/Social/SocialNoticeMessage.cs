// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 백엔드 전달함 알림 행에서 만든다. 같은 행이 다시 올 수 있어 클라이언트가 DeliveryId로 중복을 버리고 수신 확인을 보낸다
    /// <summary> 친구 요청, 길드 초대, 가입 신청 도착 같은 소셜 변경 알림. 받으면 스냅샷을 다시 받는다 </summary>
    public readonly struct SocialNoticeMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ESocialNotice _kind;
        private readonly string        _actorName;
        private readonly string        _guildName;
        private readonly long          _deliveryId;

        /****************************************
        *              Properties
        ****************************************/

        public ESocialNotice Kind       => _kind;

        // 알림을 일으킨 캐릭터 이름, 삭제된 캐릭터면 비어 있다
        public string        ActorName  => _actorName ?? string.Empty;
        public string        GuildName  => _guildName ?? string.Empty;
        public long          DeliveryId => _deliveryId;

        /****************************************
        *                Methods
        ****************************************/

        public SocialNoticeMessage(ESocialNotice kind, string actorName, string guildName, long deliveryId)
        {
            _kind       = kind;
            _actorName  = actorName;
            _guildName  = guildName;
            _deliveryId = deliveryId;
        }
    }
}
