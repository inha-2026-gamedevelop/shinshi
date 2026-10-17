namespace Shinshi.Network
{
    /// <summary> 소셜 변경 알림 종류, 백엔드 전달함 알림 행의 값과 같고 끝에만 붙인다 </summary>
    public enum ESocialNotice
    {
        FriendRequested    = 0,
        FriendAccepted     = 1,
        GuildInvited       = 2,
        GuildApplied       = 3,
        GuildJoined        = 4,
        GuildKicked        = 5,
        GuildLeaderChanged = 6,
        GuildDisbanded     = 7,
        GuildMemberLeft    = 8
    }
}
