// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Sessions;
using Shinshi.Data.Social;

namespace Shinshi.Backend.Social
{
    /// <summary> 마을 서버 전용 소셜 관리와 스냅샷. 던전 서버는 403이다. 응답은 SocialActionResponse(200 확정, 409 거절)이고
    /// 스냅샷은 SocialSnapshotResponse다. 기능 키 social이 꺼지면 탈퇴, 추방, 스냅샷을 뺀 관리 쓰기가 409 FEATURE_DISABLED다 </summary>
    [ApiController]
    public sealed class SocialController : SocialControllerBase
    {
        /****************************************
        *                Methods
        ****************************************/

        public SocialController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresSocialRepository social, SocialRequestLimiter limiter, TimeProvider timeProvider,
            ServiceFeatureGate? features = null)
            : base(callers, pool, social, limiter, timeProvider, features)
        {
        }

        /// <summary> 200 스냅샷, 409 SOCIAL_NOT_CONNECTED(ApiRejection) </summary>
        [HttpPost(SocialRouteTemplates.SNAPSHOT)]
        public async Task<IActionResult> Snapshot(string characterId, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(characterId, out Guid actor))
            {
                return BadRequest();
            }

            SocialSnapshotRecord? snapshot = await Social.ReadSnapshotAsync(ToCaller(caller), actor, cancellationToken);
            if (snapshot == null)
            {
                return Rejection(StatusCodes.Status409Conflict, ContractCodes.RejectReason.SOCIAL_NOT_CONNECTED);
            }

            return Ok(ToSnapshot(snapshot));
        }

        [HttpPost(SocialRouteTemplates.FRIEND_REQUEST)]
        public Task<IActionResult> RequestFriend(string characterId, [FromBody] SocialTargetNameRequest? request,
            CancellationToken cancellationToken)
        {
            string? nameKey = NameKeyOf(request?.TargetName);
            return RunAsync(characterId, request?.TargetName != null, true, true, (caller, actor, token) =>
                Social.RequestFriendAsync(caller, actor, nameKey, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.FRIEND_ACCEPT)]
        public Task<IActionResult> AcceptFriend(string characterId, string otherId, CancellationToken cancellationToken)
        {
            return RunPairAsync(characterId, otherId, true, true, (caller, actor, other, token) =>
                Social.AcceptFriendAsync(caller, actor, other, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.FRIEND_DECLINE)]
        public Task<IActionResult> DeclineFriend(string characterId, string otherId, CancellationToken cancellationToken)
        {
            return RunPairAsync(characterId, otherId, true, true, (caller, actor, other, token) =>
                Social.DeclineFriendAsync(caller, actor, other, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.FRIEND_CANCEL)]
        public Task<IActionResult> CancelFriendRequest(string characterId, string otherId, CancellationToken cancellationToken)
        {
            return RunPairAsync(characterId, otherId, true, true, (caller, actor, other, token) =>
                Social.CancelFriendRequestAsync(caller, actor, other, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.FRIEND_REMOVE)]
        public Task<IActionResult> RemoveFriend(string characterId, string otherId, CancellationToken cancellationToken)
        {
            return RunPairAsync(characterId, otherId, true, true, (caller, actor, other, token) =>
                Social.RemoveFriendAsync(caller, actor, other, token), cancellationToken);
        }

        /// <summary> 이름 규칙 위반은 409 GUILD_NAME_INVALID, createRequestId 형식 오류는 400이다 </summary>
        [HttpPost(SocialRouteTemplates.GUILD_CREATE)]
        public Task<IActionResult> CreateGuild(string characterId, [FromBody] SocialGuildCreateRequest? request,
            CancellationToken cancellationToken)
        {
            Guid createRequestId = IdOf(request?.CreateRequestId);
            bool named = GuildNameRules.TryNormalize(request?.Name, out string name, out string nameKey);
            return RunAsync(characterId, (request?.Name != null) && (createRequestId != Guid.Empty), true, false,
                (caller, actor, token) => Social.CreateGuildAsync(caller, actor, named ? name : null, named ? nameKey : null,
                    createRequestId, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.GUILD_APPLY)]
        public Task<IActionResult> ApplyToGuild(string characterId, [FromBody] SocialGuildApplyRequest? request,
            CancellationToken cancellationToken)
        {
            string? nameKey = GuildNameRules.TryNormalize(request?.GuildName, out _, out string key) ? key : null;
            return RunAsync(characterId, request?.GuildName != null, true, false, (caller, actor, token) =>
                Social.ApplyToGuildAsync(caller, actor, nameKey, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.GUILD_APPLICATION_ACCEPT)]
        public Task<IActionResult> AcceptApplication(string characterId, string otherId, CancellationToken cancellationToken)
        {
            return RunPairAsync(characterId, otherId, true, true, (caller, actor, other, token) =>
                Social.AcceptApplicationAsync(caller, actor, other, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.GUILD_APPLICATION_DECLINE)]
        public Task<IActionResult> DeclineApplication(string characterId, string otherId, CancellationToken cancellationToken)
        {
            return RunPairAsync(characterId, otherId, true, true, (caller, actor, other, token) =>
                Social.DeclineApplicationAsync(caller, actor, other, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.GUILD_INVITE)]
        public Task<IActionResult> InviteToGuild(string characterId, [FromBody] SocialTargetNameRequest? request,
            CancellationToken cancellationToken)
        {
            string? nameKey = NameKeyOf(request?.TargetName);
            return RunAsync(characterId, request?.TargetName != null, true, true, (caller, actor, token) =>
                Social.InviteToGuildAsync(caller, actor, nameKey, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.GUILD_INVITE_ACCEPT)]
        public Task<IActionResult> AcceptInvite(string characterId, string otherId, CancellationToken cancellationToken)
        {
            return RunPairAsync(characterId, otherId, true, true, (caller, actor, guild, token) =>
                Social.AcceptInviteAsync(caller, actor, guild, token), cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.GUILD_INVITE_DECLINE)]
        public Task<IActionResult> DeclineInvite(string characterId, string otherId, CancellationToken cancellationToken)
        {
            return RunPairAsync(characterId, otherId, true, true, (caller, actor, guild, token) =>
                Social.DeclineInviteAsync(caller, actor, guild, token), cancellationToken);
        }

        /// <summary> 기능 차단 중에도 허용한다(관계 정리) </summary>
        [HttpPost(SocialRouteTemplates.GUILD_LEAVE)]
        public Task<IActionResult> LeaveGuild(string characterId, [FromBody] SocialGuildMembershipRequest? request,
            CancellationToken cancellationToken)
        {
            Guid guildId = IdOf(request?.GuildId);
            Guid membershipId = IdOf(request?.MembershipId);
            return RunAsync(characterId, (guildId != Guid.Empty) && (membershipId != Guid.Empty), false, false,
                (caller, actor, token) => Social.LeaveGuildAsync(caller, actor, guildId, membershipId, token),
                cancellationToken);
        }

        /// <summary> 기능 차단 중에도 허용한다(관계 정리) </summary>
        [HttpPost(SocialRouteTemplates.GUILD_KICK)]
        public Task<IActionResult> KickMember(string characterId, string otherId,
            [FromBody] SocialGuildMembershipRequest? request, CancellationToken cancellationToken)
        {
            Guid guildId = IdOf(request?.GuildId);
            Guid membershipId = IdOf(request?.MembershipId);
            return RunPairAsync(characterId, otherId, (guildId != Guid.Empty) && (membershipId != Guid.Empty), false,
                (caller, actor, member, token) => Social.KickMemberAsync(caller, actor, member, guildId, membershipId, token),
                cancellationToken);
        }

        [HttpPost(SocialRouteTemplates.GUILD_DELEGATE)]
        public Task<IActionResult> DelegateLeader(string characterId, [FromBody] SocialGuildDelegateRequest? request,
            CancellationToken cancellationToken)
        {
            Guid guildId = IdOf(request?.GuildId);
            Guid newLeader = IdOf(request?.NewLeaderCharacterId);
            Guid membershipId = IdOf(request?.MembershipId);
            long expected = request?.ExpectedLeaderVersion ?? -1;
            bool valid = (guildId != Guid.Empty) && (newLeader != Guid.Empty) && (membershipId != Guid.Empty)
                && (expected >= 0);
            return RunAsync(characterId, valid, true, false, (caller, actor, token) => Social.DelegateLeaderAsync(caller,
                actor, guildId, newLeader, membershipId, expected, token), cancellationToken);
        }

        // 캐릭터 이름 규칙을 통과하면 비교 키, 아니면 null(대상 없음으로 거절된다)
        private static string? NameKeyOf(string? input)
        {
            return CharacterNameRules.TryNormalize(input, out _, out string key) ? key : null;
        }

        private static Guid IdOf(string? value)
        {
            return TryParseId(value, out Guid id) ? id : Guid.Empty;
        }

        private Task<IActionResult> RunPairAsync(string characterId, string otherId, bool valid, bool gated,
            Func<SocialCaller, Guid, Guid, CancellationToken, Task<SocialActionOutcome>> run,
            CancellationToken cancellationToken)
        {
            Guid other = IdOf(otherId);
            return RunAsync(characterId, (valid) && (other != Guid.Empty), gated, false,
                (caller, actor, token) => run(caller, actor, other, token), cancellationToken);
        }

        // 서버 자격(401, 403) -> 형식(400) -> 기능 차단(409) -> 한도(429) -> 저장소 순서로 판정한다
        private async Task<IActionResult> RunAsync(string characterId, bool valid, bool gated, bool invitation,
            Func<SocialCaller, Guid, CancellationToken, Task<SocialActionOutcome>> run, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if ((!valid) || (!TryParseId(characterId, out Guid actor)))
            {
                return BadRequest();
            }
            if ((gated) && (await IsBlockedAsync(cancellationToken)))
            {
                return Respond(ToResponse(ContractCodes.RejectReason.FEATURE_DISABLED),
                    ContractCodes.RejectReason.FEATURE_DISABLED);
            }

            int? retryAfter = Limiter.TryAcquireWrite(actor, invitation);
            if (retryAfter.HasValue)
            {
                return TooMany(retryAfter.Value);
            }

            SocialActionOutcome outcome = await run(ToCaller(caller), actor, cancellationToken);
            return Respond(ToResponse(outcome.ReasonCode, outcome.ChangedCharacterIds), outcome.ReasonCode);
        }

        private static SocialSnapshotResponse ToSnapshot(SocialSnapshotRecord snapshot)
        {
            SocialGuildRecord? guild = snapshot.Guild;
            return new SocialSnapshotResponse(new DateTimeOffset(snapshot.ServerTime).ToUnixTimeMilliseconds(),
                snapshot.Friends.Select(friend => new SocialFriendView(friend.CharacterId.ToString("D"), friend.Name,
                    friend.ClassId, friend.Presence.Presence, friend.Presence.SameServer)).ToArray(),
                snapshot.Incoming.Select(ToRequestView).ToArray(),
                snapshot.Outgoing.Select(ToRequestView).ToArray(),
                guild == null
                    ? new SocialGuildView(false, string.Empty, string.Empty, 0, SocialCodes.GuildRole.MEMBER, string.Empty, 0)
                    : new SocialGuildView(true, guild.GuildId.ToString("D"), guild.Name, guild.MemberCount, guild.MyRole,
                        guild.MyMembershipId.ToString("D"), guild.LeaderVersion),
                snapshot.Members.Select(member => new SocialGuildMemberView(member.CharacterId.ToString("D"),
                    member.MembershipId.ToString("D"), member.Name, member.ClassId, member.Role, member.Presence.Presence,
                    member.Presence.SameServer)).ToArray(),
                snapshot.GuildApplications.Select(ToRequestView).ToArray(),
                snapshot.GuildInvites.Select(invite => new SocialGuildInviteView(invite.GuildId.ToString("D"),
                    invite.GuildName, invite.InviterName, new DateTimeOffset(invite.ExpiresAt).ToUnixTimeMilliseconds()))
                    .ToArray());
        }

        private static SocialRequestView ToRequestView(SocialRequestRecord request)
        {
            return new SocialRequestView(request.CharacterId.ToString("D"), request.Name, request.ClassId,
                new DateTimeOffset(request.ExpiresAt).ToUnixTimeMilliseconds());
        }
    }
}
