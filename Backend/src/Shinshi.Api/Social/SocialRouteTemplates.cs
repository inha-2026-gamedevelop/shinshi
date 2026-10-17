namespace Shinshi.Backend.Social
{
    /// <summary> SocialRoutes와 같은 경로의 ASP.NET Core 템플릿. 두 값이 어긋나지 않는지 테스트로 묶는다 </summary>
    public static class SocialRouteTemplates
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CHARACTER = "/v1/social/characters/{characterId}";

        public const string SNAPSHOT                  = CHARACTER + "/snapshot";
        public const string FRIEND_REQUEST            = CHARACTER + "/friends/requests";
        public const string FRIEND_ACCEPT             = CHARACTER + "/friends/requests/{otherId}/accept";
        public const string FRIEND_DECLINE            = CHARACTER + "/friends/requests/{otherId}/decline";
        public const string FRIEND_CANCEL             = CHARACTER + "/friends/requests/{otherId}/cancel";
        public const string FRIEND_REMOVE             = CHARACTER + "/friends/{otherId}/remove";
        public const string GUILD_CREATE              = CHARACTER + "/guilds";
        public const string GUILD_APPLY               = CHARACTER + "/guild-applications";
        public const string GUILD_APPLICATION_ACCEPT  = CHARACTER + "/guild/applications/{otherId}/accept";
        public const string GUILD_APPLICATION_DECLINE = CHARACTER + "/guild/applications/{otherId}/decline";
        public const string GUILD_INVITE              = CHARACTER + "/guild/invites";
        public const string GUILD_INVITE_ACCEPT       = CHARACTER + "/guild-invites/{otherId}/accept";
        public const string GUILD_INVITE_DECLINE      = CHARACTER + "/guild-invites/{otherId}/decline";
        public const string GUILD_LEAVE               = CHARACTER + "/guild/leave";
        public const string GUILD_KICK                = CHARACTER + "/guild/members/{otherId}/kick";
        public const string GUILD_DELEGATE            = CHARACTER + "/guild/leader";
        public const string WHISPER                   = CHARACTER + "/whispers";
        public const string GUILD_CHAT                = CHARACTER + "/guild/chat";
        public const string DELIVERY_PULL             = "/v1/social/instances/{instanceId}/deliveries/pull";
    }
}
