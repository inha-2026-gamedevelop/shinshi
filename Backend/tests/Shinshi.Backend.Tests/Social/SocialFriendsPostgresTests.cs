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
    /// <summary> 친구 요청, 반대 요청 즉시 성립, 수락, 거절, 취소, 삭제, 상한, 만료, 접속 판정을 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class SocialFriendsPostgresTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Request_ThenAccept_MakesBothRowsAndNotifiesRequester()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync("Alice");
            SocialPlayer bob = await environment.CreatePlayerAsync("Bob");

            // 대소문자, 전각은 정규화된 이름 키로 찾는다(API가 CharacterNameRules로 만든 키)
            SocialActionOutcome requested = await environment.Social.RequestFriendAsync(environment.Town, alice.CharacterId, "bob");
            SocialActionOutcome again = await environment.Social.RequestFriendAsync(environment.Town, alice.CharacterId, "bob");
            SocialActionOutcome accepted = await environment.Social.AcceptFriendAsync(environment.Town, bob.CharacterId,
                alice.CharacterId);
            SocialActionOutcome replay = await environment.Social.AcceptFriendAsync(environment.Town, bob.CharacterId,
                alice.CharacterId);

            SocialTestEnvironment.AssertCommitted(requested);
            Assert.Equal(new[] { bob.CharacterId }, requested.ChangedCharacterIds);
            // 같은 요청의 재전송은 기한만 늘리고 알림을 다시 넣지 않는다
            SocialTestEnvironment.AssertCommitted(again);
            Assert.Empty(again.ChangedCharacterIds);
            SocialTestEnvironment.AssertCommitted(accepted);
            Assert.Equal(new[] { alice.CharacterId }, accepted.ChangedCharacterIds);
            SocialTestEnvironment.AssertCommitted(replay);
            Assert.Equal(2L, await environment.ScalarAsync("SELECT count(*) FROM friendships;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM friend_requests;"));
            Assert.Equal(new[] { SocialCodes.Notice.FRIEND_REQUESTED, SocialCodes.Notice.FRIEND_ACCEPTED },
                (await environment.QueryAsync<int>(
                    "SELECT channel FROM social_deliveries WHERE kind = 2 ORDER BY delivery_id;")).ToArray());

            SocialSnapshotRecord snapshot = (await environment.Social.ReadSnapshotAsync(environment.Town, alice.CharacterId))!;
            SocialPersonRecord friend = Assert.Single(snapshot.Friends);
            Assert.Equal((bob.CharacterId, "Bob", SocialCodes.Presence.TOWN, true),
                (friend.CharacterId, friend.Name, friend.Presence.Presence, friend.Presence.SameServer));
        }

        [PostgresFact]
        public async Task Request_ReverseExists_BecomesFriendsImmediately()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync();

            SocialTestEnvironment.AssertCommitted(await environment.Social.RequestFriendAsync(environment.Town,
                alice.CharacterId, bob.Name.ToLowerInvariant()));
            SocialActionOutcome reverse = await environment.Social.RequestFriendAsync(environment.Town, bob.CharacterId,
                alice.Name.ToLowerInvariant());
            SocialActionOutcome already = await environment.Social.RequestFriendAsync(environment.Town, bob.CharacterId,
                alice.Name.ToLowerInvariant());

            SocialTestEnvironment.AssertCommitted(reverse);
            SocialTestEnvironment.AssertRejected(already, ContractCodes.RejectReason.FRIEND_ALREADY);
            Assert.Equal(2L, await environment.ScalarAsync("SELECT count(*) FROM friendships;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM friend_requests;"));
        }

        [PostgresFact]
        public async Task DeclineCancelRemove_AreIdempotent()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync();
            SocialPlayer carol = await environment.CreatePlayerAsync();
            await environment.Social.RequestFriendAsync(environment.Town, alice.CharacterId, bob.Name.ToLowerInvariant());
            await environment.Social.RequestFriendAsync(environment.Town, alice.CharacterId, carol.Name.ToLowerInvariant());

            for (int attempt = 0; attempt < 2; ++attempt)
            {
                SocialTestEnvironment.AssertCommitted(await environment.Social.DeclineFriendAsync(environment.Town,
                    bob.CharacterId, alice.CharacterId));
                SocialTestEnvironment.AssertCommitted(await environment.Social.CancelFriendRequestAsync(environment.Town,
                    alice.CharacterId, carol.CharacterId));
            }
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM friend_requests;"));

            await environment.Social.RequestFriendAsync(environment.Town, alice.CharacterId, bob.Name.ToLowerInvariant());
            await environment.Social.AcceptFriendAsync(environment.Town, bob.CharacterId, alice.CharacterId);
            for (int attempt = 0; attempt < 2; ++attempt)
            {
                SocialTestEnvironment.AssertCommitted(await environment.Social.RemoveFriendAsync(environment.Town,
                    bob.CharacterId, alice.CharacterId));
            }
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM friendships;"));
        }

        [PostgresFact]
        public async Task Request_TargetMissingSelfOrActorElsewhere_Rejected()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync(instance: environment.OtherTownId);

            SocialTestEnvironment.AssertRejected(await environment.Social.RequestFriendAsync(environment.Town,
                alice.CharacterId, "nobody"), ContractCodes.RejectReason.SOCIAL_TARGET_NOT_FOUND);
            SocialTestEnvironment.AssertRejected(await environment.Social.RequestFriendAsync(environment.Town,
                alice.CharacterId, null), ContractCodes.RejectReason.SOCIAL_TARGET_NOT_FOUND);
            SocialTestEnvironment.AssertRejected(await environment.Social.RequestFriendAsync(environment.Town,
                alice.CharacterId, alice.Name.ToLowerInvariant()), ContractCodes.RejectReason.SOCIAL_TARGET_SELF);
            // 다른 마을 서버에 접속한 캐릭터를 이 서버가 대신 움직일 수 없다
            SocialTestEnvironment.AssertRejected(await environment.Social.RequestFriendAsync(environment.Town,
                bob.CharacterId, alice.Name.ToLowerInvariant()), ContractCodes.RejectReason.SOCIAL_NOT_CONNECTED);
            Assert.Null(await environment.Social.ReadSnapshotAsync(environment.Town, bob.CharacterId));
            // 대상이 오프라인이어도 요청은 남는다(알림 행만 없다)
            SocialPlayer offline = await environment.CreatePlayerAsync(online: false);
            SocialActionOutcome toOffline = await environment.Social.RequestFriendAsync(environment.Town, alice.CharacterId,
                offline.Name.ToLowerInvariant());
            SocialTestEnvironment.AssertCommitted(toOffline);
            Assert.Empty(toOffline.ChangedCharacterIds);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM social_deliveries;"));
        }

        [PostgresFact]
        public async Task Limits_OutgoingIncomingAndFriendCount_AreEnforced()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer[] targets = new SocialPlayer[SocialLimits.MAX_OUTGOING_FRIEND_REQUESTS + 1];
            for (int index = 0; index < targets.Length; ++index)
            {
                targets[index] = await environment.CreatePlayerAsync(online: false);
            }

            for (int index = 0; index < SocialLimits.MAX_OUTGOING_FRIEND_REQUESTS; ++index)
            {
                SocialTestEnvironment.AssertCommitted(await environment.Social.RequestFriendAsync(environment.Town,
                    alice.CharacterId, targets[index].Name.ToLowerInvariant()));
            }
            SocialTestEnvironment.AssertRejected(await environment.Social.RequestFriendAsync(environment.Town,
                alice.CharacterId, targets[^1].Name.ToLowerInvariant()), ContractCodes.RejectReason.FRIEND_REQUEST_LIMIT);

            // 받은 요청 50건이 찬 대상
            SocialPlayer popular = await environment.CreatePlayerAsync();
            SocialPlayer sender = await environment.CreatePlayerAsync();
            await SeedIncomingAsync(environment, popular, SocialLimits.MAX_INCOMING_FRIEND_REQUESTS);
            SocialTestEnvironment.AssertRejected(await environment.Social.RequestFriendAsync(environment.Town,
                sender.CharacterId, popular.Name.ToLowerInvariant()), ContractCodes.RejectReason.FRIEND_TARGET_REQUESTS_FULL);

            // 친구 50명인 쪽이 수락하거나(내 상한), 상대가 50명이면(상대 상한) 거절
            SocialPlayer full = await environment.CreatePlayerAsync();
            SocialPlayer asker = await environment.CreatePlayerAsync();
            await SeedFriendsAsync(environment, full, SocialLimits.MAX_FRIENDS);
            await environment.Social.RequestFriendAsync(environment.Town, asker.CharacterId, full.Name.ToLowerInvariant());
            SocialTestEnvironment.AssertRejected(await environment.Social.AcceptFriendAsync(environment.Town,
                full.CharacterId, asker.CharacterId), ContractCodes.RejectReason.FRIEND_LIMIT);
            SocialTestEnvironment.AssertRejected(await environment.Social.RequestFriendAsync(environment.Town,
                full.CharacterId, asker.Name.ToLowerInvariant()), ContractCodes.RejectReason.FRIEND_LIMIT);
            await environment.ExecuteAsync(
                "INSERT INTO friend_requests (requester_character_id, target_character_id, expires_at) VALUES (@Full, @Asker, clock_timestamp() + interval '1 day');",
                new { Full = full.CharacterId, Asker = asker.CharacterId });
            SocialTestEnvironment.AssertRejected(await environment.Social.AcceptFriendAsync(environment.Town,
                asker.CharacterId, full.CharacterId), ContractCodes.RejectReason.FRIEND_TARGET_LIMIT);
            Assert.Equal((long)SocialLimits.MAX_FRIENDS, await environment.ScalarAsync(
                "SELECT count(*) FROM friendships WHERE character_id = @Id;", new { Id = full.CharacterId }));
        }

        [PostgresFact]
        public async Task ExpiredRequest_IsIgnored()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync();
            await environment.Social.RequestFriendAsync(environment.Town, alice.CharacterId, bob.Name.ToLowerInvariant());
            await ExpireAsync(environment, "friend_requests");

            SocialTestEnvironment.AssertRejected(await environment.Social.AcceptFriendAsync(environment.Town, bob.CharacterId,
                alice.CharacterId), ContractCodes.RejectReason.FRIEND_REQUEST_NOT_FOUND);
            Assert.Empty((await environment.Social.ReadSnapshotAsync(environment.Town, bob.CharacterId))!.Incoming);
            // 만료된 반대 요청은 즉시 성립시키지 않고 새 요청이 된다
            SocialActionOutcome fresh = await environment.Social.RequestFriendAsync(environment.Town, bob.CharacterId,
                alice.Name.ToLowerInvariant());
            SocialTestEnvironment.AssertCommitted(fresh);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM friendships;"));
            SocialPurgeResult purged = await environment.Social.PurgeExpiredAsync(100);
            Assert.Equal(1, purged.FriendRequests);
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM friend_requests;"));
        }

        [PostgresFact]
        public async Task Presence_FollowsLeaseInstanceHeartbeatAndExpiry()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync();
            await environment.Social.RequestFriendAsync(environment.Town, alice.CharacterId, bob.Name.ToLowerInvariant());
            await environment.Social.AcceptFriendAsync(environment.Town, bob.CharacterId, alice.CharacterId);

            async Task<SocialPresence> PresenceAsync()
            {
                SocialSnapshotRecord snapshot = (await environment.Social.ReadSnapshotAsync(environment.Town,
                    alice.CharacterId))!;
                return Assert.Single(snapshot.Friends).Presence;
            }

            Assert.Equal(new SocialPresence(SocialCodes.Presence.TOWN, true), await PresenceAsync());
            await environment.ConnectAsync(bob, environment.OtherTownId);
            Assert.Equal(new SocialPresence(SocialCodes.Presence.TOWN, false), await PresenceAsync());

            // 던전 이관(UPDATE)은 character_id를 그대로 둔다
            Guid leaseId = await environment.SingleAsync<Guid>("SELECT lease_id FROM account_leases WHERE account_id = @AccountId;",
                new { bob.AccountId });
            Assert.Equal(Shinshi.Data.EAccountLeaseStatus.Transferred, (await environment.Leases.TransferAsync(bob.AccountId,
                leaseId, environment.OtherTownId, Guid.NewGuid(), environment.DungeonId, ContractCodes.ServerRole.DUNGEON,
                TimeSpan.FromMinutes(5))).Status);
            Assert.Equal(bob.CharacterId, await environment.SingleAsync<Guid>(
                "SELECT character_id FROM account_leases WHERE account_id = @AccountId;", new { bob.AccountId }));
            Assert.Equal(new SocialPresence(SocialCodes.Presence.DUNGEON, false), await PresenceAsync());

            // heartbeat가 15초 넘게 끊긴 서버의 임대는 오프라인
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '1 minute' WHERE instance_id = @Id;",
                new { Id = environment.DungeonId });
            Assert.Equal(new SocialPresence(SocialCodes.Presence.OFFLINE, false), await PresenceAsync());
            await environment.ExecuteAsync("UPDATE server_instances SET heartbeat_at = clock_timestamp() WHERE instance_id = @Id;",
                new { Id = environment.DungeonId });
            Assert.Equal(SocialCodes.Presence.DUNGEON, (await PresenceAsync()).Presence);

            // 만료된 임대도 오프라인
            await environment.ExecuteAsync(
                "UPDATE account_leases SET expires_at = clock_timestamp() - interval '1 second' WHERE account_id = @AccountId;",
                new { bob.AccountId });
            Assert.Equal(SocialCodes.Presence.OFFLINE, (await PresenceAsync()).Presence);

            // 캐릭터 없이 얻은 임대(TryAcquire)는 character_id를 비운다
            await environment.ExecuteAsync("UPDATE account_leases SET expires_at = clock_timestamp() + interval '1 minute', character_id = @CharacterId WHERE account_id = @AccountId;",
                new { bob.AccountId, bob.CharacterId });
            await environment.ExecuteAsync("UPDATE account_leases SET expires_at = clock_timestamp() - interval '1 second' WHERE account_id = @AccountId;",
                new { bob.AccountId });
            AccountLeaseOutcomeAssert(await environment.Leases.TryAcquireAsync(bob.AccountId, Guid.NewGuid(), environment.TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(5)));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE account_id = @AccountId AND character_id IS NOT NULL;",
                new { bob.AccountId }));
            Assert.Equal(SocialCodes.Presence.OFFLINE, (await PresenceAsync()).Presence);
        }

        private static void AccountLeaseOutcomeAssert(Shinshi.Data.AccountLeaseOutcome outcome)
        {
            Assert.Equal(Shinshi.Data.EAccountLeaseStatus.Acquired, outcome.Status);
        }

        private static async Task SeedIncomingAsync(SocialTestEnvironment environment, SocialPlayer target, int count)
        {
            for (int index = 0; index < count; ++index)
            {
                SocialPlayer requester = await environment.CreatePlayerAsync(online: false);
                await environment.ExecuteAsync(
                    "INSERT INTO friend_requests (requester_character_id, target_character_id, expires_at) VALUES (@Requester, @Target, clock_timestamp() + interval '1 day');",
                    new { Requester = requester.CharacterId, Target = target.CharacterId });
            }
        }

        private static async Task SeedFriendsAsync(SocialTestEnvironment environment, SocialPlayer owner, int count)
        {
            for (int index = 0; index < count; ++index)
            {
                SocialPlayer friend = await environment.CreatePlayerAsync(online: false);
                await environment.ExecuteAsync(
                    "INSERT INTO friendships (character_id, friend_character_id) VALUES (@Owner, @Friend), (@Friend, @Owner);",
                    new { Owner = owner.CharacterId, Friend = friend.CharacterId });
            }
        }

        internal static Task ExpireAsync(SocialTestEnvironment environment, string table)
        {
            return environment.ExecuteAsync("UPDATE " + table
                + " SET created_at = clock_timestamp() - interval '9 days', expires_at = clock_timestamp() - interval '1 second';");
        }
    }
}
