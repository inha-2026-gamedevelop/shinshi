// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 행위 캐릭터는 호출자가 인증 결합에서 꺼내 넘긴다. 메시지의 이름은 서버가 다시 정규화하고 ID는 UUID 형식만 통과시킨다.
    // 대상 ID가 남의 관계를 가리켜도 API가 트랜잭션 안에서 내 관계인지 확인하므로 여기서는 형식만 본다
    /// <summary> 소셜 관리 요청을 API 경로와 본문으로 바꾸는 순수 규칙 </summary>
    public static class SocialRequestRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ID_FORMAT = "D";

        /****************************************
        *                Methods
        ****************************************/

        public static string SnapshotPath(string actorId)
        {
            return Format(SocialRoutes.SNAPSHOT_FORMAT, actorId);
        }

        /// <summary> 쓰기 요청의 경로와 본문을 만든다. body가 null이면 본문 없는 POST다. 형식이 틀리면 거절 사유 </summary>
        public static ERejectReason TryBuild(in SocialRequestMessage message, string actorId, string actorName, out string path,
            out object body)
        {
            path = null;
            body = null;
            if (string.IsNullOrEmpty(actorId))
            {
                return ERejectReason.SessionMismatch;
            }

            switch (message.Action)
            {
                case ESocialAction.FriendRequest:
                    return BuildNamed(message.Name, actorName, SocialRoutes.FRIEND_REQUEST_FORMAT, actorId, out path, out body);
                case ESocialAction.FriendAccept:
                    return BuildTarget(message.TargetId, SocialRoutes.FRIEND_ACCEPT_FORMAT, actorId, out path);
                case ESocialAction.FriendDecline:
                    return BuildTarget(message.TargetId, SocialRoutes.FRIEND_DECLINE_FORMAT, actorId, out path);
                case ESocialAction.FriendCancel:
                    return BuildTarget(message.TargetId, SocialRoutes.FRIEND_CANCEL_FORMAT, actorId, out path);
                case ESocialAction.FriendRemove:
                    return BuildTarget(message.TargetId, SocialRoutes.FRIEND_REMOVE_FORMAT, actorId, out path);
                case ESocialAction.GuildCreate:
                    return BuildCreate(in message, actorId, out path, out body);
                case ESocialAction.GuildApply:
                    return BuildApply(message.Name, actorId, out path, out body);
                case ESocialAction.GuildApplicationAccept:
                    return BuildTarget(message.TargetId, SocialRoutes.GUILD_APPLICATION_ACCEPT_FORMAT, actorId, out path);
                case ESocialAction.GuildApplicationDecline:
                    return BuildTarget(message.TargetId, SocialRoutes.GUILD_APPLICATION_DECLINE_FORMAT, actorId, out path);
                case ESocialAction.GuildInvite:
                    return BuildNamed(message.Name, actorName, SocialRoutes.GUILD_INVITE_FORMAT, actorId, out path, out body);
                case ESocialAction.GuildInviteAccept:
                    return BuildTarget(message.TargetId, SocialRoutes.GUILD_INVITE_ACCEPT_FORMAT, actorId, out path);
                case ESocialAction.GuildInviteDecline:
                    return BuildTarget(message.TargetId, SocialRoutes.GUILD_INVITE_DECLINE_FORMAT, actorId, out path);
                case ESocialAction.GuildLeave:
                    return BuildLeave(in message, actorId, out path, out body);
                case ESocialAction.GuildKick:
                    return BuildKick(in message, actorId, out path, out body);
                case ESocialAction.GuildDelegate:
                    return BuildDelegate(in message, actorId, out path, out body);
                default:
                    return ERejectReason.InvalidTarget;
            }
        }

        /// <summary> UUID 문자열이면 소문자 하이픈 형식으로 바꾼다 </summary>
        public static bool TryNormalizeId(string value, out string id)
        {
            id = null;
            if ((string.IsNullOrEmpty(value)) || (value.Length > Constants.Social.MAX_ID_LENGTH)
                || (!Guid.TryParse(value, out Guid parsed)) || (parsed == Guid.Empty))
            {
                return false;
            }

            id = parsed.ToString(ID_FORMAT);
            return true;
        }

        /// <summary> 캐릭터 이름 입력이 행위 캐릭터 자신의 이름인지, 대소문자와 전각만 다른 이름도 같다 </summary>
        public static bool IsSelfName(string nameKey, string actorName)
        {
            return (!string.IsNullOrEmpty(actorName)) && (CharacterNameRules.TryNormalize(actorName, out _, out string actorKey))
                && (string.Equals(nameKey, actorKey, StringComparison.Ordinal));
        }

        private static ERejectReason BuildNamed(string input, string actorName, string format, string actorId, out string path,
            out object body)
        {
            path = null;
            body = null;
            if (!CharacterNameRules.TryNormalize(input, out string name, out string key))
            {
                return ERejectReason.SocialTargetNotFound;
            }

            if (IsSelfName(key, actorName))
            {
                return ERejectReason.SocialTargetSelf;
            }

            path = Format(format, actorId);
            body = new SocialTargetNameRequest(name);
            return ERejectReason.None;
        }

        private static ERejectReason BuildTarget(string targetId, string format, string actorId, out string path)
        {
            path = null;
            if (!TryNormalizeId(targetId, out string target))
            {
                return ERejectReason.InvalidTarget;
            }

            path = Format(format, actorId, target);
            return ERejectReason.None;
        }

        private static ERejectReason BuildCreate(in SocialRequestMessage message, string actorId, out string path, out object body)
        {
            path = null;
            body = null;
            if (!GuildNameRules.TryNormalize(message.Name, out string name, out _))
            {
                return ERejectReason.GuildNameInvalid;
            }

            path = Format(SocialRoutes.GUILD_CREATE_FORMAT, actorId);
            body = new SocialGuildCreateRequest(name, message.RequestId.ToString(ID_FORMAT));
            return ERejectReason.None;
        }

        private static ERejectReason BuildApply(string input, string actorId, out string path, out object body)
        {
            path = null;
            body = null;
            if (!GuildNameRules.TryNormalize(input, out string name, out _))
            {
                return ERejectReason.GuildNameInvalid;
            }

            path = Format(SocialRoutes.GUILD_APPLY_FORMAT, actorId);
            body = new SocialGuildApplyRequest(name);
            return ERejectReason.None;
        }

        private static ERejectReason BuildLeave(in SocialRequestMessage message, string actorId, out string path, out object body)
        {
            path = null;
            body = null;
            if ((!TryNormalizeId(message.GuildId, out string guildId)) || (!TryNormalizeId(message.MembershipId, out string membershipId)))
            {
                return ERejectReason.InvalidTarget;
            }

            path = Format(SocialRoutes.GUILD_LEAVE_FORMAT, actorId);
            body = new SocialGuildMembershipRequest(guildId, membershipId);
            return ERejectReason.None;
        }

        private static ERejectReason BuildKick(in SocialRequestMessage message, string actorId, out string path, out object body)
        {
            path = null;
            body = null;
            ERejectReason reason = ReadMemberTarget(in message, actorId, out string target, out string guildId, out string membershipId);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            path = Format(SocialRoutes.GUILD_KICK_FORMAT, actorId, target);
            body = new SocialGuildMembershipRequest(guildId, membershipId);
            return ERejectReason.None;
        }

        private static ERejectReason BuildDelegate(in SocialRequestMessage message, string actorId, out string path, out object body)
        {
            path = null;
            body = null;
            ERejectReason reason = ReadMemberTarget(in message, actorId, out string target, out string guildId, out string membershipId);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            if (message.ExpectedLeaderVersion < 0)
            {
                return ERejectReason.InvalidTarget;
            }

            path = Format(SocialRoutes.GUILD_DELEGATE_FORMAT, actorId);
            body = new SocialGuildDelegateRequest(guildId, target, membershipId, message.ExpectedLeaderVersion);
            return ERejectReason.None;
        }

        // 추방과 위임의 대상, 기대 길드, 대상 가입. 자기 자신은 대상이 될 수 없다
        private static ERejectReason ReadMemberTarget(in SocialRequestMessage message, string actorId, out string target,
            out string guildId, out string membershipId)
        {
            guildId      = null;
            membershipId = null;
            if ((!TryNormalizeId(message.TargetId, out target)) || (!TryNormalizeId(message.GuildId, out guildId))
                || (!TryNormalizeId(message.MembershipId, out membershipId)))
            {
                return ERejectReason.InvalidTarget;
            }

            if ((TryNormalizeId(actorId, out string actor)) && (string.Equals(actor, target, StringComparison.Ordinal)))
            {
                return ERejectReason.SocialTargetSelf;
            }

            return ERejectReason.None;
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
