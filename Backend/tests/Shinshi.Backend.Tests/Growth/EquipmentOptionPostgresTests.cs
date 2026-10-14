// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Growth;

namespace Shinshi.Backend.Tests.Growth
{
    /// <summary> 장비 옵션 재설정과 옵션이 지나가는 저장, 조회, 계승 경로를 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class EquipmentOptionPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int  ATTACK       = EquipmentOptionRules.KIND_ATTACK_RATE;
        private const int  CRIT         = EquipmentOptionRules.KIND_CRIT_CHANCE;
        private const int  NONE         = EquipmentOptionRules.KIND_NONE;
        private const int  WEAPON_SLOT  = 0;
        private const long ESSENCE_COST = 300;
        private const long SHARD_COST   = 3;

        // 일반 무기: 줄1 공격 50, 줄2 치명 20. 상급 줄이 없어 누계가 오른다
        private static readonly int[] G_LowRolls  = { 0, 0, 5000, 0 };

        // 일반 무기: 줄1 공격 100(최댓값), 줄2 공격 50. 상급 줄이 있어 누계가 0이 된다
        private static readonly int[] G_HighRolls = { 0, 9999, 0, 0 };

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Reroll_CommitsCostOptionsBindingPityHistoryLedgerAndReceiptTogether()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 1000, shards: 10);

            // 착용 중이고 잠긴 장비도 재설정할 수 있다
            Guid item = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 4, equipped: true, locked: true);
            Guid operationId = Guid.NewGuid();

            GrowthOperationOutcome first = await environment.Equipment.RerollOptionsAsync(environment.TownId, operationId,
                player.CharacterId, 0, item, G_LowRolls, false);

            GrowthPostgresTests.AssertCommitted(first, 1);
            Assert.Equal(GrowthCodes.Outcome.SUCCEEDED, first.Outcome);
            Assert.Equal(item, first.ItemId);
            Assert.Equal((1000 - ESSENCE_COST, 10 - SHARD_COST), (first.Growth!.Essence, first.Growth.Shards));
            EquipmentView view = Assert.Single(first.Growth.Equipment);
            Assert.Equal((ATTACK, 50, CRIT, 20), Options(view));
            Assert.True(view.Bound && view.Equipped && view.Locked);
            Assert.Equal(4, view.Enhancement);
            Assert.Equal(new[] { 1, 0, 0, 0, 0, 0 }, first.Growth.OptionRerollPity);

            // 이력의 이전 상태는 옵션 키가 없고 이후 상태에만 붙는다
            (string before, string after) = await ReadHistoryAsync(environment, item, GrowthAssets.HISTORY_OPTIONS_REROLLED);
            Assert.Null(JObject.Parse(before)["option1Kind"]);
            JObject afterState = JObject.Parse(after);
            Assert.Equal((ATTACK, 50, CRIT, 20), ((int)afterState["option1Kind"]!, (int)afterState["option1Value"]!,
                (int)afterState["option2Kind"]!, (int)afterState["option2Value"]!));
            Assert.True((bool)afterState["bound"]!);

            // 영수증에는 뽑기 네 개가 남고 기존 단일 roll 칸은 비어 있다
            await using (NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync())
            {
                (string kind, int[] rolls, int? roll, long revision) = await connection.QuerySingleAsync<(string, int[], int?, long)>(
                    """
                    SELECT operation_kind, roll_values, roll_basis_points, loadout_revision FROM growth_receipts
                    WHERE actor_id = @AccountId AND operation_id = @OperationId;
                    """, new { player.AccountId, OperationId = operationId });
                Assert.Equal(PostgresEquipmentRepository.REROLL_OPTIONS_KIND, kind);
                Assert.Equal(G_LowRolls, rolls);
                Assert.Null(roll);
                Assert.Equal(1, revision);
            }

            Assert.Equal(-(ESSENCE_COST + SHARD_COST), await environment.ScalarAsync(
                "SELECT COALESCE(sum(delta), 0)::bigint FROM economy_ledger WHERE reason = @Reason AND operation_id = @OperationId;",
                new { Reason = ContractCodes.EconomyReason.REROLL_OPTIONS, OperationId = operationId }));
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason;",
                new { Reason = ContractCodes.EconomyReason.REROLL_OPTIONS }));

            // 두 번째 재설정은 두 줄을 모두 덮어쓰고 상급 줄이 나와 누계가 0으로 돌아간다
            GrowthOperationOutcome second = await RerollAsync(environment, player, 1, item, G_HighRolls);
            GrowthPostgresTests.AssertCommitted(second, 2);
            Assert.Equal((ATTACK, 100, ATTACK, 50), Options(Assert.Single(second.Growth!.Equipment)));
            Assert.Equal(new int[GrowthContent.SLOT_COUNT], second.Growth.OptionRerollPity);
            Assert.Equal(1000 - (2 * ESSENCE_COST), second.Growth.Essence);
            Assert.Equal(10 - (2 * SHARD_COST), second.Growth.Shards);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Reroll_InsufficientEssenceOrShards_ChangesNothing()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer poorEssence = await environment.CreatePlayerAsync(essence: ESSENCE_COST - 1, shards: 10);
            Guid first = await environment.InsertEquipmentAsync(poorEssence, "equip_weapon_common", 0);

            // 정기는 충분하고 정련편만 모자라면 먼저 뺀 정기도 함께 되돌아가야 한다
            GrowthPlayer poorShards = await environment.CreatePlayerAsync(essence: 1000, shards: SHARD_COST - 1);
            Guid second = await environment.InsertEquipmentAsync(poorShards, "equip_weapon_common", 0);

            GrowthOperationOutcome noEssence = await RerollAsync(environment, poorEssence, 0, first, G_HighRolls);
            GrowthOperationOutcome noShards = await RerollAsync(environment, poorShards, 0, second, G_HighRolls);

            GrowthPostgresTests.AssertRejected(noEssence, ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            GrowthPostgresTests.AssertRejected(noShards, ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            Assert.Equal((ESSENCE_COST - 1, 10L, 0L), (noEssence.Growth!.Essence, noEssence.Growth.Shards,
                noEssence.Growth.LoadoutRevision));
            Assert.Equal((1000L, SHARD_COST - 1, 0L), (noShards.Growth!.Essence, noShards.Growth.Shards,
                noShards.Growth.LoadoutRevision));
            foreach (GrowthOperationOutcome outcome in new[] { noEssence, noShards })
            {
                EquipmentView view = Assert.Single(outcome.Growth!.Equipment);
                Assert.Equal((NONE, 0, NONE, 0), Options(view));
                Assert.False(view.Bound);
                Assert.Equal(new int[GrowthContent.SLOT_COUNT], outcome.Growth.OptionRerollPity);
            }

            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM option_reroll_pity;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM item_history;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM growth_receipts;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM economy_ledger WHERE reason = @Reason;",
                new { Reason = ContractCodes.EconomyReason.REROLL_OPTIONS }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Reroll_SameOperationId_ReplaysWithoutSecondDebitOrReroll()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 1000, shards: 10);
            Guid item = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 0);
            Guid operationId = Guid.NewGuid();

            GrowthOperationOutcome first = await environment.Equipment.RerollOptionsAsync(environment.TownId, operationId,
                player.CharacterId, 0, item, G_LowRolls, false);

            // 재전송은 난수와 마을 서버가 다시 읽은 revision이 달라도 처음 결과다(둘 다 요청 해시에서 빠진다)
            GrowthOperationOutcome replay = await environment.Equipment.RerollOptionsAsync(environment.TownId, operationId,
                player.CharacterId, 1, item, G_HighRolls, false);
            GrowthOperationOutcome stored = await environment.Equipment.RerollOptionsAsync(environment.TownId, operationId,
                player.CharacterId, 1, item, G_HighRolls, true);

            GrowthPostgresTests.AssertCommitted(first, 1);
            foreach (GrowthOperationOutcome again in new[] { replay, stored })
            {
                GrowthPostgresTests.AssertCommitted(again, 1);
                Assert.Equal(first.Result!.ReceiptId, again.Result!.ReceiptId);
                Assert.Equal((GrowthCodes.Outcome.SUCCEEDED, (Guid?)item), (again.Outcome, again.ItemId));
                Assert.Equal((ATTACK, 50, CRIT, 20), Options(Assert.Single(again.Growth!.Equipment)));
                Assert.Equal((1000 - ESSENCE_COST, 10 - SHARD_COST), (again.Growth.Essence, again.Growth.Shards));
                Assert.Equal(1, again.Growth.OptionRerollPity[WEAPON_SLOT]);
            }

            // 같은 ID로 다른 장비를 가리키면 본문이 달라 멱등 충돌이다
            Guid other = await environment.InsertEquipmentAsync(player, "equip_head_common", 0);
            GrowthPostgresTests.AssertRejected(await environment.Equipment.RerollOptionsAsync(environment.TownId, operationId,
                player.CharacterId, 1, other, G_LowRolls, false), ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);

            // 같은 operationId 동시 요청도 한 번만 적용한다
            GrowthPlayer racer = await environment.CreatePlayerAsync(essence: 1000, shards: 10);
            Guid racerItem = await environment.InsertEquipmentAsync(racer, "equip_weapon_common", 0);
            Guid raceId = Guid.NewGuid();
            GrowthOperationOutcome[] races = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                environment.Equipment.RerollOptionsAsync(environment.TownId, raceId, racer.CharacterId, 0, racerItem,
                    G_LowRolls, false)));
            Assert.All(races, outcome => Assert.Equal(races[0].Result!.ReceiptId, outcome.Result!.ReceiptId));
            CharacterGrowthSnapshot racerGrowth = await environment.ReadAsync(racer);
            Assert.Equal((1000 - ESSENCE_COST, 10 - SHARD_COST, 1L), (racerGrowth.Essence, racerGrowth.Shards,
                racerGrowth.LoadoutRevision));
            Assert.Equal(1, racerGrowth.OptionRerollPity[WEAPON_SLOT]);

            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM item_history WHERE event_kind = @Kind;",
                new { Kind = GrowthAssets.HISTORY_OPTIONS_REROLLED }));
            Assert.Equal(2L, await environment.ScalarAsync("SELECT count(*) FROM growth_receipts;"));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Reroll_ConcurrentOperationsWithOneRerollBudget_DebitOnce()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: ESSENCE_COST, shards: SHARD_COST);
            Guid item = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 0);

            GrowthOperationOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                RerollAsync(environment, player, 0, item, G_LowRolls)));

            Assert.Single(outcomes, outcome => outcome.Result!.Status == ContractCodes.OperationStatus.COMMITTED);
            Assert.All(outcomes.Where(outcome => outcome.Result!.Status == ContractCodes.OperationStatus.REJECTED),
                outcome => Assert.Contains(outcome.Result!.ReasonCode, new[]
                {
                    ContractCodes.RejectReason.STALE_REVISION, ContractCodes.RejectReason.INSUFFICIENT_ITEM
                }));
            CharacterGrowthSnapshot growth = await environment.ReadAsync(player);
            Assert.Equal((0L, 0L, 1L), (growth.Essence, growth.Shards, growth.LoadoutRevision));
            Assert.Equal((ATTACK, 50, CRIT, 20), Options(Assert.Single(growth.Equipment)));
            Assert.Equal(1, growth.OptionRerollPity[WEAPON_SLOT]);
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM item_history;"));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Reroll_TrainingForeignMissingOrEscrowedItem_RejectsWithoutCost()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 1000, shards: 10);
            GrowthPlayer stranger = await environment.CreatePlayerAsync(essence: 1000, shards: 10);
            Guid training = await environment.InsertEquipmentAsync(player, "equip_weapon_training", 0, equipped: true,
                origin: GrowthCodes.EquipmentOrigin.INITIAL_GRANT);
            Guid foreign = await environment.InsertEquipmentAsync(stranger, "equip_weapon_common", 0);
            Guid escrowed = await environment.InsertEquipmentAsync(player, "equip_head_common", 0);
            await environment.ExecuteAsync(
                """
                INSERT INTO market_listings (listing_id, seller_account_id, seller_character_id, asset_kind, definition_id,
                    item_id, grade, category, unit_price, listed_qty, remaining_qty, state, expires_at)
                VALUES (@ListingId, @AccountId, @CharacterId, 'Equipment', 'equip_head_common', @ItemId, 0, 1, 100, 1, 1,
                    'Active', clock_timestamp() + interval '1 day');
                UPDATE equipment_instances SET escrow_kind = 'Market', escrow_ref = @ListingId WHERE item_id = @ItemId;
                """, new { ListingId = Guid.NewGuid(), player.AccountId, player.CharacterId, ItemId = escrowed });

            GrowthPostgresTests.AssertRejected(await RerollAsync(environment, player, 0, training, G_HighRolls),
                ContractCodes.RejectReason.INVALID_TARGET);
            GrowthPostgresTests.AssertRejected(await RerollAsync(environment, player, 0, foreign, G_HighRolls),
                ContractCodes.RejectReason.INVALID_TARGET);
            GrowthPostgresTests.AssertRejected(await RerollAsync(environment, player, 0, Guid.NewGuid(), G_HighRolls),
                ContractCodes.RejectReason.INVALID_TARGET);
            GrowthOperationOutcome market = await RerollAsync(environment, player, 0, escrowed, G_HighRolls);
            GrowthPostgresTests.AssertRejected(market, ContractCodes.RejectReason.MARKET_ESCROWED);

            Assert.Equal((1000L, 10L, 0L), (market.Growth!.Essence, market.Growth.Shards, market.Growth.LoadoutRevision));
            Assert.All(market.Growth.Equipment, view => Assert.Equal((NONE, 0, NONE, 0), Options(view)));
            Assert.Equal((NONE, 0, NONE, 0), Options(Assert.Single((await environment.ReadAsync(stranger)).Equipment)));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM option_reroll_pity;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM item_history;"));

            // 형식이 틀린 난수는 작업을 만들기 전에 막는다
            await Assert.ThrowsAsync<ArgumentException>(() => environment.Equipment.RerollOptionsAsync(environment.TownId,
                Guid.NewGuid(), player.CharacterId, 0, foreign, new[] { 0, 0, 0 }, false));
            await Assert.ThrowsAsync<ArgumentException>(() => environment.Equipment.RerollOptionsAsync(environment.TownId,
                Guid.NewGuid(), player.CharacterId, 0, foreign, new[] { 0, 0, 0, EquipmentOptionRules.ROLL_RANGE }, false));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Reroll_PityKeyedByCharacterAndSlot_ForcesLineOneMaxOnTenthMiss()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 10000, shards: 100);
            Guid common = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 0);
            Guid rare = await environment.InsertEquipmentAsync(player, "equip_weapon_rare", 0);
            Guid head = await environment.InsertEquipmentAsync(player, "equip_head_common", 0);
            int[] lowest = { 0, 0, 0, 0 };
            await environment.ExecuteAsync(
                "INSERT INTO option_reroll_pity (character_id, slot, misses) VALUES (@CharacterId, 0, 8);",
                new { player.CharacterId });

            // 누계 8에서 하는 아홉 번째는 난수대로이고, 다른 부위는 무기 누계를 쓰지 않는다
            GrowthOperationOutcome ninth = await RerollAsync(environment, player, 0, common, lowest);
            Assert.Equal((ATTACK, 50, ATTACK, 50), Options(Find(ninth, common)));
            Assert.Equal(new[] { 9, 0, 0, 0, 0, 0 }, ninth.Growth!.OptionRerollPity);

            GrowthOperationOutcome otherSlot = await RerollAsync(environment, player, 1, head, lowest);
            Assert.Equal((EquipmentOptionRules.KIND_HEALTH_RATE, 50, EquipmentOptionRules.KIND_HEALTH_RATE, 50),
                Options(Find(otherSlot, head)));
            Assert.Equal(new[] { 9, 1, 0, 0, 0, 0 }, otherSlot.Growth!.OptionRerollPity);

            // 같은 부위의 다른 장비가 누계를 이어받아 열 번째에 줄1이 그 등급의 최댓값으로 확정된다(희귀 공격 100~200)
            GrowthOperationOutcome tenth = await RerollAsync(environment, player, 2, rare, lowest);
            GrowthPostgresTests.AssertCommitted(tenth, 3);
            Assert.Equal((ATTACK, 200, ATTACK, 100), Options(Find(tenth, rare)));
            Assert.Equal(new[] { 0, 1, 0, 0, 0, 0 }, tenth.Growth!.OptionRerollPity);
            Assert.Equal((ATTACK, 50, ATTACK, 50), Options(Find(tenth, common)));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Inherit_MovesSourceOptionsRaisesBelowTargetMinAndDropsTargetOptions()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 2000);

            // 상위 등급: 희귀 공격 100, 치명 50은 영웅 범위(공격 150~300, 치명 70~150) 아래라 min으로 오른다
            Guid rareSource = await environment.InsertEquipmentAsync(player, "equip_weapon_rare", 17);
            Guid epicTarget = await environment.InsertEquipmentAsync(player, "equip_weapon_epic", 0);
            await SetOptionsAsync(environment, rareSource, ATTACK, 100, CRIT, 50);
            await SetOptionsAsync(environment, epicTarget, ATTACK, 300, ATTACK, 300);

            // 같은 등급: 값은 그대로이고 옵션 없는 줄은 없음으로 옮겨져 대상의 옵션을 지운다
            Guid headSource = await environment.InsertEquipmentAsync(player, "equip_head_rare", 12);
            Guid headTarget = await environment.InsertEquipmentAsync(player, "equip_head_rare", 0);
            await SetOptionsAsync(environment, headSource, EquipmentOptionRules.KIND_DEFENSE_RATE, 150, NONE, 0);
            await SetOptionsAsync(environment, headTarget, EquipmentOptionRules.KIND_HEALTH_RATE, 200,
                EquipmentOptionRules.KIND_HEALTH_RATE, 200);

            GrowthOperationOutcome upper = await environment.Equipment.InheritAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 0, rareSource, epicTarget, false);
            GrowthOperationOutcome same = await environment.Equipment.InheritAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 1, headSource, headTarget, false);

            GrowthPostgresTests.AssertCommitted(upper, 1);
            GrowthPostgresTests.AssertCommitted(same, 2);
            EquipmentView weapon = Find(same, epicTarget);
            Assert.Equal((ATTACK, 150, CRIT, 70), Options(weapon));
            Assert.Equal((10, true), (weapon.Enhancement, weapon.Bound));
            EquipmentView helmet = Find(same, headTarget);
            Assert.Equal((EquipmentOptionRules.KIND_DEFENSE_RATE, 150, NONE, 0), Options(helmet));
            Assert.Equal(12, helmet.Enhancement);
            Assert.Equal(2, same.Growth!.Equipment.Length);
            Assert.Equal(0, same.Growth.Essence);

            // 소모된 원본의 마지막 상태에는 옵션이 남아 추적할 수 있다
            (string consumed, string _) = await ReadHistoryAsync(environment, rareSource, GrowthAssets.HISTORY_INHERIT_CONSUMED);
            Assert.Equal((ATTACK, 100), ((int)JObject.Parse(consumed)["option1Kind"]!, (int)JObject.Parse(consumed)["option1Value"]!));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task EnhanceEquipLockAndDismantle_KeepOrDropOptionsAsDesigned()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 1000, shards: 10);
            Guid item = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 0);
            GrowthPostgresTests.AssertCommitted(await RerollAsync(environment, player, 0, item, G_LowRolls), 1);

            // 강화, 착용, 해제, 잠금은 옵션을 건드리지 않는다. 옵션 열을 갱신 문장에서 빠뜨리면 여기서 0이 된다
            GrowthOperationOutcome enhanced = await environment.Equipment.EnhanceAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 1, item, 0, false);
            Assert.Equal(GrowthCodes.Outcome.SUCCEEDED, enhanced.Outcome);
            Assert.Equal((ATTACK, 50, CRIT, 20), Options(Find(enhanced, item)));
            GrowthOperationOutcome equipped = await environment.Equipment.EquipAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 2, item, false);
            Assert.Equal((ATTACK, 50, CRIT, 20), Options(Find(equipped, item)));
            GrowthOperationOutcome unequipped = await environment.Equipment.UnequipAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 3, item, false);
            GrowthOperationOutcome locked = await environment.Equipment.LockAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 4, item, true, false);
            GrowthOperationOutcome unlocked = await environment.Equipment.LockAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 5, item, false, false);
            Assert.Equal((ATTACK, 50, CRIT, 20), Options(Find(unequipped, item)));
            Assert.Equal((ATTACK, 50, CRIT, 20), Options(Find(locked, item)));
            EquipmentView last = Find(unlocked, item);
            Assert.Equal((ATTACK, 50, CRIT, 20, 1), (last.Option1Kind, last.Option1Value, last.Option2Kind, last.Option2Value,
                last.Enhancement));

            // 분해는 옵션과 함께 장비를 지우고 환급은 그대로다
            GrowthOperationOutcome dismantled = await environment.Equipment.DismantleAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 6, item, false);
            GrowthPostgresTests.AssertCommitted(dismantled, 7);
            Assert.Empty(dismantled.Growth!.Equipment);
            Assert.Equal(1, GrowthTestEnvironment.Quantity(dismantled.Growth, "item_boss_material_common"));
            Assert.Equal(1, dismantled.Growth.OptionRerollPity[WEAPON_SLOT]);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Migration0018_ExistingEquipmentHasNoOptionsAndConstraintsRejectBadValues()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync();
            Guid item = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 0);

            // 옵션 열을 적지 않은 삽입은 옵션 없음이다
            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM equipment_instances
                WHERE item_id = @ItemId AND option1_kind = 0 AND option1_value = 0 AND option2_kind = 0 AND option2_value = 0;
                """, new { ItemId = item }));

            await AssertCheckViolationAsync(environment, "UPDATE equipment_instances SET option1_kind = 5, option1_value = 50;");
            await AssertCheckViolationAsync(environment, "UPDATE equipment_instances SET option2_kind = -1, option2_value = 50;");
            await AssertCheckViolationAsync(environment, "UPDATE equipment_instances SET option1_kind = 1, option1_value = 10001;");
            await AssertCheckViolationAsync(environment, "UPDATE equipment_instances SET option2_kind = 0, option2_value = 10;");
            await AssertCheckViolationAsync(environment,
                "INSERT INTO option_reroll_pity (character_id, slot, misses) VALUES ('" + player.CharacterId + "', 6, 0);");
            await AssertCheckViolationAsync(environment,
                "INSERT INTO option_reroll_pity (character_id, slot, misses) VALUES ('" + player.CharacterId + "', 0, -1);");
        }

        private static Task<GrowthOperationOutcome> RerollAsync(GrowthTestEnvironment environment, GrowthPlayer player,
            long revision, Guid item, int[] rolls)
        {
            return environment.Equipment.RerollOptionsAsync(environment.TownId, Guid.NewGuid(), player.CharacterId, revision,
                item, rolls, false);
        }

        private static EquipmentView Find(GrowthOperationOutcome outcome, Guid itemId)
        {
            Assert.NotNull(outcome.Growth);
            return outcome.Growth.Equipment.Single(view => view.ItemId == itemId.ToString("D"));
        }

        private static (int, int, int, int) Options(EquipmentView view)
        {
            return (view.Option1Kind, view.Option1Value, view.Option2Kind, view.Option2Value);
        }

        private static Task SetOptionsAsync(GrowthTestEnvironment environment, Guid itemId, int kind1, int value1, int kind2,
            int value2)
        {
            return environment.ExecuteAsync(
                """
                UPDATE equipment_instances
                SET option1_kind = @Kind1, option1_value = @Value1, option2_kind = @Kind2, option2_value = @Value2, bound = true
                WHERE item_id = @ItemId;
                """, new { ItemId = itemId, Kind1 = kind1, Value1 = value1, Kind2 = kind2, Value2 = value2 });
        }

        private static async Task<(string Before, string After)> ReadHistoryAsync(GrowthTestEnvironment environment, Guid itemId,
            string eventKind)
        {
            await using NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<(string, string)>(
                """
                SELECT COALESCE(before_state::text, '{}'), COALESCE(after_state::text, '{}') FROM item_history
                WHERE item_id = @ItemId AND event_kind = @Kind;
                """, new { ItemId = itemId, Kind = eventKind });
        }

        private static async Task AssertCheckViolationAsync(GrowthTestEnvironment environment, string sql)
        {
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() => environment.ExecuteAsync(sql));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }
    }
}
