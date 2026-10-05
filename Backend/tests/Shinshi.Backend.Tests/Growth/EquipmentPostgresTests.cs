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
    /// <summary> 장비 제작, 강화, 계승, 착용, 분해를 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class EquipmentPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BOSS_COMMON    = "item_boss_material_common";
        private const string AUCTION_COMMON = "item_auction_material_common";

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Enhance_ConcurrentAttemptsWithOneAttemptBudget_DebitOnce()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 150, shards: 2);
            Guid item = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 0);

            GrowthOperationOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                environment.Equipment.EnhanceAsync(environment.TownId, Guid.NewGuid(), player.CharacterId, 0, item, 0, false)));

            Assert.Single(outcomes, outcome => outcome.Result!.Status == ContractCodes.OperationStatus.COMMITTED);
            Assert.All(outcomes.Where(outcome => outcome.Result!.Status == ContractCodes.OperationStatus.REJECTED),
                outcome => Assert.Contains(outcome.Result!.ReasonCode, new[]
                {
                    ContractCodes.RejectReason.STALE_REVISION, ContractCodes.RejectReason.INSUFFICIENT_ITEM
                }));
            CharacterGrowthSnapshot growth = await environment.ReadAsync(player);
            Assert.Equal((0L, 0L), (growth.Essence, growth.Shards));
            Assert.Equal(1, Assert.Single(growth.Equipment).Enhancement);

            // 같은 operationId 동시 재전송은 효과를 한 번만 적용한다
            GrowthPlayer other = await environment.CreatePlayerAsync(essence: 1000, shards: 10);
            Guid otherItem = await environment.InsertEquipmentAsync(other, "equip_weapon_common", 0);
            Guid operationId = Guid.NewGuid();
            GrowthOperationOutcome[] replays = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
                environment.Equipment.EnhanceAsync(environment.TownId, operationId, other.CharacterId, 0, otherItem, 0, false)));
            Assert.All(replays, outcome => Assert.Equal(replays[0].Result!.ReceiptId, outcome.Result!.ReceiptId));
            CharacterGrowthSnapshot otherGrowth = await environment.ReadAsync(other);
            Assert.Equal(850, otherGrowth.Essence);
            Assert.Equal(1, Assert.Single(otherGrowth.Equipment).Enhancement);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Enhance_PityKeyedBySlotAndStage_SharedAcrossItemsAndResetOnSuccess()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 100000, shards: 1000);
            Guid rare = await environment.InsertEquipmentAsync(player, "equip_weapon_rare", 5);
            Guid epic = await environment.InsertEquipmentAsync(player, "equip_weapon_epic", 5);
            Guid maxed = await environment.InsertEquipmentAsync(player, "equip_head_rare", 25);

            // +6 목표 기본 80%: roll 8000은 실패, 실패 보정 1회로 90%: 다른 장비라도 같은 부위, 단계면 roll 8999는 성공
            GrowthOperationOutcome failed = await environment.Equipment.EnhanceAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 0, rare, 8000, false);
            GrowthPostgresTests.AssertCommitted(failed, 1);
            Assert.Equal(GrowthCodes.Outcome.FAILED, failed.Outcome);
            EnhancementPityView pity = Assert.Single(failed.Growth!.EnhancementPity);
            Assert.Equal((0, 5, 1), (pity.Slot, pity.Stage, pity.Failures));
            Assert.Equal(100000 - 400, failed.Growth.Essence);
            Assert.Equal(1000 - 3, failed.Growth.Shards);

            GrowthOperationOutcome succeeded = await environment.Equipment.EnhanceAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 1, epic, 8999, false);
            Assert.Equal(GrowthCodes.Outcome.SUCCEEDED, succeeded.Outcome);
            Assert.Equal(epic.ToString("D"), succeeded.ItemId?.ToString("D"));
            Assert.Empty(succeeded.Growth!.EnhancementPity);
            Assert.Equal(6, succeeded.Growth.Equipment.Single(view => view.ItemId == epic.ToString("D")).Enhancement);
            Assert.True(succeeded.Growth.Equipment.Single(view => view.ItemId == epic.ToString("D")).Bound);

            GrowthOperationOutcome capped = await environment.Equipment.EnhanceAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 2, maxed, 0, false);
            GrowthPostgresTests.AssertRejected(capped, ContractCodes.RejectReason.INVALID_TARGET);
            Assert.Equal(100000 - 800, capped.Growth!.Essence);
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM item_history WHERE item_id = ANY(@Items);", new { Items = new[] { rare, epic } }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Craft_StorageAndMaterials_RejectWithoutConsumption()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(items: new Dictionary<string, long>
            {
                [BOSS_COMMON] = 10, [AUCTION_COMMON] = 1
            });

            GrowthOperationOutcome crafted = await CraftAsync(environment, player, 0, "equip_accessory_common");
            GrowthPostgresTests.AssertCommitted(crafted, 1);
            EquipmentView item = Assert.Single(crafted.Growth!.Equipment);
            Assert.Equal(crafted.ItemId?.ToString("D"), item.ItemId);
            Assert.Equal((0, false, false, GrowthCodes.EquipmentOrigin.CRAFT), (item.Enhancement, item.Equipped, item.Bound,
                item.Origin));
            Assert.Equal(7, GrowthTestEnvironment.Quantity(crafted.Growth, BOSS_COMMON));
            Assert.Equal(0, GrowthTestEnvironment.Quantity(crafted.Growth, AUCTION_COMMON));

            GrowthPostgresTests.AssertRejected(await CraftAsync(environment, player, 1, "equip_accessory_common"),
                ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            GrowthPostgresTests.AssertRejected(await CraftAsync(environment, player, 1, "equip_weapon_training"),
                ContractCodes.RejectReason.INVALID_TARGET);

            for (int index = 1; index < environment.Content.Equipment.StorageSlotsPerCharacter; ++index)
            {
                await environment.InsertEquipmentAsync(player, "equip_head_common", 0);
            }

            GrowthOperationOutcome full = await CraftAsync(environment, player, 1, "equip_weapon_common");
            GrowthPostgresTests.AssertRejected(full, ContractCodes.RejectReason.INVENTORY_FULL);
            Assert.Equal(7, GrowthTestEnvironment.Quantity(full.Growth!, BOSS_COMMON));
            Assert.Equal(100, full.Growth!.StorageUsed);
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM item_history WHERE event_kind = @Kind;", new { Kind = GrowthAssets.HISTORY_CRAFTED }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Inherit_CommitsLevelCostAndSourceTogetherOrNothing()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(essence: 1000);
            Guid source = await environment.InsertEquipmentAsync(player, "equip_weapon_rare", 17);
            Guid target = await environment.InsertEquipmentAsync(player, "equip_weapon_epic", 0);
            Guid equipped = await environment.InsertEquipmentAsync(player, "equip_head_rare", 20, equipped: true);
            Guid headTarget = await environment.InsertEquipmentAsync(player, "equip_head_epic", 0);
            await environment.ExecuteAsync(
                "INSERT INTO enhancement_pity (character_id, slot, stage, failures) VALUES (@CharacterId, 0, 17, 3);",
                new { player.CharacterId });

            GrowthOperationOutcome poor = await InheritAsync(environment, player, 0, source, target);
            GrowthPostgresTests.AssertRejected(poor, ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            Assert.Equal(4, poor.Growth!.Equipment.Length);
            Assert.Equal(1000, poor.Growth.Essence);

            GrowthPostgresTests.AssertRejected(await InheritAsync(environment, player, 0, equipped, headTarget),
                ContractCodes.RejectReason.BOUND_ITEM);
            GrowthPostgresTests.AssertRejected(await InheritAsync(environment, player, 0, source, headTarget),
                ContractCodes.RejectReason.INVALID_INHERITANCE);

            await environment.SeedAsync(player, 500, 0, new Dictionary<string, long>());
            GrowthOperationOutcome inherited = await InheritAsync(environment, player, 0, source, target);
            GrowthPostgresTests.AssertCommitted(inherited, 1);
            Assert.Equal(0, inherited.Growth!.Essence);
            Assert.DoesNotContain(inherited.Growth.Equipment, view => view.ItemId == source.ToString("D"));
            EquipmentView result = inherited.Growth.Equipment.Single(view => view.ItemId == target.ToString("D"));
            Assert.Equal((10, true, false), (result.Enhancement, result.Bound, result.Equipped));

            // 실패 보정은 결과 단계로 옮기지 않는다
            EnhancementPityView pity = Assert.Single(inherited.Growth.EnhancementPity);
            Assert.Equal((0, 17, 3), (pity.Slot, pity.Stage, pity.Failures));
            Assert.Equal(new[] { GrowthAssets.HISTORY_INHERIT_CONSUMED },
                (await QueryKindsAsync(environment, source)).ToArray());
            Assert.Equal(new[] { GrowthAssets.HISTORY_INHERITED }, (await QueryKindsAsync(environment, target)).ToArray());
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task EquipLockAndDismantle_RespectBindingAndRefundFloor()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync();
            Guid training = await environment.InsertEquipmentAsync(player, "equip_weapon_training", 0, equipped: true,
                origin: GrowthCodes.EquipmentOrigin.INITIAL_GRANT);
            Guid crafted = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 0);

            GrowthOperationOutcome equip = await ActAsync(environment, player, 0, crafted, "equip");
            GrowthPostgresTests.AssertCommitted(equip, 1);
            Assert.False(equip.Growth!.Equipment.Single(view => view.ItemId == training.ToString("D")).Equipped);
            EquipmentView worn = equip.Growth.Equipment.Single(view => view.ItemId == crafted.ToString("D"));
            Assert.True(worn.Equipped && worn.Bound);

            GrowthPostgresTests.AssertRejected(await ActAsync(environment, player, 1, training, "dismantle"),
                ContractCodes.RejectReason.BOUND_ITEM);
            GrowthPostgresTests.AssertRejected(await ActAsync(environment, player, 1, crafted, "dismantle"),
                ContractCodes.RejectReason.BOUND_ITEM);
            GrowthPostgresTests.AssertCommitted(await ActAsync(environment, player, 1, crafted, "unequip"), 2);
            GrowthPostgresTests.AssertCommitted(await environment.Equipment.LockAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 2, crafted, true, false), 3);
            GrowthPostgresTests.AssertRejected(await ActAsync(environment, player, 3, crafted, "dismantle"),
                ContractCodes.RejectReason.BOUND_ITEM);
            GrowthPostgresTests.AssertCommitted(await environment.Equipment.LockAsync(environment.TownId, Guid.NewGuid(),
                player.CharacterId, 3, crafted, false, false), 4);

            GrowthOperationOutcome dismantled = await ActAsync(environment, player, 4, crafted, "dismantle");
            GrowthPostgresTests.AssertCommitted(dismantled, 5);
            Assert.DoesNotContain(dismantled.Growth!.Equipment, view => view.ItemId == crafted.ToString("D"));
            Assert.Equal(1, GrowthTestEnvironment.Quantity(dismantled.Growth, BOSS_COMMON));
            Assert.Equal(GrowthAssets.HISTORY_DISMANTLED, (await QueryKindsAsync(environment, crafted)).Last());
            await environment.AssertReconciledAsync();
        }

        /// <summary> 분해 반환이 보스 재료 보관 상한(9999)을 넘으면 회수함으로 보내지 않고 장비를 남긴 채 INVENTORY_FULL이다 </summary>
        [PostgresFact]
        public async Task Dismantle_RefundOverStorageLimit_RejectsWithoutConsumingEquipment()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync(items: new Dictionary<string, long>
            {
                [BOSS_COMMON] = Shinshi.Data.Inbox.RewardStorageLimits.MATERIAL_STACK
            });
            Guid crafted = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 0);

            GrowthPostgresTests.AssertRejected(await ActAsync(environment, player, 0, crafted, "dismantle"),
                ContractCodes.RejectReason.INVENTORY_FULL);
            CharacterGrowthSnapshot growth = await environment.ReadAsync(player);
            Assert.Contains(growth.Equipment, view => view.ItemId == crafted.ToString("D"));
            Assert.Equal(Shinshi.Data.Inbox.RewardStorageLimits.MATERIAL_STACK,
                GrowthTestEnvironment.Quantity(growth, BOSS_COMMON));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM reward_inbox;"));

            // 한 칸 비우면 반환(1개)이 들어가 분해가 확정된다
            await environment.ExecuteAsync(
                "UPDATE account_items SET quantity = quantity - 1 WHERE account_id = @AccountId AND definition_id = @Id;",
                new { player.AccountId, Id = BOSS_COMMON });
            GrowthPostgresTests.AssertCommitted(await ActAsync(environment, player, 0, crafted, "dismantle"), 1);
        }

        /// <summary> 삭제한 캐릭터(0012 deleted_at)는 성장 조회가 없고 성장 쓰기는 INVALID_TARGET이다 </summary>
        [PostgresFact]
        public async Task DeletedCharacter_GrowthReadAndWrite_AreNotFound()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            GrowthPlayer player = await environment.CreatePlayerAsync();
            Guid crafted = await environment.InsertEquipmentAsync(player, "equip_weapon_common", 0);
            await environment.ExecuteAsync(
                """
                UPDATE characters SET deleted_at = clock_timestamp(), delete_operation_id = @OperationId
                WHERE character_id = @CharacterId;
                """, new { OperationId = Guid.NewGuid(), player.CharacterId });

            Assert.Null(await environment.Reader.ReadAsync(player.CharacterId));
            Assert.Equal(EGrowthAccess.NotFound,
                await environment.Reader.CheckTownAccessAsync(player.CharacterId, environment.TownId));
            GrowthPostgresTests.AssertRejected(await ActAsync(environment, player, 0, crafted, "dismantle"),
                ContractCodes.RejectReason.INVALID_TARGET);
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM equipment_instances WHERE item_id = @ItemId;", new { ItemId = crafted }));
        }

        private static Task<GrowthOperationOutcome> CraftAsync(GrowthTestEnvironment environment, GrowthPlayer player,
            long revision, string definitionId)
        {
            return environment.Equipment.CraftAsync(environment.TownId, Guid.NewGuid(), player.CharacterId, revision,
                definitionId, false);
        }

        private static Task<GrowthOperationOutcome> InheritAsync(GrowthTestEnvironment environment, GrowthPlayer player,
            long revision, Guid source, Guid target)
        {
            return environment.Equipment.InheritAsync(environment.TownId, Guid.NewGuid(), player.CharacterId, revision,
                source, target, false);
        }

        private static Task<GrowthOperationOutcome> ActAsync(GrowthTestEnvironment environment, GrowthPlayer player,
            long revision, Guid item, string action)
        {
            Guid operation = Guid.NewGuid();
            return action switch
            {
                "equip"   => environment.Equipment.EquipAsync(environment.TownId, operation, player.CharacterId, revision,
                    item, false),
                "unequip" => environment.Equipment.UnequipAsync(environment.TownId, operation, player.CharacterId, revision,
                    item, false),
                _         => environment.Equipment.DismantleAsync(environment.TownId, operation, player.CharacterId,
                    revision, item, false)
            };
        }

        private static async Task<IEnumerable<string>> QueryKindsAsync(GrowthTestEnvironment environment, Guid itemId)
        {
            await using Npgsql.NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync();
            return await Dapper.SqlMapper.QueryAsync<string>(connection,
                "SELECT event_kind FROM item_history WHERE item_id = @ItemId ORDER BY seq;", new { ItemId = itemId });
        }
    }
}
