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
    /// <summary> 귓속말, 길드 채팅, 전달함 꺼내기와 수신 확인. 모든 줄이 행으로 먼저 남고 확인받은 행만 지워지며,
    /// 로컬 전달이 앞선 남은 행을 오름차순으로 포함하고, 꺼내기가 수신자별로 공정한지 검사한다 </summary>
    public sealed class SocialDeliveriesPostgresTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Whisper_SameInstance_StoresRowReturnsLocalAndDeletesOnlyAfterAck()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync("BobKim");

            SocialChatOutcome sent = await environment.Social.WhisperAsync(environment.Town, alice.CharacterId, "bobkim", "안녕");

            Assert.Equal(ContractCodes.RejectReason.NONE, sent.ReasonCode);
            Assert.Equal("BobKim", sent.Name);
            SocialDeliveryRecord local = Assert.Single(sent.LocalDeliveries);
            Assert.Equal((bob.CharacterId, SocialCodes.DeliveryKind.WHISPER, SocialCodes.ChatChannel.WHISPER,
                    (Guid?)alice.CharacterId, alice.Name, "안녕"),
                (local.TargetCharacterId, local.Kind, local.Channel, local.SenderCharacterId, local.SenderName, local.Payload));
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM social_deliveries;"));

            // 로컬로 보낸 행은 재전송 간격 동안 꺼내기에서 빠지고, 간격이 지나면 다시 나온다(확인 전)
            Assert.Empty((await environment.Social.PullAsync(environment.Town, Array.Empty<SocialDeliveryAckInput>())).Deliveries);
            await DueAsync(environment);
            Assert.Equal(local.DeliveryId,
                Assert.Single((await environment.Social.PullAsync(environment.Town, Array.Empty<SocialDeliveryAckInput>()))
                    .Deliveries).DeliveryId);

            // 수신 캐릭터가 다르거나 다른 인스턴스의 확인은 지우지 못한다
            await DueAsync(environment);
            SocialPullOutcome wrongRecipient = await environment.Social.PullAsync(environment.Town,
                new[] { new SocialDeliveryAckInput(local.DeliveryId, alice.CharacterId) });
            SocialPullOutcome otherInstance = await environment.Social.PullAsync(environment.OtherTown,
                new[] { new SocialDeliveryAckInput(local.DeliveryId, bob.CharacterId) });
            Assert.Equal(0, wrongRecipient.Acknowledged);
            Assert.Equal(0, otherInstance.Acknowledged);
            Assert.Empty(otherInstance.Deliveries);
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM social_deliveries;"));

            SocialPullOutcome acked = await environment.Social.PullAsync(environment.Town,
                new[] { new SocialDeliveryAckInput(local.DeliveryId, bob.CharacterId) });
            Assert.Equal(1, acked.Acknowledged);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM social_deliveries;"));
        }

        [PostgresFact]
        public async Task Whisper_RejectsMissingSelfOfflineAndActorElsewhere()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer offline = await environment.CreatePlayerAsync(online: false);
            SocialPlayer stale = await environment.CreatePlayerAsync(instance: environment.OtherTownId);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '1 minute' WHERE instance_id = @Id;",
                new { Id = environment.OtherTownId });

            Assert.Equal(ContractCodes.RejectReason.SOCIAL_TARGET_NOT_FOUND,
                (await environment.Social.WhisperAsync(environment.Town, alice.CharacterId, "nobody", "x")).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SOCIAL_TARGET_NOT_FOUND,
                (await environment.Social.WhisperAsync(environment.Town, alice.CharacterId, null, "x")).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SOCIAL_TARGET_SELF, (await environment.Social.WhisperAsync(environment.Town,
                alice.CharacterId, alice.Name.ToLowerInvariant(), "x")).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.WHISPER_TARGET_OFFLINE, (await environment.Social.WhisperAsync(
                environment.Town, alice.CharacterId, offline.Name.ToLowerInvariant(), "x")).ReasonCode);
            // heartbeat가 끊긴 서버에 남은 임대는 접속이 아니다
            Assert.Equal(ContractCodes.RejectReason.WHISPER_TARGET_OFFLINE, (await environment.Social.WhisperAsync(
                environment.Town, alice.CharacterId, stale.Name.ToLowerInvariant(), "x")).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SOCIAL_NOT_CONNECTED, (await environment.Social.WhisperAsync(
                environment.Dungeon, alice.CharacterId, offline.Name.ToLowerInvariant(), "x")).ReasonCode);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM social_deliveries;"));
        }

        [PostgresFact]
        public async Task Whisper_OtherServer_NoLocalRowAndTargetServerPulls_AfterMoveNewServerPulls()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync(instance: environment.DungeonId);

            SocialChatOutcome sent = await environment.Social.WhisperAsync(environment.Town, alice.CharacterId,
                bob.Name.ToLowerInvariant(), "던전에게");
            Assert.Empty(sent.LocalDeliveries);
            // 마을 꺼내기는 던전 대상 행을 돌려주지 않는다
            Assert.Empty((await environment.Social.PullAsync(environment.Town, Array.Empty<SocialDeliveryAckInput>())).Deliveries);
            SocialDeliveryRecord pulled = Assert.Single((await environment.Social.PullAsync(environment.Dungeon,
                Array.Empty<SocialDeliveryAckInput>())).Deliveries);
            Assert.Equal("던전에게", pulled.Payload);

            // 확인 없이 대상이 마을로 돌아오면 새 서버가 재전송 간격 뒤 같은 행을 꺼낸다(클라이언트가 ID로 중복을 버린다)
            await environment.ConnectAsync(bob, environment.TownId);
            Assert.Empty((await environment.Social.PullAsync(environment.Town, Array.Empty<SocialDeliveryAckInput>())).Deliveries);
            await DueAsync(environment);
            Assert.Equal(pulled.DeliveryId, Assert.Single((await environment.Social.PullAsync(environment.Town,
                Array.Empty<SocialDeliveryAckInput>())).Deliveries).DeliveryId);
            // 옛 서버(던전)의 확인은 더 이상 지우지 못한다
            Assert.Equal(0, (await environment.Social.PullAsync(environment.Dungeon,
                new[] { new SocialDeliveryAckInput(pulled.DeliveryId, bob.CharacterId) })).Acknowledged);
        }

        [PostgresFact]
        public async Task LocalDelivery_IncludesEarlierRemainingRowsAscending()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer carol = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync(instance: environment.DungeonId);

            // 던전에 있을 때 받은 m1(확인 없음), 마을로 돌아온 뒤 같은 마을의 m2
            SocialChatOutcome first = await environment.Social.WhisperAsync(environment.Town, carol.CharacterId,
                bob.Name.ToLowerInvariant(), "m1");
            Assert.Empty(first.LocalDeliveries);
            await environment.ConnectAsync(bob, environment.TownId);
            SocialChatOutcome second = await environment.Social.WhisperAsync(environment.Town, alice.CharacterId,
                bob.Name.ToLowerInvariant(), "m2");

            Assert.Equal(new[] { "m1", "m2" }, second.LocalDeliveries.Select(row => row.Payload).ToArray());
            Assert.True(second.LocalDeliveries[0].DeliveryId < second.LocalDeliveries[1].DeliveryId);
            Assert.Equal(carol.Name, second.LocalDeliveries[0].SenderName);
        }

        [PostgresFact]
        public async Task LocalDelivery_RecipientOverLimit_LeftToPull()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync();
            await SeedRowsAsync(environment, bob, SocialLimits.MAX_DELIVERIES_PER_RECIPIENT);

            SocialChatOutcome sent = await environment.Social.WhisperAsync(environment.Town, alice.CharacterId,
                bob.Name.ToLowerInvariant(), "over");

            Assert.Equal(ContractCodes.RejectReason.NONE, sent.ReasonCode);
            Assert.Empty(sent.LocalDeliveries);
            Assert.Equal((long)SocialLimits.MAX_DELIVERIES_PER_RECIPIENT + 1,
                await environment.ScalarAsync("SELECT count(*) FROM social_deliveries;"));
        }

        [PostgresFact]
        public async Task Pull_OneRecipientBacklog_DoesNotBlockOthers()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer flooded = await environment.CreatePlayerAsync();
            SocialPlayer quiet = await environment.CreatePlayerAsync();
            await SeedRowsAsync(environment, flooded, SocialLimits.MAX_DELIVERIES_PER_PULL);
            await SeedRowsAsync(environment, quiet, 1);

            SocialPullOutcome first = await environment.Social.PullAsync(environment.Town, Array.Empty<SocialDeliveryAckInput>());

            Assert.Equal(SocialLimits.MAX_DELIVERIES_PER_RECIPIENT,
                first.Deliveries.Count(row => row.TargetCharacterId == flooded.CharacterId));
            Assert.Single(first.Deliveries, row => row.TargetCharacterId == quiet.CharacterId);
            Assert.True(first.Deliveries.Select(row => row.DeliveryId).SequenceEqual(
                first.Deliveries.Select(row => row.DeliveryId).OrderBy(id => id)));
            // 꺼낸 행은 재전송 간격 동안 빠지므로 다음 꺼내기는 그다음 20개다
            SocialPullOutcome second = await environment.Social.PullAsync(environment.Town, Array.Empty<SocialDeliveryAckInput>());
            Assert.Equal(SocialLimits.MAX_DELIVERIES_PER_RECIPIENT, second.Deliveries.Count);
            Assert.Empty(second.Deliveries.Select(row => row.DeliveryId).Intersect(first.Deliveries.Select(row => row.DeliveryId)));
            Assert.True(second.Deliveries.Min(row => row.DeliveryId) > first.Deliveries
                .Where(row => row.TargetCharacterId == flooded.CharacterId).Max(row => row.DeliveryId));
        }

        [PostgresFact]
        public async Task GuildChat_RowsForOnlineMembersAndLocalOnlyOnCaller_NonMemberRejected()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer leader = await environment.CreatePlayerAsync();
            SocialPlayer townMate = await environment.CreatePlayerAsync();
            SocialPlayer dungeonMate = await environment.CreatePlayerAsync(instance: environment.DungeonId);
            SocialPlayer offlineMate = await environment.CreatePlayerAsync(online: false);
            SocialPlayer outsider = await environment.CreatePlayerAsync();
            Guid guildId = await environment.CreateGuildWithAsync(leader, "채팅", townMate, dungeonMate, offlineMate);

            SocialChatOutcome sent = await environment.Social.GuildChatAsync(environment.Town, leader.CharacterId, "모여");

            Assert.Equal(ContractCodes.RejectReason.NONE, sent.ReasonCode);
            Assert.Equal(leader.Name, sent.Name);
            Assert.Equal(new[] { leader.CharacterId, townMate.CharacterId }.OrderBy(id => id),
                sent.LocalDeliveries.Select(row => row.TargetCharacterId).OrderBy(id => id));
            Assert.All(sent.LocalDeliveries, row => Assert.Equal((SocialCodes.DeliveryKind.GUILD_CHAT,
                SocialCodes.ChatChannel.GUILD), (row.Kind, row.Channel)));
            Assert.Equal(3L, await environment.ScalarAsync("SELECT count(*) FROM social_deliveries WHERE kind = 1;"));
            Assert.Equal(dungeonMate.CharacterId, Assert.Single((await environment.Social.PullAsync(environment.Dungeon,
                Array.Empty<SocialDeliveryAckInput>())).Deliveries).TargetCharacterId);
            Assert.Equal(ContractCodes.RejectReason.GUILD_NOT_MEMBER,
                (await environment.Social.GuildChatAsync(environment.Town, outsider.CharacterId, "x")).ReasonCode);

            // 추방된 캐릭터에게는 새 줄이 가지 않는다
            await environment.Social.KickMemberAsync(environment.Town, leader.CharacterId, townMate.CharacterId, guildId,
                await environment.MembershipOfAsync(townMate));
            SocialChatOutcome after = await environment.Social.GuildChatAsync(environment.Town, leader.CharacterId, "다시");
            Assert.DoesNotContain(after.LocalDeliveries, row => row.TargetCharacterId == townMate.CharacterId);
        }

        [PostgresFact]
        public async Task Notice_IsDeliveredAndDeletedOnlyByAck_ExpiredRowsPurged()
        {
            await using SocialTestEnvironment environment = await SocialTestEnvironment.CreateAsync();
            SocialPlayer alice = await environment.CreatePlayerAsync();
            SocialPlayer bob = await environment.CreatePlayerAsync();
            await environment.Social.RequestFriendAsync(environment.Town, alice.CharacterId, bob.Name.ToLowerInvariant());

            SocialDeliveryRecord notice = Assert.Single((await environment.Social.PullAsync(environment.Town,
                Array.Empty<SocialDeliveryAckInput>())).Deliveries);
            Assert.Equal((SocialCodes.DeliveryKind.NOTICE, SocialCodes.Notice.FRIEND_REQUESTED, alice.Name, string.Empty),
                (notice.Kind, notice.Channel, notice.SenderName, notice.Payload));
            await DueAsync(environment);
            Assert.Single((await environment.Social.PullAsync(environment.Town, Array.Empty<SocialDeliveryAckInput>())).Deliveries);
            Assert.Equal(1, (await environment.Social.PullAsync(environment.Town,
                new[] { new SocialDeliveryAckInput(notice.DeliveryId, bob.CharacterId) })).Acknowledged);

            await environment.Social.WhisperAsync(environment.Town, alice.CharacterId, bob.Name.ToLowerInvariant(), "곧 만료");
            await environment.ExecuteAsync(
                "UPDATE social_deliveries SET created_at = clock_timestamp() - interval '2 minutes', expires_at = clock_timestamp() - interval '1 second', next_attempt_at = clock_timestamp() - interval '1 minute';");
            Assert.Empty((await environment.Social.PullAsync(environment.Town, Array.Empty<SocialDeliveryAckInput>())).Deliveries);
            Assert.Equal(1, (await environment.Social.PurgeExpiredAsync(100)).Deliveries);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM social_deliveries;"));
        }

        // 재전송 간격이 지난 것처럼 모든 행을 다시 꺼낼 수 있게 한다
        private static Task DueAsync(SocialTestEnvironment environment)
        {
            return environment.ExecuteAsync("UPDATE social_deliveries SET next_attempt_at = clock_timestamp() - interval '1 second';");
        }

        private static Task SeedRowsAsync(SocialTestEnvironment environment, SocialPlayer target, int count)
        {
            return environment.ExecuteAsync(
                """
                INSERT INTO social_deliveries (target_character_id, kind, channel, sender_name, payload, expires_at)
                SELECT @Target, 0, 4, 'seed', 'line ' || n, clock_timestamp() + interval '1 minute'
                FROM generate_series(1, @Count) AS n;
                """, new { Target = target.CharacterId, Count = count });
        }
    }
}
