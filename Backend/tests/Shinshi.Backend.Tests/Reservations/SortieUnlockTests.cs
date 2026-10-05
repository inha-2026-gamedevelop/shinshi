// System
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Growth;
using Shinshi.Data.Reservations;

namespace Shinshi.Backend.Tests.Reservations
{
    /// <summary> 출정 해금 판정: 준비 확인의 계정 진행 필드와 예약의 MISSING_UNLOCK 최종 판정(내장 성장 콘텐츠) </summary>
    public sealed class SortieUnlockTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string REGION_FOREST   = "region_forest";
        private const string REGION_RUINS    = "region_ruins";
        private const string BATTLE_WOLBAEK  = "battle_wolbaek_easy";
        private const string BATTLE_CHEOLGAK = "battle_cheolgak_easy";
        private const string UNKNOWN_BATTLE  = "battle_forest_1";
        private const long   TICKETS         = 2;
        private const long   POTIONS         = 3;
        private const int    FOX_TRACES      = 2;

        private static readonly GrowthContent G_Content = GrowthContent.LoadEmbedded();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 체험판 숲은 안내 완료가 있어야 열리고, 사전 해금된 월백 이지는 기록이 없어도 열린다 </summary>
        [Fact]
        public void Check_TrialProfile_ForestNeedsIntroAndWolbaekIsPreUnlocked()
        {
            SortieUnlockPolicy policy = new SortieUnlockPolicy(G_Content, EGrowthContentProfile.Trial);

            Assert.Equal(ContractCodes.RejectReason.MISSING_UNLOCK,
                policy.Check(GameSessionCodes.Mode.NORMAL, REGION_FOREST, new[] { State(false) }));
            Assert.Equal(ContractCodes.RejectReason.NONE,
                policy.Check(GameSessionCodes.Mode.NORMAL, REGION_FOREST, new[] { State(true) }));
            Assert.Equal(ContractCodes.RejectReason.MISSING_UNLOCK,
                policy.Check(GameSessionCodes.Mode.NORMAL, REGION_FOREST, new[] { State(true), State(false) }));
            Assert.Equal(ContractCodes.RejectReason.NONE,
                policy.Check(GameSessionCodes.Mode.BOSS, BATTLE_WOLBAEK, new[] { State(false) }));
        }

        /// <summary> 체험판에 없는 지역은 성공 기록이 있어도 닫혀 있고, 정식은 앞 지역 성공 기록으로 열린다 </summary>
        [Fact]
        public void Check_RuinsByProfile_FollowsTrialAvailabilityAndClearRecord()
        {
            AccountProgressState cleared = State(true, (EAccountProgressKind.RegionCleared, REGION_FOREST));
            SortieUnlockPolicy trial = new SortieUnlockPolicy(G_Content, EGrowthContentProfile.Trial);
            SortieUnlockPolicy standard = new SortieUnlockPolicy(G_Content, EGrowthContentProfile.Standard);

            Assert.Equal(ContractCodes.RejectReason.MISSING_UNLOCK,
                trial.Check(GameSessionCodes.Mode.NORMAL, REGION_RUINS, new[] { cleared }));
            Assert.Equal(ContractCodes.RejectReason.NONE,
                standard.Check(GameSessionCodes.Mode.NORMAL, REGION_RUINS, new[] { cleared }));
            Assert.Equal(ContractCodes.RejectReason.MISSING_UNLOCK,
                standard.Check(GameSessionCodes.Mode.NORMAL, REGION_RUINS, new[] { State(true) }));
        }

        /// <summary> 비었거나 콘텐츠에 없는 battleId, 모드와 맞지 않는 ID는 판정하지 않는다 </summary>
        [Fact]
        public void Check_UnknownOrMismatchedTarget_IsNotJudged()
        {
            SortieUnlockPolicy policy = new SortieUnlockPolicy(G_Content, EGrowthContentProfile.Trial);

            Assert.Null(policy.TargetOf(GameSessionCodes.Mode.NORMAL, string.Empty));
            Assert.Null(policy.TargetOf(GameSessionCodes.Mode.NORMAL, UNKNOWN_BATTLE));
            Assert.Null(policy.TargetOf(GameSessionCodes.Mode.NORMAL, BATTLE_WOLBAEK));
            Assert.Null(policy.TargetOf(GameSessionCodes.Mode.BOSS, REGION_FOREST));
            Assert.Equal(BATTLE_CHEOLGAK, policy.TargetOf(GameSessionCodes.Mode.BOSS, BATTLE_CHEOLGAK));
        }

        /// <summary> 예약은 안내 미완료 계정이 있으면 MISSING_UNLOCK으로 아무것도 예약하지 않고, 기록 뒤에는 확정한다 </summary>
        [PostgresFact]
        public async Task Reserve_ForestWithoutIntro_RejectsUntilProgressRecorded()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            PostgresReservationRepository reservations = new PostgresReservationRepository(environment.DataSource,
                environment.Executor, environment.Leases, environment.JoinTokens,
                new SortieUnlockPolicy(G_Content, EGrowthContentProfile.Trial));

            ReservationOutcome rejected = await reservations.ReserveAsync(Forest(environment, player));
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, rejected.Result.Status);
            Assert.Equal(ContractCodes.RejectReason.MISSING_UNLOCK, rejected.Result.ReasonCode);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM game_sessions;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT COALESCE(sum(reserved), 0)::bigint FROM account_items;"));

            await environment.ExecuteAsync(
                "INSERT INTO account_progress (account_id, intro_completed_at) VALUES (@Id, clock_timestamp());",
                new { Id = player.AccountId });
            ReservationOutcome committed = await reservations.ReserveAsync(Forest(environment, player));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, committed.Result.Status);
        }

        /// <summary> 출시 프로필은 성장 콘텐츠에 없는 battleId(테스트용 지도, 빈 값)의 출정을 INVALID_TARGET으로 거절하고 아무것도
        /// 예약하지 않는다. 개발 프로필은 같은 요청을 판정 없이 확정한다 </summary>
        [PostgresFact]
        public async Task Reserve_UnknownBattle_RejectedOnlyInReleaseProfile()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            PostgresReservationRepository release = new PostgresReservationRepository(environment.DataSource,
                environment.Executor, environment.Leases, environment.JoinTokens,
                new SortieUnlockPolicy(G_Content, EGrowthContentProfile.Trial, rejectUnknownBattles: true));
            PostgresReservationRepository development = new PostgresReservationRepository(environment.DataSource,
                environment.Executor, environment.Leases, environment.JoinTokens,
                new SortieUnlockPolicy(G_Content, EGrowthContentProfile.Trial));

            ReservationOutcome unknown = await release.ReserveAsync(Normal(environment, player, UNKNOWN_BATTLE));
            ReservationOutcome empty = await release.ReserveAsync(Normal(environment, player, null));
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET, unknown.Result.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET, empty.Result.ReasonCode);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM game_sessions;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT COALESCE(sum(reserved), 0)::bigint FROM account_items;"));

            ReservationOutcome allowed = await development.ReserveAsync(Normal(environment, player, UNKNOWN_BATTLE));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, allowed.Result.Status);
            Assert.Equal(ContractCodes.RejectReason.NONE,
                new SortieUnlockPolicy(G_Content, EGrowthContentProfile.Trial, true).CheckKnownBattle(
                    GameSessionCodes.Mode.BOSS, BATTLE_WOLBAEK));
        }

        /// <summary> 준비 확인은 접속한 참가자에게만 안내 완료, 여우의 흔적, 기록 ID를 종류별로 돌려준다 </summary>
        [PostgresFact]
        public async Task Readiness_LeasedParticipant_ReportsRecordedProgress()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer recorded = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer fresh = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            await environment.ExecuteAsync(
                """
                INSERT INTO account_progress (account_id, intro_completed_at, fox_traces)
                    VALUES (@Id, clock_timestamp(), @Fox);
                INSERT INTO account_unlocks (account_id, unlock_kind, target_id, operation_id) VALUES
                    (@Id, 'RegionCleared', @Ruins, gen_random_uuid()),
                    (@Id, 'RegionCleared', @Forest, gen_random_uuid()),
                    (@Id, 'BattleCleared', @Cheolgak, gen_random_uuid()),
                    (@Id, 'BattleUnlocked', @Wolbaek, gen_random_uuid());
                """, new
                {
                    Id = recorded.AccountId, Fox = FOX_TRACES, Ruins = REGION_RUINS, Forest = REGION_FOREST,
                    Cheolgak = BATTLE_CHEOLGAK, Wolbaek = BATTLE_WOLBAEK
                });
            PostgresReadinessRepository readiness = new PostgresReadinessRepository(environment.DataSource);

            IReadOnlyList<ReadinessParticipantStatus> statuses = await readiness.ReadAsync(environment.TownId, new[]
            {
                (recorded.AccountId, recorded.CharacterId), (fresh.AccountId, fresh.CharacterId)
            });
            IReadOnlyList<ReadinessParticipantStatus> otherTown = await readiness.ReadAsync(Guid.NewGuid(), new[]
            {
                (recorded.AccountId, recorded.CharacterId)
            });

            ReadinessProgress progress = Assert.IsType<ReadinessProgress>(statuses[0].Progress);
            Assert.True(progress.IntroCompleted);
            Assert.Equal(FOX_TRACES, progress.FoxTraces);
            Assert.Equal(new[] { REGION_FOREST, REGION_RUINS }, progress.ClearedRegionIds);
            Assert.Empty(progress.UnlockedRegionIds);
            Assert.Equal(new[] { BATTLE_CHEOLGAK }, progress.ClearedBattleIds);
            Assert.Equal(new[] { BATTLE_WOLBAEK }, progress.UnlockedBattleIds);
            ReadinessProgress empty = Assert.IsType<ReadinessProgress>(statuses[1].Progress);
            Assert.False(empty.IntroCompleted);
            Assert.Equal(0, empty.FoxTraces);
            Assert.Empty(empty.ClearedRegionIds);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, otherTown[0].ReasonCode);
            Assert.Null(otherTown[0].Progress);
        }

        private static ReservationInput Normal(SessionTestEnvironment environment, TestPlayer player, string? battleId)
        {
            return new ReservationInput(environment.TownId, Guid.NewGuid(), GameSessionCodes.Mode.NORMAL,
                SessionTestEnvironment.MAP_ID, SessionTestEnvironment.CONTENT_VERSION, new[]
                {
                    new ParticipantReservationInput(player.AccountId, player.CharacterId, 0, new[]
                    {
                        new PotionReservationInput(SessionTestEnvironment.POTION_ID, SessionTestEnvironment.BINDING, 1)
                    })
                }, battleId, null, GameSessionCodes.Difficulty.NONE);
        }

        private static ReservationInput Forest(SessionTestEnvironment environment, TestPlayer player)
        {
            return new ReservationInput(environment.TownId, Guid.NewGuid(), GameSessionCodes.Mode.NORMAL,
                SessionTestEnvironment.MAP_ID, SessionTestEnvironment.CONTENT_VERSION, new[]
                {
                    new ParticipantReservationInput(player.AccountId, player.CharacterId, 0, new[]
                    {
                        new PotionReservationInput(SessionTestEnvironment.POTION_ID, SessionTestEnvironment.BINDING, 1)
                    })
                }, REGION_FOREST, null, GameSessionCodes.Difficulty.NONE);
        }

        private static AccountProgressState State(bool introCompleted,
            params (EAccountProgressKind Kind, string TargetId)[] records)
        {
            return new AccountProgressState(introCompleted, 0, 0, records);
        }
    }
}
