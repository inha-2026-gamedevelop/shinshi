// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Growth;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Settlement
{
    /// <summary> 성공 정산이 계정 진행(지역 성공, 보스 첫 처치, 여우의 흔적)을 한 번만 기록하는지 실제 PostgreSQL에서 검사한다.
    /// 실패 종료와 떠난 참가자는 기록하지 않는다 </summary>
    public sealed class SettlementProgressTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   TICKETS       = 3;
        private const long   POTIONS       = 3;
        private const long   ESSENCE       = 10;
        private const string REGION_ALTAR  = "region_altar";
        private const string REGION_FOREST = "region_forest";
        private const string BATTLE_EASY   = "battle_wolbaek_easy";
        private const string UNKNOWN_MAP   = "battle_test_map";

        // 정산한 던전은 풀로 돌아가 다시 배정되기까지 시간이 걸리므로 출정마다 던전을 하나씩 둔다
        private const int NORMAL_DUNGEONS = 6;
        private const int MIXED_DUNGEONS  = 3;

        private static readonly GrowthContent G_Content = GrowthContent.LoadEmbedded();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 일반전 성공은 지역 성공과 그 지역의 흔적을 남기고, 같은 정산 재전송은 다시 쌓지 않는다. 흔적은 상한에서 멈춘다 </summary>
        [PostgresFact]
        public async Task NormalSuccess_RecordsRegionClearAndFoxTracesOncePerSettlement()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(NORMAL_DUNGEONS);
            PostgresSettlementRepository settlement = Create(environment);
            TestPlayer first = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer second = await environment.CreatePlayerAsync(TICKETS, POTIONS);

            (ReservedSessionView session, Guid settlementId) = await EndAsync(environment, new[] { first, second },
                GameSessionCodes.Mode.NORMAL, REGION_ALTAR, GameSessionCodes.EndReason.SUCCESS);
            OperationResult settled = await settlement.SettleAsync(session.InstanceId, session.SessionId, settlementId);
            OperationResult resent = await settlement.SettleAsync(session.InstanceId, session.SessionId, settlementId);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);
            Assert.Equal(settled.ReceiptId, resent.ReceiptId);
            Assert.Equal(2L, await CountUnlocksAsync(environment, EAccountProgressKind.RegionCleared, REGION_ALTAR));
            Assert.Equal(1L, await FoxAsync(environment, first));
            Assert.Equal(1L, await FoxAsync(environment, second));

            // 같은 지역을 상한보다 많이 성공해도 흔적은 상한(foxTraceCap)에서 멈추고 성공 기록은 하나다
            int cap = G_Content.Progression.FoxTraceCap;
            for (int clear = 1; clear <= cap; ++clear)
            {
                (ReservedSessionView again, Guid againId) = await EndAsync(environment, new[] { first },
                    GameSessionCodes.Mode.NORMAL, REGION_ALTAR, GameSessionCodes.EndReason.SUCCESS);
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                    (await settlement.SettleAsync(again.InstanceId, again.SessionId, againId)).Status);
            }

            Assert.Equal((long)cap, await FoxAsync(environment, first));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_unlocks WHERE account_id = @Id;", new { Id = first.AccountId }));
        }

        /// <summary> 보스 성공은 bossId와 난이도로 찾은 보스전 첫 처치를 남긴다 </summary>
        [PostgresFact]
        public async Task BossSuccess_RecordsBattleClearFromBossAndDifficulty()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            PostgresSettlementRepository settlement = Create(environment);
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);

            (ReservedSessionView session, Guid settlementId) = await EndAsync(environment, new[] { player },
                GameSessionCodes.Mode.BOSS, BATTLE_EASY, GameSessionCodes.EndReason.SUCCESS);
            OperationResult settled = await settlement.SettleAsync(session.InstanceId, session.SessionId, settlementId);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);
            Assert.Equal(1L, await CountUnlocksAsync(environment, EAccountProgressKind.BattleCleared, BATTLE_EASY));
            Assert.Equal(0L, await FoxAsync(environment, player));
        }

        /// <summary> 실패 종료, 콘텐츠에 없는 지도, 떠난 참가자는 진행을 남기지 않는다 </summary>
        [PostgresFact]
        public async Task FailureUnknownMapAndDeparted_RecordNothing()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(MIXED_DUNGEONS);
            PostgresSettlementRepository settlement = Create(environment);
            TestPlayer stayed = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer departed = await environment.CreatePlayerAsync(TICKETS, POTIONS);

            (ReservedSessionView failed, Guid failedId) = await EndAsync(environment, new[] { stayed },
                GameSessionCodes.Mode.NORMAL, REGION_FOREST, GameSessionCodes.EndReason.ALL_DEAD);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await settlement.SettleAsync(failed.InstanceId, failed.SessionId, failedId)).Status);
            (ReservedSessionView unknown, Guid unknownId) = await EndAsync(environment, new[] { stayed },
                GameSessionCodes.Mode.NORMAL, UNKNOWN_MAP, GameSessionCodes.EndReason.SUCCESS);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await settlement.SettleAsync(unknown.InstanceId, unknown.SessionId, unknownId)).Status);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM account_unlocks;"));

            (ReservedSessionView party, Guid partyId) = await EndAsync(environment, new[] { stayed, departed },
                GameSessionCodes.Mode.NORMAL, REGION_FOREST, GameSessionCodes.EndReason.SUCCESS, departed.AccountId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await settlement.SettleAsync(party.InstanceId, party.SessionId, partyId)).Status);

            Assert.Equal(1L, await CountUnlocksAsync(environment, EAccountProgressKind.RegionCleared, REGION_FOREST));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_unlocks WHERE account_id = @Id;", new { Id = departed.AccountId }));
        }

        private static PostgresSettlementRepository Create(SessionTestEnvironment environment)
        {
            return new PostgresSettlementRepository(environment.DataSource, environment.Executor, environment.Ledger,
                new PostgresAccountProgressRepository(environment.DataSource, G_Content, EGrowthContentProfile.Standard));
        }

        // 출정을 시작하고 출정 계약(battle_id)을 맞춘 뒤 종료 이벤트를 저장한다. departedAccount가 있으면 그 참가자를 이탈로 표시한다
        private static async Task<(ReservedSessionView Session, Guid SettlementId)> EndAsync(
            SessionTestEnvironment environment, IReadOnlyList<TestPlayer> players, int mode, string battleId, int endReason,
            Guid? departedAccount = null)
        {
            ReservedSessionView session = await environment.StartAsync(mode, players, 0);
            await environment.ExecuteAsync("UPDATE game_sessions SET battle_id = @BattleId WHERE session_id = @Id;",
                new { BattleId = battleId, Id = session.SessionId });
            if (departedAccount.HasValue)
            {
                await environment.ExecuteAsync(
                    """
                    UPDATE session_participants SET departed_at = clock_timestamp(), depart_reason = @Reason
                    WHERE session_id = @Id AND account_id = @AccountId;
                    """, new
                    {
                        Reason = GameSessionCodes.EndReason.ABANDON, Id = session.SessionId, AccountId = departedAccount.Value
                    });
            }

            Guid settlementId = Guid.NewGuid();
            SessionEventInput ended = SessionEventTests.Ended(1, settlementId,
                session.Participants.Select(row => SessionEventTests.Grant(row.ParticipantId, ESSENCE)));
            ended.EndReason = endReason;
            OperationResult appended = await SessionEventTests.AppendAsync(environment, session, new[] { ended });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, appended.Status);
            return (session, settlementId);
        }

        private static Task<long> CountUnlocksAsync(SessionTestEnvironment environment, EAccountProgressKind kind,
            string targetId)
        {
            return environment.ScalarAsync(
                "SELECT count(*) FROM account_unlocks WHERE unlock_kind = @Kind AND target_id = @Target;",
                new { Kind = kind.ToString(), Target = targetId });
        }

        private static Task<long> FoxAsync(SessionTestEnvironment environment, TestPlayer player)
        {
            return environment.ScalarAsync(
                "SELECT COALESCE((SELECT fox_traces FROM account_progress WHERE account_id = @Id), 0)::bigint;",
                new { Id = player.AccountId });
        }
    }
}
