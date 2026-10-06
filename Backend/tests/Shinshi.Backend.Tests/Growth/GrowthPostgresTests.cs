// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Xunit
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Growth;

namespace Shinshi.Backend.Tests.Growth
{
    /// <summary> 부적과 수련 쓰기를 실제 PostgreSQL에서 검사한다. 거절은 무효과, 재전송은 처음 결과, 원장 합계는 잔액과 같아야 한다 </summary>
    public sealed class GrowthPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string WIDE_SLASH_1A = "T01-1A";
        private const string WIDE_SLASH_1B = "T01-1B";
        private const string WIDE_SLASH_2B = "T01-2B";
        private const string WIDE_SLASH_3A = "T01-3A";
        private const string SORCERER_1A   = "T05-1A";
        private const string WIDE_SLASH    = "skill_warrior_wide_slash";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Register_RollBoundaryAndPity_ConsumesPerAttemptAndLearnsOnce()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 1000,
                items: new Dictionary<string, long> { [WIDE_SLASH_1A] = 5 });

            // 첫 시도 성공률 50%: roll 5000은 실패, 실패 1회 뒤 65%: roll 6499는 성공
            GrowthOperationOutcome failed = await RegisterAsync(environment, player, 0, WIDE_SLASH_1A, 5000);
            AssertCommitted(failed, 1);
            Assert.Equal(GrowthCodes.Outcome.FAILED, failed.Outcome);
            TalismanProgressView progress = Assert.Single(failed.Growth!.Talismans);
            Assert.Equal((0, 1), (progress.Level, progress.RegisterFailures));
            Assert.Equal(850, failed.Growth.Essence);
            Assert.Equal(4, GrowthTestEnvironment.Quantity(failed.Growth, WIDE_SLASH_1A));

            GrowthOperationOutcome learned = await RegisterAsync(environment, player, 1, WIDE_SLASH_1A, 6499);
            AssertCommitted(learned, 2);
            Assert.Equal(GrowthCodes.Outcome.SUCCEEDED, learned.Outcome);
            progress = Assert.Single(learned.Growth!.Talismans);
            Assert.Equal((1, 0), (progress.Level, progress.RegisterFailures));

            GrowthOperationOutcome again = await RegisterAsync(environment, player, 2, WIDE_SLASH_1A, 0);
            AssertRejected(again, ContractCodes.RejectReason.INVALID_TARGET);
            Assert.Equal(700, again.Growth!.Essence);
            Assert.Equal(3, GrowthTestEnvironment.Quantity(again.Growth, WIDE_SLASH_1A));

            GrowthPlayer lucky = await environment.CreatePlayerAsync(essence: 150,
                items: new Dictionary<string, long> { [WIDE_SLASH_1A] = 1 });
            GrowthOperationOutcome first = await RegisterAsync(environment, lucky, 0, WIDE_SLASH_1A, 4999);
            Assert.Equal(GrowthCodes.Outcome.SUCCEEDED, first.Outcome);
            Assert.Equal(0, first.Growth!.Essence);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Register_ResentWithDifferentRoll_ReturnsFirstResultAndConflictsOnOtherBody()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 1000,
                items: new Dictionary<string, long> { [WIDE_SLASH_1A] = 3, [WIDE_SLASH_1B] = 3 });
            Guid operationId = Guid.NewGuid();

            GrowthOperationOutcome first = await RegisterAsync(environment, player, 0, WIDE_SLASH_1A, 9999, operationId);
            GrowthOperationOutcome resent = await RegisterAsync(environment, player, 0, WIDE_SLASH_1A, 0, operationId);
            GrowthOperationOutcome conflict = await RegisterAsync(environment, player, 0, WIDE_SLASH_1B, 0, operationId);

            Assert.Equal(GrowthCodes.Outcome.FAILED, first.Outcome);
            Assert.Equal(GrowthCodes.Outcome.FAILED, resent.Outcome);
            Assert.Equal(first.Result!.ReceiptId, resent.Result!.ReceiptId);
            Assert.Equal(first.Result.ServerTimeUnixMs, resent.Result.ServerTimeUnixMs);
            AssertRejected(conflict, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);
            Assert.Equal(850, resent.Growth!.Essence);
            Assert.Equal(2, GrowthTestEnvironment.Quantity(resent.Growth, WIDE_SLASH_1A));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason AND asset_kind = 'Essence';",
                new { Reason = ContractCodes.EconomyReason.TALISMAN_REGISTER }));
            await environment.AssertReconciledAsync();
        }

        /// <summary> 멱등 actor는 계정이다. 응답을 잃고 다른 마을로 재접속한 뒤 바뀐 revision으로 같은 operationId를 보내도
        /// 처음 결과(같은 영수증, 같은 판정)를 받고 비용은 한 번만 빠진다. 영수증에는 중계 마을과 확률 입력이 남는다 </summary>
        [PostgresFact]
        public async Task Register_ResentFromOtherTownWithNewRevision_ReturnsStoredResult()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 1000,
                items: new Dictionary<string, long> { [WIDE_SLASH_1A] = 3 });
            Guid operationId = Guid.NewGuid();

            GrowthOperationOutcome first = await RegisterAsync(environment, player, 0, WIDE_SLASH_1A, 9999, operationId);
            AssertCommitted(first, 1);

            Guid otherTown = Guid.NewGuid();
            await environment.Pool.RegisterAsync(new Shinshi.Data.Sessions.ServerRegistration(otherTown, Guid.NewGuid(),
                ContractCodes.ServerRole.TOWN, "127.0.0.1", 7779, "test-build", GrowthTestEnvironment.CONTENT_VERSION));
            await environment.ExecuteAsync(
                "UPDATE account_leases SET instance_id = @Town, lease_id = gen_random_uuid() WHERE account_id = @AccountId;",
                new { Town = otherTown, player.AccountId });
            GrowthOperationOutcome resent = await environment.Talismans.RegisterAsync(otherTown, operationId,
                player.CharacterId, first.Result!.CommittedRevision, WIDE_SLASH_1A, 0, false);
            GrowthOperationOutcome stored = await environment.Talismans.RegisterAsync(otherTown, operationId,
                player.CharacterId, 7, WIDE_SLASH_1A, 0, true);

            Assert.Equal(first.Result.ReceiptId, resent.Result!.ReceiptId);
            Assert.Equal(first.Result.ServerTimeUnixMs, resent.Result.ServerTimeUnixMs);
            Assert.Equal(GrowthCodes.Outcome.FAILED, resent.Outcome);
            Assert.Equal(first.Result.ReceiptId, stored.Result!.ReceiptId);
            Assert.Equal(850, resent.Growth!.Essence);
            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM growth_receipts
                WHERE actor_id = @AccountId AND operation_id = @OperationId AND town_instance_id = @Town
                    AND roll_basis_points = 9999;
                """, new { player.AccountId, OperationId = operationId, Town = environment.TownId }));
            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM economy_ledger
                WHERE actor_id = @AccountId AND operation_id = @OperationId AND actor_kind = 'Player'
                    AND asset_kind = 'Essence';
                """, new { player.AccountId, OperationId = operationId }));
            await environment.AssertReconciledAsync();
        }

        /// <summary> 한 작업이 같은 자산을 두 줄(지급 뒤 차감) 써도 entry_key의 작업 내 일련번호로 겹치지 않고 대사가 맞는다 </summary>
        [PostgresFact]
        public async Task EffectContext_SameAssetTwiceInOneOperation_WritesTwoLedgerRows()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 10);
            Shinshi.Data.OperationRequest request = new Shinshi.Data.OperationRequest(player.AccountId, Guid.NewGuid(),
                "test.growth.twice", "{}");

            OperationResult result = await environment.Executor.ExecuteAsync(request, async (context, token) =>
            {
                GrowthEffectContext effect = new GrowthEffectContext(context, environment.Content, environment.Ledger,
                    environment.History, player.AccountId, "System", request.OperationId, player.AccountId,
                    player.CharacterId, 0, ContractCodes.EconomyReason.ADMIN_GRANT);
                await effect.CreditAsync(effect.Essence(), 5, token);
                Assert.True(await effect.TryDebitAsync(effect.Essence(), 3, token));
                return Shinshi.Data.OperationDecision.Committed(0, Guid.NewGuid().ToString("D"));
            });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE operation_id = @OperationId AND asset_kind = 'Essence';",
                new { request.OperationId }));
            Assert.Equal(12, (await environment.ReadAsync(player)).Essence);
            await environment.AssertReconciledAsync();
        }

        /// <summary> 없는 캐릭터는 멱등 actor(계정)를 정할 수 없어 저장 없이 INVALID_TARGET이다 </summary>
        [PostgresFact]
        public async Task Register_UnknownCharacter_RejectsWithoutStoringOperation()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            Guid operationId = Guid.NewGuid();

            GrowthOperationOutcome outcome = await environment.Talismans.RegisterAsync(environment.TownId, operationId,
                Guid.NewGuid(), 0, WIDE_SLASH_1A, 0, false);
            GrowthOperationOutcome blocked = await environment.Talismans.RegisterAsync(environment.TownId, operationId,
                Guid.NewGuid(), 0, WIDE_SLASH_1A, 0, true);

            AssertRejected(outcome, ContractCodes.RejectReason.INVALID_TARGET);
            Assert.Null(blocked.Result);
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM operations WHERE operation_id = @OperationId;", new { OperationId = operationId }));
        }

        [PostgresFact]
        public async Task Guards_LocationSortieRevisionAndBalance_RejectWithoutEffect()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 1000,
                items: new Dictionary<string, long> { [WIDE_SLASH_1A] = 2 });
            GrowthPlayer poor = await environment.CreatePlayerAsync(essence: 149,
                items: new Dictionary<string, long> { [WIDE_SLASH_1A] = 2 });
            long seedRows = await environment.ScalarAsync("SELECT count(*) FROM economy_ledger;");

            GrowthOperationOutcome stale = await RegisterAsync(environment, player, 5, WIDE_SLASH_1A, 0);
            AssertRejected(stale, ContractCodes.RejectReason.STALE_REVISION);
            Assert.Equal(0, stale.Result!.CommittedRevision);

            GrowthOperationOutcome wrongTown = await environment.Talismans.RegisterAsync(Guid.NewGuid(), Guid.NewGuid(),
                player.CharacterId, 0, WIDE_SLASH_1A, 0, false);
            AssertRejected(wrongTown, ContractCodes.RejectReason.WRONG_LOCATION);

            AssertRejected(await RegisterAsync(environment, poor, 0, WIDE_SLASH_1A, 0),
                ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            AssertRejected(await RegisterAsync(environment, player, 0, SORCERER_1A, 0),
                ContractCodes.RejectReason.INVALID_TARGET);

            await OpenSortieAsync(environment, player);
            AssertRejected(await RegisterAsync(environment, player, 0, WIDE_SLASH_1A, 0), ContractCodes.RejectReason.LOCKED);
            AssertRejected(await environment.Training.TrainAsync(environment.TownId, Guid.NewGuid(), player.CharacterId, 0,
                0, 1, false), ContractCodes.RejectReason.LOCKED);

            CharacterGrowthSnapshot growth = await environment.ReadAsync(player);
            Assert.Equal(1000, growth.Essence);
            Assert.Equal(2, GrowthTestEnvironment.Quantity(growth, WIDE_SLASH_1A));
            Assert.Empty(growth.Talismans);
            Assert.Equal(0, growth.LoadoutRevision);
            Assert.Equal(149, (await environment.ReadAsync(poor)).Essence);
            Assert.Equal(seedRows, await environment.ScalarAsync("SELECT count(*) FROM economy_ledger;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM growth_receipts;"));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Upgrade_ToLevelFive_ChargesTableAndRejectsBeyondWithoutCost()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 10000,
                items: new Dictionary<string, long> { [WIDE_SLASH_1A] = 10, [SORCERER_1A] = 3 });
            await LearnAsync(environment, player, WIDE_SLASH_1A, 1);

            long revision = 0;
            GrowthOperationOutcome outcome = null!;
            for (int level = 2; level <= GrowthContent.MAX_EFFECT_LEVEL; ++level)
            {
                outcome = await environment.Talismans.UpgradeAsync(environment.TownId, Guid.NewGuid(), player.CharacterId,
                    revision, WIDE_SLASH_1A, false);
                AssertCommitted(outcome, ++revision);
            }

            Assert.Equal(GrowthContent.MAX_EFFECT_LEVEL, Assert.Single(outcome.Growth!.Talismans).Level);
            Assert.Equal(10000 - 3500, outcome.Growth.Essence);
            Assert.Equal(3, GrowthTestEnvironment.Quantity(outcome.Growth, WIDE_SLASH_1A));

            GrowthOperationOutcome capped = await environment.Talismans.UpgradeAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, revision, WIDE_SLASH_1A, false);
            AssertRejected(capped, ContractCodes.RejectReason.INVALID_TARGET);
            Assert.Equal(6500, capped.Growth!.Essence);
            Assert.Equal(3, GrowthTestEnvironment.Quantity(capped.Growth, WIDE_SLASH_1A));
            AssertRejected(await environment.Talismans.UpgradeAsync(environment.TownId, Guid.NewGuid(), player.CharacterId,
                revision, WIDE_SLASH_1B, false), ContractCodes.RejectReason.MISSING_UNLOCK);
            AssertRejected(await environment.Talismans.UpgradeAsync(environment.TownId, Guid.NewGuid(), player.CharacterId,
                revision, SORCERER_1A, false), ContractCodes.RejectReason.INVALID_TARGET);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task LoadoutAndDisassemble_LearnedOwnClassOnly()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 0,
                items: new Dictionary<string, long> { [WIDE_SLASH_1A] = 3, [SORCERER_1A] = 2 });
            await LearnAsync(environment, player, WIDE_SLASH_1A, 1);
            await LearnAsync(environment, player, WIDE_SLASH_2B, 2);

            GrowthOperationOutcome equipped = await environment.Talismans.ReplaceLoadoutAsync(environment.TownId,
                Guid.NewGuid(), player.CharacterId, 0, new[]
                {
                    new TalismanLoadoutEntry(WIDE_SLASH, 0, WIDE_SLASH_1A), new TalismanLoadoutEntry(WIDE_SLASH, 1, WIDE_SLASH_2B)
                }, false);
            AssertCommitted(equipped, 1);
            Assert.Equal(2, equipped.Growth!.Loadout.Length);

            GrowthOperationOutcome unlearned = await environment.Talismans.ReplaceLoadoutAsync(environment.TownId,
                Guid.NewGuid(), player.CharacterId, 1, new[] { new TalismanLoadoutEntry(WIDE_SLASH, 2, WIDE_SLASH_3A) }, false);
            AssertRejected(unlearned, ContractCodes.RejectReason.MISSING_UNLOCK);
            Assert.Equal(2, unlearned.Growth!.Loadout.Length);

            AssertCommitted(await environment.Talismans.ReplaceLoadoutAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 1, Array.Empty<TalismanLoadoutEntry>(), false), 2);

            GrowthOperationOutcome dismantled = await environment.Talismans.DisassembleAsync(environment.TownId,
                Guid.NewGuid(), player.CharacterId, 2, WIDE_SLASH_1A, 2, false);
            AssertCommitted(dismantled, 3);
            Assert.Equal(100, dismantled.Growth!.Essence);
            Assert.Equal(1, GrowthTestEnvironment.Quantity(dismantled.Growth, WIDE_SLASH_1A));
            AssertRejected(await environment.Talismans.DisassembleAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 3, SORCERER_1A, 1, false), ContractCodes.RejectReason.INVALID_TARGET);
            AssertRejected(await environment.Talismans.DisassembleAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 3, WIDE_SLASH_1A, 2, false), ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason AND asset_kind = 'Essence' AND delta = 100;",
                new { Reason = ContractCodes.EconomyReason.DISMANTLE }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Training_InvestToCapsAndRespec_ChargesFormula()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 70000);

            AssertCommitted(await TrainAsync(environment, player, 0, 0, 40), 1);
            AssertRejected(await TrainAsync(environment, player, 1, 0, 1), ContractCodes.RejectReason.INVALID_TARGET);
            AssertCommitted(await TrainAsync(environment, player, 1, 1, 40), 2);
            GrowthOperationOutcome full = await TrainAsync(environment, player, 2, 2, 20);
            AssertCommitted(full, 3);
            Assert.Equal(70000 - 59500, full.Growth!.Essence);
            Assert.Equal(new[] { 40, 40, 20, 0, 0 }, full.Growth.Training);
            AssertRejected(await TrainAsync(environment, player, 3, 3, 1), ContractCodes.RejectReason.INVALID_TARGET);

            GrowthOperationOutcome same = await environment.Training.RespecAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 3, new[] { 40, 40, 20, 0, 0 }, false);
            AssertCommitted(same, 3);
            Assert.Equal(10500, same.Growth!.Essence);

            GrowthOperationOutcome respec = await environment.Training.RespecAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 3, new[] { 20, 20, 20, 20, 20 }, false);
            AssertCommitted(respec, 4);
            Assert.Equal(10500 - 2200, respec.Growth!.Essence);
            AssertRejected(await environment.Training.RespecAsync(environment.TownId, Guid.NewGuid(), player.CharacterId, 4,
                new[] { 20, 20, 20, 20, 19 }, false), ContractCodes.RejectReason.INVALID_TARGET);

            GrowthPlayer fresh = await environment.CreatePlayerAsync(essence: 0);
            AssertCommitted(await environment.Training.RespecAsync(environment.TownId, Guid.NewGuid(), fresh.CharacterId, 0,
                new int[GrowthContent.TRAINING_STAT_COUNT], false), 0);
            AssertRejected(await TrainAsync(environment, fresh, 0, 0, 1), ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            await environment.AssertReconciledAsync();
        }

        internal static async Task OpenSortieAsync(GrowthTestEnvironment environment, GrowthPlayer player)
        {
            Guid dungeon = Guid.NewGuid();
            await environment.Pool.RegisterAsync(new Shinshi.Data.Sessions.ServerRegistration(dungeon, Guid.NewGuid(),
                ContractCodes.ServerRole.DUNGEON, "127.0.0.1", 7778, "test-build", GrowthTestEnvironment.CONTENT_VERSION));
            Guid session = Guid.NewGuid();
            await environment.ExecuteAsync(
                """
                INSERT INTO game_sessions (session_id, mode, map_id, state, starting_count, content_version, instance_id,
                    server_lease, expires_at)
                VALUES (@Session, 'Normal', 'test-map', 'Reserved', 1, @Content, @Dungeon, gen_random_uuid(),
                    clock_timestamp() + interval '1 minute');
                INSERT INTO session_participants (session_id, participant_id, account_id, character_id, state)
                VALUES (@Session, gen_random_uuid(), @AccountId, @CharacterId, 'Reserved');
                """, new
                {
                    Session = session, Content = GrowthTestEnvironment.CONTENT_VERSION, Dungeon = dungeon,
                    player.AccountId, player.CharacterId
                });
        }

        internal static Task LearnAsync(GrowthTestEnvironment environment, GrowthPlayer player, string effectId, int level)
        {
            return environment.ExecuteAsync(
                "INSERT INTO talisman_progress (character_id, effect_id, level) VALUES (@CharacterId, @EffectId, @Level);",
                new { player.CharacterId, EffectId = effectId, Level = level });
        }

        internal static void AssertCommitted(GrowthOperationOutcome outcome, long revision)
        {
            Assert.NotNull(outcome.Result);
            Assert.Equal(ContractCodes.RejectReason.NONE, outcome.Result.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, outcome.Result.Status);
            Assert.Equal(revision, outcome.Result.CommittedRevision);
            Assert.NotNull(outcome.Growth);
            Assert.Equal(revision, outcome.Growth.LoadoutRevision);
        }

        internal static void AssertRejected(GrowthOperationOutcome outcome, int reason)
        {
            Assert.NotNull(outcome.Result);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, outcome.Result.Status);
            Assert.Equal(reason, outcome.Result.ReasonCode);
        }

        private static Task<GrowthOperationOutcome> RegisterAsync(GrowthTestEnvironment environment, GrowthPlayer player,
            long revision, string effectId, int roll, Guid? operationId = null)
        {
            return environment.Talismans.RegisterAsync(environment.TownId, operationId ?? Guid.NewGuid(), player.CharacterId,
                revision, effectId, roll, false);
        }

        private static Task<GrowthOperationOutcome> TrainAsync(GrowthTestEnvironment environment, GrowthPlayer player,
            long revision, int stat, int steps)
        {
            return environment.Training.TrainAsync(environment.TownId, Guid.NewGuid(), player.CharacterId, revision, stat,
                steps, false);
        }
    }
}
