namespace Shinshi.Contracts
{
    /// <summary> 게임 서버가 부르는 소셜 경로 </summary>
    public static class SocialRoutes
    {
        // 모든 경로는 서버 임대 Bearer와 X-Shinshi-Instance-Id 헤더가 필요하다. {0}의 행위 캐릭터는 호출 인스턴스에
        // 살아 있는 계정 임대(character_id)가 있어야 하며 없으면 409 SOCIAL_NOT_CONNECTED다.
        // 관리 경로는 마을 역할만(던전 403), 채팅과 꺼내기는 마을과 던전이 부른다. 관리 쓰기 응답은 SocialActionResponse다.
        // operationId가 없고 상태 기준으로 멱등이라 결과 불명이면 같은 요청을 다시 보낸다(길드 생성은 같은 createRequestId)

        /****************************************
        *                Fields
        ****************************************/

        // POST, 본문 없음 -> SocialSnapshotResponse
        public const string SNAPSHOT_FORMAT = "/v1/social/characters/{0}/snapshot";

        // POST, SocialTargetNameRequest
        public const string FRIEND_REQUEST_FORMAT = "/v1/social/characters/{0}/friends/requests";

        // POST, 본문 없음. {1}은 요청을 보낸 상대 캐릭터
        public const string FRIEND_ACCEPT_FORMAT  = "/v1/social/characters/{0}/friends/requests/{1}/accept";
        public const string FRIEND_DECLINE_FORMAT = "/v1/social/characters/{0}/friends/requests/{1}/decline";

        // POST, 본문 없음. {1}은 내가 요청을 보낸 대상 캐릭터
        public const string FRIEND_CANCEL_FORMAT = "/v1/social/characters/{0}/friends/requests/{1}/cancel";

        // POST, 본문 없음. {1}은 친구 캐릭터
        public const string FRIEND_REMOVE_FORMAT = "/v1/social/characters/{0}/friends/{1}/remove";

        // POST, SocialGuildCreateRequest
        public const string GUILD_CREATE_FORMAT = "/v1/social/characters/{0}/guilds";

        // POST, SocialGuildApplyRequest
        public const string GUILD_APPLY_FORMAT = "/v1/social/characters/{0}/guild-applications";

        // POST, 본문 없음(길드장). {1}은 신청한 캐릭터
        public const string GUILD_APPLICATION_ACCEPT_FORMAT  = "/v1/social/characters/{0}/guild/applications/{1}/accept";
        public const string GUILD_APPLICATION_DECLINE_FORMAT = "/v1/social/characters/{0}/guild/applications/{1}/decline";

        // POST, SocialTargetNameRequest(길드장)
        public const string GUILD_INVITE_FORMAT = "/v1/social/characters/{0}/guild/invites";

        // POST, 본문 없음. {1}은 초대한 길드
        public const string GUILD_INVITE_ACCEPT_FORMAT  = "/v1/social/characters/{0}/guild-invites/{1}/accept";
        public const string GUILD_INVITE_DECLINE_FORMAT = "/v1/social/characters/{0}/guild-invites/{1}/decline";

        // POST, SocialGuildMembershipRequest(내 가입)
        public const string GUILD_LEAVE_FORMAT = "/v1/social/characters/{0}/guild/leave";

        // POST, SocialGuildMembershipRequest(대상 가입, 길드장). {1}은 대상 길드원
        public const string GUILD_KICK_FORMAT = "/v1/social/characters/{0}/guild/members/{1}/kick";

        // POST, SocialGuildDelegateRequest(대상 가입, 길드장)
        public const string GUILD_DELEGATE_FORMAT = "/v1/social/characters/{0}/guild/leader";

        // POST, SocialWhisperRequest -> SocialWhisperResponse(마을, 던전)
        public const string WHISPER_FORMAT = "/v1/social/characters/{0}/whispers";

        // POST, SocialGuildChatRequest -> SocialGuildChatResponse(마을, 던전)
        public const string GUILD_CHAT_FORMAT = "/v1/social/characters/{0}/guild/chat";

        // POST, SocialDeliveryPullRequest -> SocialDeliveryPullResponse(마을, 던전). {0}은 호출 인스턴스
        public const string DELIVERY_PULL_FORMAT = "/v1/social/instances/{0}/deliveries/pull";
    }
}
