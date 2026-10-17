// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Social;

namespace Shinshi.Backend.Tests.Social
{
    /// <summary> 길드 생성, 이름 중복, 해체 후 재사용, 신청과 초대 교차, 정원, 탈퇴, 추방, 위임, 상태 기준 멱등과 member_count 불변식 </summary>
    public sealed class SocialGuildsPostgresTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Create_ReplayDuplicateAndReuseAfterDisband()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync();
            Guid requestId = Guid.NewGuid();
            Assert.True(GuildNameRules.TryNormalize(" 청룡Dan ", out string name, out string key));

            SocialTestEnvironment.AssertCommitted(await environment.Social.CreateGuildAsync(environment.Town,
                alice.CharacterId, name, key, requestId));
            // 같은 생성자, 같은 요청 ID는 처음 만든 길드로 성공한다(새 행 없음)
            SocialTestEnvironment.AssertCommitted(await environment.Social.CreateGuildAsync(environment.Town,
                alice.CharacterId, name, key, requestId));
            // 다른 요청 ID는 이미 길드에 있어 거절
            SocialTestEnvironment.AssertRejected(await environment.Social.CreateGuildAsync(environment.Town,
                alice.CharacterId, "다른길드", "다른길드", Guid.NewGuid()), ContractCodes.RejectReason.GUILD_ALREADY_MEMBER);
            // 전각, 대소문자만 다른 이름은 같은 이름
            Assert.True(GuildNameRules.TryNormalize("청룡ＤＡＮ", out string wide, out string wideKey));
            SocialTestEnvironment.AssertRejected(await environment.Social.CreateGuildAsync(environment.Town,
                bob.CharacterId, wide, wideKey, Guid.NewGuid()), ContractCodes.RejectReason.GUILD_NAME_TAKEN);
            SocialTestEnvironment.AssertRejected(await environment.Social.CreateGuildAsync(environment.Town,
                bob.CharacterId, null, null, Guid.NewGuid()), ContractCodes.RejectReason.GUILD_NAME_INVALID);
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM guilds;"));

            Guid guildId = await environment.GuildOfAsync(alice);
            SocialTestEnvironment.AssertCommitted(await environment.Social.LeaveGuildAsync(environment.Town,
                alice.CharacterId, guildId, await environment.MembershipOfAsync(alice)));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM guilds WHERE disbanded_at IS NOT NULL AND member_count = 0 AND leader_character_id IS NULL;"));
            SocialTestEnvironment.AssertCommitted(await environment.Social.CreateGuildAsync(environment.Town,
                bob.CharacterId, wide, wideKey, Guid.NewGuid()));
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task ApplyAndInvite_CrossJoinImmediately_AndAcceptPaths()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer leader = await environment.CreatePlayerAsync();
            SocialPlayer applicant = await environment.CreatePlayerAsync();
            SocialPlayer invitee = await environment.CreatePlayerAsync();
            SocialPlayer late = await environment.CreatePlayerAsync();
            Guid guildId = await environment.CreateGuildWithAsync(leader, "백호회");

            // 신청 뒤 길드장이 초대하면 곧바로 가입
            SocialActionOutcome applied = await environment.Social.ApplyToGuildAsync(environment.Town, applicant.CharacterId,
                "백호회");
            SocialTestEnvironment.AssertCommitted(applied);
            Assert.Equal(new[] { leader.CharacterId }, applied.ChangedCharacterIds);
            SocialTestEnvironment.AssertCommitted(await environment.Social.InviteToGuildAsync(environment.Town,
                leader.CharacterId, applicant.Name.ToLowerInvariant()));
            Assert.Equal(guildId, await environment.GuildOfAsync(applicant));

            // 초대 뒤 대상이 신청하면 곧바로 가입하고, 그 재전송도 성공한다
            SocialTestEnvironment.AssertCommitted(await environment.Social.InviteToGuildAsync(environment.Town,
                leader.CharacterId, invitee.Name.ToLowerInvariant()));
            SocialTestEnvironment.AssertCommitted(await environment.Social.ApplyToGuildAsync(environment.Town,
                invitee.CharacterId, "백호회"));
            SocialTestEnvironment.AssertCommitted(await environment.Social.ApplyToGuildAsync(environment.Town,
                invitee.CharacterId, "백호회"));
            Assert.Equal(guildId, await environment.GuildOfAsync(invitee));

            // 신청 -> 길드장 수락. 수락 재전송도 성공
            await environment.Social.ApplyToGuildAsync(environment.Town, late.CharacterId, "백호회");
            SocialSnapshotRecord leaderView = (await environment.Social.ReadSnapshotAsync(environment.Town, leader.CharacterId))!;
            Assert.Equal(late.CharacterId, Assert.Single(leaderView.GuildApplications).CharacterId);
            SocialActionOutcome accepted = await environment.Social.AcceptApplicationAsync(environment.Town,
                leader.CharacterId, late.CharacterId);
            SocialTestEnvironment.AssertCommitted(accepted);
            Assert.Contains(late.CharacterId, accepted.ChangedCharacterIds);
            SocialTestEnvironment.AssertCommitted(await environment.Social.AcceptApplicationAsync(environment.Town,
                leader.CharacterId, late.CharacterId));
            // 길드장이 아니면 수락 불가
            SocialTestEnvironment.AssertRejected(await environment.Social.AcceptApplicationAsync(environment.Town,
                late.CharacterId, applicant.CharacterId), ContractCodes.RejectReason.GUILD_NOT_LEADER);
            Assert.Equal(4L, await environment.ScalarAsync("SELECT member_count FROM guilds WHERE guild_id = @Id;",
                new { Id = guildId }));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM guild_join_requests;"));

            SocialSnapshotRecord memberView = (await environment.Social.ReadSnapshotAsync(environment.Town, late.CharacterId))!;
            Assert.Equal(4, memberView.Members.Count);
            Assert.Equal(leader.CharacterId, memberView.Members[0].CharacterId);
            Assert.Empty(memberView.GuildApplications);
            Assert.Equal(SocialCodes.GuildRole.MEMBER, memberView.Guild!.MyRole);
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task Join_FullGuild_RejectedWithGuildFull()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer leader = await environment.CreatePlayerAsync();
            Guid guildId = await environment.CreateGuildWithAsync(leader, "가득");
            for (int index = 1; index < SocialLimits.MAX_GUILD_MEMBERS; ++index)
            {
                await environment.AddMemberDirectAsync(guildId, await environment.CreatePlayerAsync(online: false));
            }

            SocialPlayer extra = await environment.CreatePlayerAsync();
            await environment.Social.ApplyToGuildAsync(environment.Town, extra.CharacterId, "가득");
            SocialTestEnvironment.AssertRejected(await environment.Social.AcceptApplicationAsync(environment.Town,
                leader.CharacterId, extra.CharacterId), ContractCodes.RejectReason.GUILD_FULL);
            Assert.Equal((long)SocialLimits.MAX_GUILD_MEMBERS, await environment.ScalarAsync(
                "SELECT member_count FROM guilds WHERE guild_id = @Id;", new { Id = guildId }));
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task PendingLimits_ApplicationsTargetInvitesAndGuildPending()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer applicant = await environment.CreatePlayerAsync();
            for (int index = 0; index <= SocialLimits.MAX_PENDING_APPLICATIONS; ++index)
            {
                SocialPlayer leader = await environment.CreatePlayerAsync();
                string guildName = "신청" + (char)('가' + index);
                await environment.CreateGuildWithAsync(leader, guildName);
                SocialActionOutcome outcome = await environment.Social.ApplyToGuildAsync(environment.Town,
                    applicant.CharacterId, guildName);
                Assert.Equal(index < SocialLimits.MAX_PENDING_APPLICATIONS
                    ? ContractCodes.RejectReason.NONE
                    : ContractCodes.RejectReason.GUILD_APPLICATION_LIMIT, outcome.ReasonCode);
            }

            SocialPlayer target = await environment.CreatePlayerAsync();
            for (int index = 0; index <= SocialLimits.MAX_PENDING_GUILD_INVITES; ++index)
            {
                SocialPlayer leader = await environment.CreatePlayerAsync();
                await environment.CreateGuildWithAsync(leader, "초대" + (char)('가' + index));
                SocialActionOutcome outcome = await environment.Social.InviteToGuildAsync(environment.Town,
                    leader.CharacterId, target.Name.ToLowerInvariant());
                Assert.Equal(index < SocialLimits.MAX_PENDING_GUILD_INVITES
                    ? ContractCodes.RejectReason.NONE
                    : ContractCodes.RejectReason.GUILD_TARGET_INVITES_FULL, outcome.ReasonCode);
            }
            Assert.Equal((long)SocialLimits.MAX_PENDING_GUILD_INVITES,
                (long)(await environment.Social.ReadSnapshotAsync(environment.Town, target.CharacterId))!.GuildInvites.Count);

            SocialPlayer busyLeader = await environment.CreatePlayerAsync();
            Guid busy = await environment.CreateGuildWithAsync(busyLeader, "북적");
            for (int index = 0; index < SocialLimits.MAX_GUILD_PENDING; ++index)
            {
                SocialPlayer pending = await environment.CreatePlayerAsync(online: false);
                await environment.ExecuteAsync(
                    "INSERT INTO guild_join_requests (guild_id, character_id, kind, expires_at) VALUES (@Guild, @Character, 0, clock_timestamp() + interval '1 day');",
                    new { Guild = busy, Character = pending.CharacterId });
            }
            SocialPlayer one = await environment.CreatePlayerAsync();
            SocialTestEnvironment.AssertRejected(await environment.Social.ApplyToGuildAsync(environment.Town,
                one.CharacterId, "북적"), ContractCodes.RejectReason.GUILD_PENDING_FULL);
            SocialTestEnvironment.AssertRejected(await environment.Social.InviteToGuildAsync(environment.Town,
                busyLeader.CharacterId, one.Name.ToLowerInvariant()), ContractCodes.RejectReason.GUILD_PENDING_FULL);

            // 만료된 신청은 세지 않는다
            await SocialFriendsPostgresTests.ExpireAsync(environment, "guild_join_requests");
            SocialTestEnvironment.AssertCommitted(await environment.Social.ApplyToGuildAsync(environment.Town,
                one.CharacterId, "북적"));
            SocialTestEnvironment.AssertRejected(await environment.Social.ApplyToGuildAsync(environment.Town,
                one.CharacterId, "없는길드"), ContractCodes.RejectReason.GUILD_NOT_FOUND);
        }

        [PostgresFact]
        public async Task Leave_LeaderWithMembersMustDelegate_StaleMembershipHasNoEffect()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer leader = await environment.CreatePlayerAsync();
            SocialPlayer member = await environment.CreatePlayerAsync();
            SocialPlayer otherLeader = await environment.CreatePlayerAsync();
            Guid guildId = await environment.CreateGuildWithAsync(leader, "첫길드", member);
            Guid otherGuild = await environment.CreateGuildWithAsync(otherLeader, "둘째");

            SocialTestEnvironment.AssertRejected(await environment.Social.LeaveGuildAsync(environment.Town,
                leader.CharacterId, guildId, await environment.MembershipOfAsync(leader)),
                ContractCodes.RejectReason.GUILD_LEADER_MUST_DELEGATE);

            Guid oldMembership = await environment.MembershipOfAsync(member);
            SocialActionOutcome left = await environment.Social.LeaveGuildAsync(environment.Town, member.CharacterId, guildId,
                oldMembership);
            SocialTestEnvironment.AssertCommitted(left);
            Assert.Equal(new[] { leader.CharacterId }, left.ChangedCharacterIds);

            // 탈퇴 뒤 다른 길드에 가입한 다음 늦게 온 옛 탈퇴 요청은 새 가입을 지우지 않는다
            await environment.Social.InviteToGuildAsync(environment.Town, otherLeader.CharacterId, member.Name.ToLowerInvariant());
            SocialTestEnvironment.AssertCommitted(await environment.Social.AcceptInviteAsync(environment.Town,
                member.CharacterId, otherGuild));
            SocialTestEnvironment.AssertCommitted(await environment.Social.LeaveGuildAsync(environment.Town,
                member.CharacterId, guildId, oldMembership));
            Assert.Equal(otherGuild, await environment.GuildOfAsync(member));
            // 같은 길드로 다시 들어가도 가입 버전이 달라 옛 요청이 작용하지 않는다
            await environment.Social.LeaveGuildAsync(environment.Town, member.CharacterId, otherGuild,
                await environment.MembershipOfAsync(member));
            await environment.Social.InviteToGuildAsync(environment.Town, leader.CharacterId, member.Name.ToLowerInvariant());
            await environment.Social.AcceptInviteAsync(environment.Town, member.CharacterId, guildId);
            SocialTestEnvironment.AssertCommitted(await environment.Social.LeaveGuildAsync(environment.Town,
                member.CharacterId, guildId, oldMembership));
            Assert.Equal(guildId, await environment.GuildOfAsync(member));
            Assert.NotEqual(oldMembership, await environment.MembershipOfAsync(member));
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task Kick_StaleMembershipAfterRejoin_HasNoEffect()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer leader = await environment.CreatePlayerAsync();
            SocialPlayer member = await environment.CreatePlayerAsync();
            SocialPlayer bystander = await environment.CreatePlayerAsync();
            Guid guildId = await environment.CreateGuildWithAsync(leader, "추방", member, bystander);
            Guid oldMembership = await environment.MembershipOfAsync(member);

            SocialTestEnvironment.AssertRejected(await environment.Social.KickMemberAsync(environment.Town,
                member.CharacterId, bystander.CharacterId, guildId, await environment.MembershipOfAsync(bystander)),
                ContractCodes.RejectReason.GUILD_NOT_LEADER);
            SocialTestEnvironment.AssertRejected(await environment.Social.KickMemberAsync(environment.Town,
                leader.CharacterId, leader.CharacterId, guildId, await environment.MembershipOfAsync(leader)),
                ContractCodes.RejectReason.SOCIAL_TARGET_SELF);
            SocialActionOutcome kicked = await environment.Social.KickMemberAsync(environment.Town, leader.CharacterId,
                member.CharacterId, guildId, oldMembership);
            SocialTestEnvironment.AssertCommitted(kicked);
            Assert.Equal(new[] { member.CharacterId, bystander.CharacterId }.OrderBy(id => id),
                kicked.ChangedCharacterIds.OrderBy(id => id));

            // 다시 가입한 길드원에게 늦게 온 옛 추방 요청은 효과가 없다
            await environment.Social.ApplyToGuildAsync(environment.Town, member.CharacterId, "추방");
            await environment.Social.AcceptApplicationAsync(environment.Town, leader.CharacterId, member.CharacterId);
            SocialTestEnvironment.AssertCommitted(await environment.Social.KickMemberAsync(environment.Town,
                leader.CharacterId, member.CharacterId, guildId, oldMembership));
            Assert.Equal(guildId, await environment.GuildOfAsync(member));
            Assert.Equal(3L, await environment.ScalarAsync("SELECT member_count FROM guilds WHERE guild_id = @Id;",
                new { Id = guildId }));
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task Delegate_LeaderVersionGuardsLateReplay()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync();
            SocialPlayer carol = await environment.CreatePlayerAsync();
            Guid guildId = await environment.CreateGuildWithAsync(alice, "위임", bob, carol);
            Guid bobMembership = await environment.MembershipOfAsync(bob);
            Guid aliceMembership = await environment.MembershipOfAsync(alice);

            SocialActionOutcome first = await environment.Social.DelegateLeaderAsync(environment.Town, alice.CharacterId,
                guildId, bob.CharacterId, bobMembership, 0);
            SocialTestEnvironment.AssertCommitted(first);
            Assert.Equal(new[] { bob.CharacterId, carol.CharacterId }.OrderBy(id => id),
                first.ChangedCharacterIds.OrderBy(id => id));
            // 같은 요청의 즉시 재전송은 이미 적용된 것으로 성공
            SocialTestEnvironment.AssertCommitted(await environment.Social.DelegateLeaderAsync(environment.Town,
                alice.CharacterId, guildId, bob.CharacterId, bobMembership, 0));
            Assert.Equal(1L, await environment.LeaderVersionAsync(guildId));

            SocialTestEnvironment.AssertCommitted(await environment.Social.DelegateLeaderAsync(environment.Town,
                bob.CharacterId, guildId, alice.CharacterId, aliceMembership, 1));
            // A가 다시 길드장이어도 늦게 도착한 A->B 재전송은 버전이 2 앞서 거절된다
            SocialTestEnvironment.AssertRejected(await environment.Social.DelegateLeaderAsync(environment.Town,
                alice.CharacterId, guildId, bob.CharacterId, bobMembership, 0), ContractCodes.RejectReason.GUILD_LEADER_CHANGED);
            // 버전이 맞아도 길드장이 아니면 거절, 대상 가입 버전이 다르면 길드원 아님
            SocialTestEnvironment.AssertRejected(await environment.Social.DelegateLeaderAsync(environment.Town,
                carol.CharacterId, guildId, bob.CharacterId, bobMembership, 2), ContractCodes.RejectReason.GUILD_NOT_LEADER);
            SocialTestEnvironment.AssertRejected(await environment.Social.DelegateLeaderAsync(environment.Town,
                alice.CharacterId, guildId, bob.CharacterId, Guid.NewGuid(), 2), ContractCodes.RejectReason.GUILD_NOT_MEMBER);
            Assert.Equal(alice.CharacterId, await environment.SingleAsync<Guid>(
                "SELECT leader_character_id FROM guilds WHERE guild_id = @Id;", new { Id = guildId }));
            Assert.Equal(2L, await environment.LeaderVersionAsync(guildId));
            await environment.AssertGuildInvariantsAsync();
        }
    }
}
