// System
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 친구, 길드 창이 함께 쓰는 거절 사유 문구, 결과 불명과 조회 실패 안내, 성공 안내, 소셜 알림 문구 </summary>
    public static class SocialViewRules
    {
        // 결과 불명 뒤 "새로 고쳤습니다"는 스냅샷을 실제로 받았을 때만 보인다. 받기 전에 보이면 옛 목록을 새 것으로 믿게 된다

        /****************************************
        *                Fields
        ****************************************/

        public const string UNKNOWN_TEXT         = "결과를 확인하지 못했습니다.";
        public const string REFRESHED_TEXT       = "목록을 새로 고쳤습니다.";
        public const string SNAPSHOT_FAILED_TEXT = "목록을 받지 못했습니다. 잠시 후 창을 다시 열어 주세요.";
        public const string WAITING_TEXT         = "요청 결과를 기다리고 있습니다.";
        public const string LOADING_TEXT         = "목록을 불러오는 중입니다.";
        public const string OFFLINE_TEXT         = "마을 서버에 연결하면 확인할 수 있습니다.";
        public const string DUNGEON_TEXT         = "친구와 길드 관리는 신시에서 할 수 있습니다. 출정 중에는 채팅과 알림만 받습니다.";
        public const string PREVIEW_TEXT         = "미리보기입니다. 요청을 보내지 않습니다.";
        public const string INVALID_NAME_TEXT    = "이름을 다시 확인해 주세요.";
        public const string INVALID_GUILD_TEXT   = "길드 이름은 2~8자의 한글, 영문, 숫자로 정합니다.";
        public const string UNKNOWN_NAME         = "알 수 없음";

        private const string FAILED_TEXT = "요청을 처리하지 못했습니다.";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 거절 사유의 안내 문구 </summary>
        public static string Describe(ERejectReason reason)
        {
            switch (reason)
            {
                case ERejectReason.SocialTargetNotFound:
                    return "해당 이름의 캐릭터가 없습니다.";
                case ERejectReason.SocialTargetSelf:
                    return "자기 자신은 대상으로 정할 수 없습니다.";
                case ERejectReason.SocialNotConnected:
                    return "접속 정보를 확인하지 못했습니다. 다시 접속해 주세요.";
                case ERejectReason.FriendAlready:
                    return "이미 친구입니다.";
                case ERejectReason.FriendLimit:
                    return $"친구는 {SocialLimits.MAX_FRIENDS}명까지 둘 수 있습니다.";
                case ERejectReason.FriendTargetLimit:
                    return "상대의 친구 목록이 가득 찼습니다.";
                case ERejectReason.FriendRequestLimit:
                    return "보낸 친구 요청이 너무 많습니다. 대기 중인 요청을 정리해 주세요.";
                case ERejectReason.FriendTargetRequestsFull:
                    return "상대가 받은 친구 요청이 가득 찼습니다.";
                case ERejectReason.FriendRequestNotFound:
                    return "친구 요청이 없거나 만료됐습니다.";
                case ERejectReason.WhisperTargetOffline:
                    return "상대가 접속 중이 아닙니다.";
                case ERejectReason.GuildNameInvalid:
                    return INVALID_GUILD_TEXT;
                case ERejectReason.GuildNameTaken:
                    return "이미 쓰고 있는 길드 이름입니다.";
                case ERejectReason.GuildAlreadyMember:
                    return "이미 길드에 가입해 있습니다.";
                case ERejectReason.GuildTargetInGuild:
                    return "상대가 이미 다른 길드에 있습니다.";
                case ERejectReason.GuildNotFound:
                    return "길드가 없거나 해체됐습니다.";
                case ERejectReason.GuildNotMember:
                    return "길드원이 아닙니다. 목록을 다시 확인해 주세요.";
                case ERejectReason.GuildNotLeader:
                    return "길드장만 할 수 있습니다.";
                case ERejectReason.GuildFull:
                    return $"길드 정원({SocialLimits.MAX_GUILD_MEMBERS}명)이 찼습니다.";
                case ERejectReason.GuildLeaderMustDelegate:
                    return GuildViewRules.MustDelegateText();
                case ERejectReason.GuildApplicationNotFound:
                    return "가입 신청이 없거나 만료됐습니다.";
                case ERejectReason.GuildInviteNotFound:
                    return "길드 초대가 없거나 만료됐습니다.";
                case ERejectReason.GuildApplicationLimit:
                    return "대기 중인 가입 신청이 너무 많습니다.";
                case ERejectReason.GuildPendingFull:
                    return "길드의 대기 신청과 초대가 가득 찼습니다.";
                case ERejectReason.GuildTargetInvitesFull:
                    return "상대가 받은 길드 초대가 가득 찼습니다.";
                case ERejectReason.GuildLeaderChanged:
                    return "길드장이 바뀌었습니다. 목록을 다시 확인해 주세요.";
                case ERejectReason.CharacterIsGuildLeader:
                    return "길드원이 있는 길드장은 먼저 길드장을 위임해야 합니다.";
                case ERejectReason.RateLimited:
                    return "잠시 후 다시 시도해 주세요.";
                case ERejectReason.FeatureDisabled:
                    return "현재 사용할 수 없는 기능입니다.";
                case ERejectReason.BackendUnavailable:
                    return UNKNOWN_TEXT;
                case ERejectReason.WrongLocation:
                    return DUNGEON_TEXT;
                case ERejectReason.PendingCommit:
                    return WAITING_TEXT;
                case ERejectReason.SessionMismatch:
                    return "캐릭터로 접속한 뒤 사용할 수 있습니다.";
                default:
                    return FAILED_TEXT;
            }
        }

        /// <summary> 확정된 요청의 안내 문구 </summary>
        public static string SuccessText(ESocialAction action)
        {
            switch (action)
            {
                case ESocialAction.FriendRequest:
                    return "친구 요청을 보냈습니다.";
                case ESocialAction.FriendAccept:
                    return "친구가 되었습니다.";
                case ESocialAction.FriendDecline:
                    return "친구 요청을 거절했습니다.";
                case ESocialAction.FriendCancel:
                    return "친구 요청을 취소했습니다.";
                case ESocialAction.FriendRemove:
                    return "친구를 삭제했습니다.";
                case ESocialAction.GuildCreate:
                    return "길드를 만들었습니다.";
                case ESocialAction.GuildApply:
                    return "가입 신청을 보냈습니다.";
                case ESocialAction.GuildApplicationAccept:
                    return "가입 신청을 수락했습니다.";
                case ESocialAction.GuildApplicationDecline:
                    return "가입 신청을 거절했습니다.";
                case ESocialAction.GuildInvite:
                    return "길드 초대를 보냈습니다.";
                case ESocialAction.GuildInviteAccept:
                    return "길드에 가입했습니다.";
                case ESocialAction.GuildInviteDecline:
                    return "길드 초대를 거절했습니다.";
                case ESocialAction.GuildLeave:
                    return "길드에서 탈퇴했습니다.";
                case ESocialAction.GuildKick:
                    return "길드원을 추방했습니다.";
                case ESocialAction.GuildDelegate:
                    return "길드장을 위임했습니다.";
                default:
                    return string.Empty;
            }
        }

        /// <summary> 친구 창이 다루는 요청인지 </summary>
        public static bool IsFriendAction(ESocialAction action)
        {
            return (action >= ESocialAction.FriendRequest) && (action <= ESocialAction.FriendRemove);
        }

        /// <summary> 길드 창이 다루는 요청인지 </summary>
        public static bool IsGuildAction(ESocialAction action)
        {
            return (action >= ESocialAction.GuildCreate) && (action <= ESocialAction.GuildDelegate);
        }

        /// <summary> 소셜 알림을 채팅 시스템 줄 하나로 바꾼다. 이름이 비어 있으면 알 수 없음으로 쓴다 </summary>
        public static string DescribeNotice(ESocialNotice kind, string actorName, string guildName)
        {
            string actor = string.IsNullOrEmpty(actorName) ? UNKNOWN_NAME : actorName;
            string guild = string.IsNullOrEmpty(guildName) ? UNKNOWN_NAME : guildName;
            switch (kind)
            {
                case ESocialNotice.FriendRequested:
                    return Format("{0}님이 친구 요청을 보냈습니다.", actor);
                case ESocialNotice.FriendAccepted:
                    return Format("{0}님과 친구가 되었습니다.", actor);
                case ESocialNotice.GuildInvited:
                    return Format("{0}님이 {1} 길드에 초대했습니다.", actor, guild);
                case ESocialNotice.GuildApplied:
                    return Format("{0}님이 길드 가입을 신청했습니다.", actor);
                case ESocialNotice.GuildJoined:
                    return Format("{0}님이 {1} 길드에 가입했습니다.", actor, guild);
                case ESocialNotice.GuildKicked:
                    return Format("{0} 길드에서 추방됐습니다.", guild);
                case ESocialNotice.GuildLeaderChanged:
                    return Format("{0}님이 길드장이 되었습니다.", actor);
                case ESocialNotice.GuildDisbanded:
                    return Format("{0} 길드가 해체됐습니다.", guild);
                case ESocialNotice.GuildMemberLeft:
                    return Format("{0}님이 길드를 떠났습니다.", actor);
                default:
                    return string.Empty;
            }
        }

        private static string Format(string format, string first)
        {
            return string.Format(CultureInfo.InvariantCulture, format, first);
        }

        private static string Format(string format, string first, string second)
        {
            return string.Format(CultureInfo.InvariantCulture, format, first, second);
        }
    }
}
