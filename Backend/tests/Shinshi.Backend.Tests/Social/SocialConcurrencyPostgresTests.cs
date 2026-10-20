// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Characters;
using Shinshi.Data.Social;

namespace Shinshi.Backend.Tests.Social
{
    /// <summary> 경합 전이에서 한쪽만 이기고 불변식이 유지되는지, 캐릭터 삭제 연동이 교착 없이 끝나는지 실제 PostgreSQL에서 검사한다.
    /// 각 경합은 같은 시작 상태에서 Task.WhenAll로 동시에 보내며 여러 번 반복한다 </summary>
    public sealed class SocialConcurrencyPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int ROUNDS = 5;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task SameApplicant_TwoGuildsAcceptConcurrently_OnlyOneJoins()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer first = await environment.CreatePlayerAsync();
                SocialPlayer second = await environment.CreatePlayerAsync();
                SocialPlayer applicant = await environment.CreatePlayerAsync();
                await environment.CreateGuildWithAsync(first, "가" + (char)('가' + round));
                await environment.CreateGuildWithAsync(second, "나" + (char)('가' + round));
                await environment.Social.ApplyToGuildAsync(environment.Town, applicant.CharacterId, "가" + (char)('가' + round));
                await environment.Social.ApplyToGuildAsync(environment.Town, applicant.CharacterId, "나" + (char)('가' + round));

                SocialActionOutcome[] results = await Task.WhenAll(
                    Task.Run(() => environment.Social.AcceptApplicationAsync(environment.Town, first.CharacterId,
                        applicant.CharacterId)),
                    Task.Run(() => environment.Social.AcceptApplicationAsync(environment.Town, second.CharacterId,
                        applicant.CharacterId)));

                Assert.Equal(1, results.Count(result => result.IsCommitted));
                // 먼저 가입한 길드가 신청 행을 모두 지우므로 뒤는 신청 없음 또는 다른 길드 소속이다
                Assert.Contains(results.Single(result => !result.IsCommitted).ReasonCode, new[]
                {
                    ContractCodes.RejectReason.GUILD_TARGET_IN_GUILD, ContractCodes.RejectReason.GUILD_APPLICATION_NOT_FOUND
                });
            }
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task Capacity29_TwoAcceptsConcurrently_OneGuildFull()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer leader = await environment.CreatePlayerAsync();
                string name = "정원" + (char)('가' + round);
                Guid guildId = await environment.CreateGuildWithAsync(leader, name);
                for (int index = 2; index < SocialLimits.MAX_GUILD_MEMBERS; ++index)
                {
                    await environment.AddMemberDirectAsync(guildId, await environment.CreatePlayerAsync(online: false));
                }
                SocialPlayer one = await environment.CreatePlayerAsync();
                SocialPlayer two = await environment.CreatePlayerAsync();
                await environment.Social.ApplyToGuildAsync(environment.Town, one.CharacterId, name);
                await environment.Social.ApplyToGuildAsync(environment.Town, two.CharacterId, name);

                // 길드장 한 명이 두 신청을 동시에 수락(같은 길드장 행에서 줄을 선다), 그리고 초대 수락 경로도 길드 행에서 줄을 선다
                SocialActionOutcome[] results = await Task.WhenAll(
                    Task.Run(() => environment.Social.AcceptApplicationAsync(environment.Town, leader.CharacterId,
                        one.CharacterId)),
                    Task.Run(() => environment.Social.AcceptApplicationAsync(environment.Town, leader.CharacterId,
                        two.CharacterId)));

                Assert.Equal(1, results.Count(result => result.IsCommitted));
                Assert.Equal(ContractCodes.RejectReason.GUILD_FULL, results.Single(result => !result.IsCommitted).ReasonCode);
                Assert.Equal((long)SocialLimits.MAX_GUILD_MEMBERS, await environment.ScalarAsync(
                    "SELECT member_count FROM guilds WHERE guild_id = @Id;", new { Id = guildId }));
            }
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task Capacity29_TwoInviteAcceptsConcurrently_OneGuildFull()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer leader = await environment.CreatePlayerAsync();
                Guid guildId = await environment.CreateGuildWithAsync(leader, "초정" + (char)('가' + round));
                for (int index = 2; index < SocialLimits.MAX_GUILD_MEMBERS; ++index)
                {
                    await environment.AddMemberDirectAsync(guildId, await environment.CreatePlayerAsync(online: false));
                }
                SocialPlayer one = await environment.CreatePlayerAsync();
                SocialPlayer two = await environment.CreatePlayerAsync();
                await environment.Social.InviteToGuildAsync(environment.Town, leader.CharacterId, one.Name.ToLowerInvariant());
                await environment.Social.InviteToGuildAsync(environment.Town, leader.CharacterId, two.Name.ToLowerInvariant());

                // 서로 다른 캐릭터 행이라 길드 행 잠금과 정원 조건부 갱신만이 막는다
                SocialActionOutcome[] results = await Task.WhenAll(
                    Task.Run(() => environment.Social.AcceptInviteAsync(environment.Town, one.CharacterId, guildId)),
                    Task.Run(() => environment.Social.AcceptInviteAsync(environment.Town, two.CharacterId, guildId)));

                Assert.Equal(1, results.Count(result => result.IsCommitted));
                Assert.Equal(ContractCodes.RejectReason.GUILD_FULL, results.Single(result => !result.IsCommitted).ReasonCode);
            }
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task TwoDelegationsConcurrently_LaterGetsLeaderChanged()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer leader = await environment.CreatePlayerAsync();
                SocialPlayer bob = await environment.CreatePlayerAsync();
                SocialPlayer carol = await environment.CreatePlayerAsync();
                Guid guildId = await environment.CreateGuildWithAsync(leader, "위" + (char)('가' + round), bob, carol);
                Guid bobMembership = await environment.MembershipOfAsync(bob);
                Guid carolMembership = await environment.MembershipOfAsync(carol);

                SocialActionOutcome[] results = await Task.WhenAll(
                    Task.Run(() => environment.Social.DelegateLeaderAsync(environment.Town, leader.CharacterId, guildId,
                        bob.CharacterId, bobMembership, 0)),
                    Task.Run(() => environment.Social.DelegateLeaderAsync(environment.Town, leader.CharacterId, guildId,
                        carol.CharacterId, carolMembership, 0)));

                Assert.Equal(1, results.Count(result => result.IsCommitted));
                Assert.Equal(ContractCodes.RejectReason.GUILD_LEADER_CHANGED,
                    results.Single(result => !result.IsCommitted).ReasonCode);
                Assert.Equal(1L, await environment.LeaderVersionAsync(guildId));
            }
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task DisbandAndInviteAcceptConcurrently_ExactlyOneOutcome()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer leader = await environment.CreatePlayerAsync();
                SocialPlayer invitee = await environment.CreatePlayerAsync();
                Guid guildId = await environment.CreateGuildWithAsync(leader, "해" + (char)('가' + round));
                await environment.Social.InviteToGuildAsync(environment.Town, leader.CharacterId,
                    invitee.Name.ToLowerInvariant());
                Guid membership = await environment.MembershipOfAsync(leader);

                SocialActionOutcome[] results = await Task.WhenAll(
                    Task.Run(() => environment.Social.LeaveGuildAsync(environment.Town, leader.CharacterId, guildId,
                        membership)),
                    Task.Run(() => environment.Social.AcceptInviteAsync(environment.Town, invitee.CharacterId, guildId)));

                bool disbanded = results[0].IsCommitted;
                if (disbanded)
                {
                    Assert.Contains(results[1].ReasonCode, new[]
                    {
                        ContractCodes.RejectReason.GUILD_INVITE_NOT_FOUND, ContractCodes.RejectReason.GUILD_NOT_FOUND
                    });
                }
                else
                {
                    Assert.Equal(ContractCodes.RejectReason.GUILD_LEADER_MUST_DELEGATE, results[0].ReasonCode);
                    Assert.True(results[1].IsCommitted);
                }
            }
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task SameNameCreatedConcurrently_OneTaken()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer one = await environment.CreatePlayerAsync();
                SocialPlayer two = await environment.CreatePlayerAsync();
                string name = "Same" + (char)('A' + round);
                string upper = name.ToUpperInvariant();

                SocialActionOutcome[] results = await Task.WhenAll(
                    Task.Run(() => environment.Social.CreateGuildAsync(environment.Town, one.CharacterId, name,
                        name.ToLowerInvariant(), Guid.NewGuid())),
                    Task.Run(() => environment.Social.CreateGuildAsync(environment.Town, two.CharacterId, upper,
                        upper.ToLowerInvariant(), Guid.NewGuid())));

                Assert.Equal(1, results.Count(result => result.IsCommitted));
                Assert.Equal(ContractCodes.RejectReason.GUILD_NAME_TAKEN, results.Single(result => !result.IsCommitted).ReasonCode);
            }
            Assert.Equal((long)ROUNDS, await environment.ScalarAsync("SELECT count(*) FROM guilds;"));
            Assert.Equal((long)ROUNDS, await environment.ScalarAsync("SELECT count(*) FROM guild_members;"));
        }

        [PostgresFact]
        public async Task Friends49_TwoAcceptsConcurrently_OneFriendLimit()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer owner = await environment.CreatePlayerAsync();
                for (int index = 1; index < SocialLimits.MAX_FRIENDS; ++index)
                {
                    SocialPlayer friend = await environment.CreatePlayerAsync(online: false);
                    await environment.ExecuteAsync(
                        "INSERT INTO friendships (character_id, friend_character_id) VALUES (@A, @B), (@B, @A);",
                        new { A = owner.CharacterId, B = friend.CharacterId });
                }
                SocialPlayer one = await environment.CreatePlayerAsync();
                SocialPlayer two = await environment.CreatePlayerAsync();
                await environment.Social.RequestFriendAsync(environment.Town, one.CharacterId, owner.Name.ToLowerInvariant());
                await environment.Social.RequestFriendAsync(environment.Town, two.CharacterId, owner.Name.ToLowerInvariant());

                SocialActionOutcome[] results = await Task.WhenAll(
                    Task.Run(() => environment.Social.AcceptFriendAsync(environment.Town, owner.CharacterId, one.CharacterId)),
                    Task.Run(() => environment.Social.AcceptFriendAsync(environment.Town, owner.CharacterId, two.CharacterId)));

                Assert.Equal(1, results.Count(result => result.IsCommitted));
                Assert.Equal(ContractCodes.RejectReason.FRIEND_LIMIT, results.Single(result => !result.IsCommitted).ReasonCode);
                Assert.Equal((long)SocialLimits.MAX_FRIENDS, await environment.ScalarAsync(
                    "SELECT count(*) FROM friendships WHERE character_id = @Id;", new { Id = owner.CharacterId }));
            }
        }

        [PostgresFact]
        public async Task TargetWithFourInvites_TwoGuildsInviteConcurrently_OneInvitesFull()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer target = await environment.CreatePlayerAsync();
                for (int index = 1; index < SocialLimits.MAX_PENDING_GUILD_INVITES; ++index)
                {
                    SocialPlayer leader = await environment.CreatePlayerAsync();
                    await environment.CreateGuildWithAsync(leader, "선" + (char)('가' + round) + (char)('가' + index));
                    SocialTestEnvironment.AssertCommitted(await environment.Social.InviteToGuildAsync(environment.Town,
                        leader.CharacterId, target.Name.ToLowerInvariant()));
                }
                SocialPlayer firstLeader = await environment.CreatePlayerAsync();
                SocialPlayer secondLeader = await environment.CreatePlayerAsync();
                await environment.CreateGuildWithAsync(firstLeader, "갑" + (char)('가' + round));
                await environment.CreateGuildWithAsync(secondLeader, "을" + (char)('가' + round));

                SocialActionOutcome[] results = await Task.WhenAll(
                    Task.Run(() => environment.Social.InviteToGuildAsync(environment.Town, firstLeader.CharacterId,
                        target.Name.ToLowerInvariant())),
                    Task.Run(() => environment.Social.InviteToGuildAsync(environment.Town, secondLeader.CharacterId,
                        target.Name.ToLowerInvariant())));

                Assert.Equal(1, results.Count(result => result.IsCommitted));
                Assert.Equal(ContractCodes.RejectReason.GUILD_TARGET_INVITES_FULL,
                    results.Single(result => !result.IsCommitted).ReasonCode);
                Assert.Equal((long)SocialLimits.MAX_PENDING_GUILD_INVITES, await environment.ScalarAsync(
                    "SELECT count(*) FROM guild_join_requests WHERE character_id = @Id AND kind = 1;",
                    new { Id = target.CharacterId }));
            }
        }

        [PostgresFact]
        public async Task FriendRequestsCrossedConcurrently_BecomeFriendsOnce()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer one = await environment.CreatePlayerAsync();
                SocialPlayer two = await environment.CreatePlayerAsync();

                SocialActionOutcome[] results = await Task.WhenAll(
                    Task.Run(() => environment.Social.RequestFriendAsync(environment.Town, one.CharacterId,
                        two.Name.ToLowerInvariant())),
                    Task.Run(() => environment.Social.RequestFriendAsync(environment.Town, two.CharacterId,
                        one.Name.ToLowerInvariant())));

                Assert.All(results, result => SocialTestEnvironment.AssertCommitted(result));
                Assert.Equal(2L, await environment.ScalarAsync(
                    "SELECT count(*) FROM friendships WHERE character_id = ANY(@Ids);",
                    new { Ids = new[] { one.CharacterId, two.CharacterId } }));
                Assert.Equal(0L, await environment.ScalarAsync(
                    "SELECT count(*) FROM friend_requests WHERE requester_character_id = ANY(@Ids);",
                    new { Ids = new[] { one.CharacterId, two.CharacterId } }));
            }
        }

        [PostgresFact]
        public async Task DeleteCharacters_GuildmatesAndFriendsConcurrently_BothFinishWithoutDeadlock()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            PostgresCharacterRepository characters = new PostgresCharacterRepository(environment.DataSource,
                new IdempotentOperationExecutor(new PostgresOperationStore(environment.DataSource)));
            for (int round = 0; round < ROUNDS; ++round)
            {
                SocialPlayer leader = await environment.CreatePlayerAsync();
                SocialPlayer one = await environment.CreatePlayerAsync(online: false);
                SocialPlayer two = await environment.CreatePlayerAsync(online: false);
                Guid guildId = await environment.CreateGuildWithAsync(leader, "삭" + (char)('가' + round), one, two);
                await environment.ExecuteAsync(
                    """
                    INSERT INTO friendships (character_id, friend_character_id) VALUES (@A, @B), (@B, @A);
                    INSERT INTO friend_requests (requester_character_id, target_character_id, expires_at)
                    VALUES (@A, @L, clock_timestamp() + interval '1 day'), (@L, @B, clock_timestamp() + interval '1 day');
                    """, new { A = one.CharacterId, B = two.CharacterId, L = leader.CharacterId });

                OperationResult[] results = await Task.WhenAll(
                    Task.Run(() => Shinshi.Backend.Tests.Accounts.CharacterDeleteTestSupport.DeleteAfterGraceAsync(
                        environment.DataSource, characters, one.AccountId, one.CharacterId, 0)),
                    Task.Run(() => Shinshi.Backend.Tests.Accounts.CharacterDeleteTestSupport.DeleteAfterGraceAsync(
                        environment.DataSource, characters, two.AccountId, two.CharacterId, 0)));

                Assert.All(results, result => Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status));
                Assert.Equal(1L, await environment.ScalarAsync("SELECT member_count FROM guilds WHERE guild_id = @Id;",
                    new { Id = guildId }));
                Assert.Equal(0L, await environment.ScalarAsync(
                    "SELECT count(*) FROM friendships WHERE character_id = ANY(@Ids) OR friend_character_id = ANY(@Ids);",
                    new { Ids = new[] { one.CharacterId, two.CharacterId } }));
                Assert.Equal(0L, await environment.ScalarAsync(
                    "SELECT count(*) FROM friend_requests WHERE requester_character_id = ANY(@Ids) OR target_character_id = ANY(@Ids);",
                    new { Ids = new[] { one.CharacterId, two.CharacterId } }));
                // 남은 길드장(접속 중)에게 탈퇴 알림 두 줄
                Assert.Equal(2L, await environment.ScalarAsync(
                    "SELECT count(*) FROM social_deliveries WHERE target_character_id = @Id AND channel = @Left AND kind = 2;",
                    new { Id = leader.CharacterId, Left = SocialCodes.Notice.GUILD_MEMBER_LEFT }));
            }
            await environment.AssertGuildInvariantsAsync();
        }

        [PostgresFact]
        public async Task DeleteCharacter_LeaderRules_AndSocialActionAfterDeletion()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            PostgresCharacterRepository characters = new PostgresCharacterRepository(environment.DataSource,
                new IdempotentOperationExecutor(new PostgresOperationStore(environment.DataSource)));
            SocialPlayer leader = await environment.CreatePlayerAsync();
            SocialPlayer member = await environment.CreatePlayerAsync(online: false);
            SocialPlayer lonely = await environment.CreatePlayerAsync();
            SocialPlayer friend = await environment.CreatePlayerAsync();
            Guid guildId = await environment.CreateGuildWithAsync(leader, "장삭", member);
            Guid lonelyGuild = await environment.CreateGuildWithAsync(lonely, "혼자");
            // 접속 중인 캐릭터는 삭제할 수 없어 길드를 만든 뒤 접속을 끊는다
            await environment.DisconnectAsync(leader);
            await environment.DisconnectAsync(lonely);
            await environment.ExecuteAsync(
                """
                INSERT INTO guild_join_requests (guild_id, character_id, kind, inviter_character_id, expires_at)
                VALUES (@Guild, @Friend, 1, @Lonely, clock_timestamp() + interval '1 day');
                INSERT INTO social_deliveries (target_character_id, kind, channel, sender_name, payload, expires_at)
                VALUES (@Lonely, 0, 4, 'x', 'hello', clock_timestamp() + interval '1 minute');
                """, new { Guild = lonelyGuild, Friend = friend.CharacterId, Lonely = lonely.CharacterId });

            OperationResult rejected = await Shinshi.Backend.Tests.Accounts.CharacterDeleteTestSupport.DeleteAfterGraceAsync(
                environment.DataSource, characters, leader.AccountId, leader.CharacterId, 0);
            OperationResult disbanded = await Shinshi.Backend.Tests.Accounts.CharacterDeleteTestSupport.DeleteAfterGraceAsync(
                environment.DataSource, characters, lonely.AccountId, lonely.CharacterId, 0);

            Assert.Equal((ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.CHARACTER_IS_GUILD_LEADER),
                (rejected.Status, rejected.ReasonCode));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, disbanded.Status);
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM guilds WHERE guild_id = @Id AND disbanded_at IS NOT NULL;", new { Id = lonelyGuild }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM social_deliveries WHERE target_character_id = @Id;", new { Id = lonely.CharacterId }));
            // 해체로 지운 초대의 대상(접속 중)에게 해체 알림
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM social_deliveries WHERE target_character_id = @Id AND channel = @Disbanded;",
                new { Id = friend.CharacterId, Disbanded = SocialCodes.Notice.GUILD_DISBANDED }));
            Assert.Equal(2L, await environment.ScalarAsync("SELECT member_count FROM guilds WHERE guild_id = @Id;",
                new { Id = guildId }));
            // 삭제된 캐릭터에게는 관계를 만들 수 없다
            SocialTestEnvironment.AssertRejected(await environment.Social.RequestFriendAsync(environment.Town,
                friend.CharacterId, lonely.Name.ToLowerInvariant()), ContractCodes.RejectReason.SOCIAL_TARGET_NOT_FOUND);
            await environment.AssertGuildInvariantsAsync();
        }
    }
}
