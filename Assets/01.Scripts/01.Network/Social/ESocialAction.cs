namespace Shinshi.Network
{
    /// <summary> 소셜 관리 요청 종류, 메시지에 번호로 실리므로 값을 바꾸지 않고 끝에 붙인다 </summary>
    public enum ESocialAction
    {
        Snapshot                = 0,
        FriendRequest           = 1,
        FriendAccept            = 2,
        FriendDecline           = 3,
        FriendCancel            = 4,
        FriendRemove            = 5,
        GuildCreate             = 6,
        GuildApply              = 7,
        GuildApplicationAccept  = 8,
        GuildApplicationDecline = 9,
        GuildInvite             = 10,
        GuildInviteAccept       = 11,
        GuildInviteDecline      = 12,
        GuildLeave              = 13,
        GuildKick               = 14,
        GuildDelegate           = 15
    }
}
