namespace Shinshi.Contracts
{
    /// <summary> 소셜 계약의 정수 코드. Unity의 같은 이름 enum(EPresence, EGuildRole, ESocialNotice, EChatChannel)과 값이 같아야 한다 </summary>
    public static class SocialCodes
    {
        // 소셜 거절 코드(1600대)는 ContractCodes.RejectReason 하나에만 두고 Unity ERejectReason과 번호를 맞춘다

        /// <summary> EPresence와 같은 값. 임대 role 0 마을, 1 던전 </summary>
        public static class Presence
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int OFFLINE = 0;
            public const int TOWN    = 1;
            public const int DUNGEON = 2;
        }

        /// <summary> EGuildRole과 같은 값, guild_members.role과 같다 </summary>
        public static class GuildRole
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int MEMBER = 0;
            public const int LEADER = 1;
        }

        /// <summary> social_deliveries.kind </summary>
        public static class DeliveryKind
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int WHISPER    = 0;
            public const int GUILD_CHAT = 1;
            public const int NOTICE     = 2;
        }

        /// <summary> 채팅 행의 channel. EChatChannel의 Guild(3), Whisper(4)와 같다 </summary>
        public static class ChatChannel
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int GUILD   = 3;
            public const int WHISPER = 4;
        }

        /// <summary> 알림 행(kind 2)의 channel. ESocialNotice와 같은 값이며 끝에만 붙인다 </summary>
        public static class Notice
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int FRIEND_REQUESTED     = 0;
            public const int FRIEND_ACCEPTED      = 1;
            public const int GUILD_INVITED        = 2;
            public const int GUILD_APPLIED        = 3;
            public const int GUILD_JOINED         = 4;
            public const int GUILD_KICKED         = 5;
            public const int GUILD_LEADER_CHANGED = 6;
            public const int GUILD_DISBANDED      = 7;
            public const int GUILD_MEMBER_LEFT    = 8;
        }
    }
}
