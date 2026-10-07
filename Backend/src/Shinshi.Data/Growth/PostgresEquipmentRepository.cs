// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Growth
{
    /// <summary> 장비 제작, 강화, 계승, 착용과 해제, 잠금, 분해. 모든 장비 변경은 item_history에 이전, 이후 상태를 남긴다 </summary>
    public sealed class PostgresEquipmentRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CRAFT_KIND     = "growth.equipment.craft";
        public const string ENHANCE_KIND   = "growth.equipment.enhance";
        public const string INHERIT_KIND   = "growth.equipment.inherit";
        public const string EQUIP_KIND     = "growth.equipment.equip";
        public const string UNEQUIP_KIND   = "growth.equipment.unequip";
        public const string LOCK_KIND      = "growth.equipment.lock";
        public const string DISMANTLE_KIND = "growth.equipment.dismantle";

        private readonly GrowthOperationRunner _runner;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresEquipmentRepository(GrowthOperationRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _runner = runner;
        }

        /// <summary> 보스 재료만 써서 +0 장비를 만든다. 보관 칸이 없으면 재료를 쓰지 않고 INVENTORY_FULL이다 </summary>
        public Task<GrowthOperationOutcome> CraftAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, string definitionId, bool replayOnly, CancellationToken cancellationToken = default)
        {
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision, CRAFT_KIND,
                ContractCodes.EconomyReason.CRAFT, new { definitionId });
            return _runner.RunAsync(command, (context, token) => CraftEffectAsync(context, definitionId, token), replayOnly,
                cancellationToken);
        }

        /// <summary> 정기와 정련편을 쓰고 보정된 성공률로 한 단계 올린다. 실패하면 비용만 쓰고 단계를 유지하며 보정이 오른다 </summary>
        public Task<GrowthOperationOutcome> EnhanceAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, Guid itemId, int rollBasisPoints, bool replayOnly, CancellationToken cancellationToken = default)
        {
            if (!EnhancementRules.IsValidRoll(rollBasisPoints))
            {
                throw new ArgumentOutOfRangeException(nameof(rollBasisPoints));
            }

            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision,
                ENHANCE_KIND, ContractCodes.EconomyReason.ENHANCE, new { itemId = itemId.ToString("D") }, rollBasisPoints);
            return _runner.RunAsync(command, (context, token) => EnhanceEffectAsync(context, itemId, rollBasisPoints, token),
                replayOnly, cancellationToken);
        }

        /// <summary> 원본 단계를 같은 부위 +0 미착용 대상에 옮기고 원본을 지운다. 대상 단계의 보정 기록은 건드리지 않는다 </summary>
        public Task<GrowthOperationOutcome> InheritAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, Guid sourceItemId, Guid targetItemId, bool replayOnly,
            CancellationToken cancellationToken = default)
        {
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision,
                INHERIT_KIND, ContractCodes.EconomyReason.INHERIT, new
                {
                    sourceItemId = sourceItemId.ToString("D"),
                    targetItemId = targetItemId.ToString("D")
                });
            return _runner.RunAsync(command,
                (context, token) => InheritEffectAsync(context, sourceItemId, targetItemId, token), replayOnly,
                cancellationToken);
        }

        /// <summary> 장비를 착용한다. 같은 부위에 착용한 장비가 있으면 함께 내린다. 착용하면 캐릭터에 귀속된다 </summary>
        public Task<GrowthOperationOutcome> EquipAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, Guid itemId, bool replayOnly, CancellationToken cancellationToken = default)
        {
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision, EQUIP_KIND,
                ContractCodes.EconomyReason.CRAFT, new { itemId = itemId.ToString("D") });
            return _runner.RunAsync(command, (context, token) => EquipEffectAsync(context, itemId, token), replayOnly,
                cancellationToken);
        }

        /// <summary> 착용 장비를 내린다. 보관 칸이 없으면 INVENTORY_FULL이다 </summary>
        public Task<GrowthOperationOutcome> UnequipAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, Guid itemId, bool replayOnly, CancellationToken cancellationToken = default)
        {
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision,
                UNEQUIP_KIND, ContractCodes.EconomyReason.CRAFT, new { itemId = itemId.ToString("D") });
            return _runner.RunAsync(command, (context, token) => UnequipEffectAsync(context, itemId, token), replayOnly,
                cancellationToken);
        }

        /// <summary> 잠금을 켜거나 끈다. 잠긴 장비는 분해와 계승 원본으로 쓰지 못한다 </summary>
        public Task<GrowthOperationOutcome> LockAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, Guid itemId, bool locked, bool replayOnly, CancellationToken cancellationToken = default)
        {
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision, LOCK_KIND,
                ContractCodes.EconomyReason.CRAFT, new { itemId = itemId.ToString("D"), locked });
            return _runner.RunAsync(command, (context, token) => LockEffectAsync(context, itemId, locked, token), replayOnly,
                cancellationToken);
        }

        /// <summary> 착용, 잠금을 푼 제작 장비를 지우고 일반 보스 재료 일부를 돌려준다. 수련용과 지급품은 BOUND_ITEM이다 </summary>
        public Task<GrowthOperationOutcome> DismantleAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, Guid itemId, bool replayOnly, CancellationToken cancellationToken = default)
        {
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision,
                DISMANTLE_KIND, ContractCodes.EconomyReason.DISMANTLE, new { itemId = itemId.ToString("D") });
            return _runner.RunAsync(command, (context, token) => DismantleEffectAsync(context, itemId, token), replayOnly,
                cancellationToken);
        }

        private static async Task<GrowthEffectResult> CraftEffectAsync(GrowthEffectContext context, string definitionId,
            CancellationToken cancellationToken)
        {
            GrowthContent content = context.Content;
            if (!content.TryGetEquipment(definitionId, out GrowthContent.EquipmentDefinition definition)
                || !CraftRules.IsCraftable(definition))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (await context.CountStoredAsync(cancellationToken) >= content.Equipment.StorageSlotsPerCharacter)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVENTORY_FULL);
            }

            IReadOnlyList<(string DefinitionId, long Quantity)> cost = CraftRules.CraftCost(definition);
            await context.LockAccountItemsAsync(cost.Select(line => line.DefinitionId), cancellationToken);
            foreach ((string materialId, long quantity) in cost.OrderBy(line => line.DefinitionId, StringComparer.Ordinal))
            {
                if (!await context.TryDebitAsync(context.AccountItem(materialId), quantity, cancellationToken))
                {
                    return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
                }
            }

            // 일반 등급 제작 +0만 후속 거래 대상이다. 희귀 이상은 만들 때부터 거래 불가다
            EquipmentRecord item = new EquipmentRecord
            {
                ItemId = Guid.NewGuid(),
                AccountId = context.AccountId,
                CharacterId = context.CharacterId,
                DefinitionId = definition.DefinitionId,
                Slot = definition.Slot,
                Grade = definition.Grade,
                IsTraining = false,
                Origin = GrowthCodes.EquipmentOrigin.CRAFT,
                Bound = definition.Grade > 0
            };
            await context.InsertEquipmentAsync(item, GrowthAssets.HISTORY_CRAFTED, cancellationToken);
            return GrowthEffectResult.Committed(itemId: item.ItemId);
        }

        private static async Task<GrowthEffectResult> EnhanceEffectAsync(GrowthEffectContext context, Guid itemId,
            int rollBasisPoints, CancellationToken cancellationToken)
        {
            Dictionary<Guid, EquipmentRecord> items = await context.LockEquipmentAsync(new[] { itemId }, cancellationToken);
            if (!items.TryGetValue(itemId, out EquipmentRecord? item))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (item.EscrowKind != null)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.MARKET_ESCROWED);
            }
            if (!EnhancementRules.CanEnhance(item.Enhancement))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }

            GrowthContent content = context.Content;
            (long essence, long shards) = EnhancementRules.AttemptCost(content, item.Enhancement);
            if (!await context.TryDebitAsync(context.Essence(), essence, cancellationToken)
                || !await context.TryDebitAsync(context.Shards(), shards, cancellationToken))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            }

            // 보정 키는 캐릭터, 부위, 현재 단계다. 장비 ID나 등급을 넣지 않아 같은 부위의 다른 장비도 기록을 공유한다
            int failures = await context.Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                SELECT failures FROM enhancement_pity
                WHERE character_id = @CharacterId AND slot = @Slot AND stage = @Stage FOR UPDATE;
                """, new { context.CharacterId, item.Slot, Stage = item.Enhancement }, context.Transaction,
                cancellationToken: cancellationToken)) ?? 0;
            bool success = EnhancementRules.IsSuccess(
                EnhancementRules.SuccessBasisPoints(content, item.Enhancement, failures), rollBasisPoints);
            await context.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO enhancement_pity (character_id, slot, stage, failures)
                VALUES (@CharacterId, @Slot, @Stage, @Failures)
                ON CONFLICT (character_id, slot, stage) DO UPDATE SET failures = EXCLUDED.failures;
                """, new { context.CharacterId, item.Slot, Stage = item.Enhancement, Failures = success ? 0 : failures + 1 },
                context.Transaction, cancellationToken: cancellationToken));

            EquipmentRecord after = item.Clone();
            after.Bound = true;
            if (success)
            {
                ++after.Enhancement;
                await context.UpdateEquipmentAsync(item, after, GrowthAssets.HISTORY_ENHANCED, cancellationToken);
                return GrowthEffectResult.Committed(GrowthCodes.Outcome.SUCCEEDED, item.ItemId);
            }

            await context.UpdateEquipmentAsync(item, after, GrowthAssets.HISTORY_ENHANCE_FAILED, cancellationToken);
            return GrowthEffectResult.Committed(GrowthCodes.Outcome.FAILED, item.ItemId);
        }

        private static async Task<GrowthEffectResult> InheritEffectAsync(GrowthEffectContext context, Guid sourceItemId,
            Guid targetItemId, CancellationToken cancellationToken)
        {
            Dictionary<Guid, EquipmentRecord> items = await context.LockEquipmentAsync(new[] { sourceItemId, targetItemId },
                cancellationToken);
            GrowthContent content = context.Content;
            if ((sourceItemId == targetItemId) || !items.TryGetValue(sourceItemId, out EquipmentRecord? source)
                || !items.TryGetValue(targetItemId, out EquipmentRecord? target)
                || !content.TryGetEquipment(source.DefinitionId, out GrowthContent.EquipmentDefinition sourceDefinition)
                || !content.TryGetEquipment(target.DefinitionId, out GrowthContent.EquipmentDefinition targetDefinition))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if ((source.EscrowKind != null) || (target.EscrowKind != null))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.MARKET_ESCROWED);
            }

            // TODO(민성): 착용 중인 원본 거절과 계승 뒤 대상 자동 착용 여부는 기획 미정(docs/design/growth-economy.md 6장).
            // 확정 전에는 착용 원본을 BOUND_ITEM으로 거절하고 자동 착용하지 않는다
            InheritanceDecision decision = InheritanceRules.Evaluate(content,
                new InheritanceItem(source.ItemId, sourceDefinition, source.Enhancement, source.Equipped, source.Locked),
                new InheritanceItem(target.ItemId, targetDefinition, target.Enhancement, target.Equipped, target.Locked));
            if (!decision.IsAccepted)
            {
                return GrowthEffectResult.Rejected(decision.ReasonCode);
            }
            if (!await context.TryDebitAsync(context.Essence(), decision.EssenceCost, cancellationToken))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            }

            // 원본을 먼저 지워 대상 갱신과 같은 트랜잭션에서 단계 적용, 정기 차감, 원본 소모가 함께 확정된다
            await context.DeleteEquipmentAsync(source, GrowthAssets.HISTORY_INHERIT_CONSUMED, cancellationToken);
            EquipmentRecord after = target.Clone();
            after.Enhancement = decision.ResultLevel;
            after.Bound = true;
            await context.UpdateEquipmentAsync(target, after, GrowthAssets.HISTORY_INHERITED, cancellationToken);
            return GrowthEffectResult.Committed(GrowthCodes.Outcome.SUCCEEDED, target.ItemId);
        }

        private static async Task<GrowthEffectResult> EquipEffectAsync(GrowthEffectContext context, Guid itemId,
            CancellationToken cancellationToken)
        {
            Dictionary<Guid, EquipmentRecord> items = await context.LockEquipmentAsync(new[] { itemId }, cancellationToken);
            if (!items.TryGetValue(itemId, out EquipmentRecord? item))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (item.EscrowKind != null)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.MARKET_ESCROWED);
            }
            if (item.Equipped)
            {
                return GrowthEffectResult.Committed(itemId: item.ItemId, changesLoadout: false);
            }

            // 같은 부위의 착용 장비는 item_id 순서와 무관하게 잡히지만 같은 캐릭터 행 잠금 아래라 교착이 생기지 않는다
            EquipmentRecord? previous = await context.LockEquippedAsync(item.Slot, cancellationToken);
            if (previous != null)
            {
                EquipmentRecord lowered = previous.Clone();
                lowered.Equipped = false;
                await context.UpdateEquipmentAsync(previous, lowered, GrowthAssets.HISTORY_UNEQUIPPED, cancellationToken);
            }

            EquipmentRecord after = item.Clone();
            after.Equipped = true;
            after.Bound = true;
            await context.UpdateEquipmentAsync(item, after, GrowthAssets.HISTORY_EQUIPPED, cancellationToken);
            return GrowthEffectResult.Committed(itemId: item.ItemId);
        }

        private static async Task<GrowthEffectResult> UnequipEffectAsync(GrowthEffectContext context, Guid itemId,
            CancellationToken cancellationToken)
        {
            Dictionary<Guid, EquipmentRecord> items = await context.LockEquipmentAsync(new[] { itemId }, cancellationToken);
            if (!items.TryGetValue(itemId, out EquipmentRecord? item))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (item.EscrowKind != null)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.MARKET_ESCROWED);
            }
            if (!item.Equipped)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (await context.CountStoredAsync(cancellationToken) >= context.Content.Equipment.StorageSlotsPerCharacter)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVENTORY_FULL);
            }

            EquipmentRecord after = item.Clone();
            after.Equipped = false;
            await context.UpdateEquipmentAsync(item, after, GrowthAssets.HISTORY_UNEQUIPPED, cancellationToken);
            return GrowthEffectResult.Committed(itemId: item.ItemId);
        }

        private static async Task<GrowthEffectResult> LockEffectAsync(GrowthEffectContext context, Guid itemId, bool locked,
            CancellationToken cancellationToken)
        {
            Dictionary<Guid, EquipmentRecord> items = await context.LockEquipmentAsync(new[] { itemId }, cancellationToken);
            if (!items.TryGetValue(itemId, out EquipmentRecord? item))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (item.EscrowKind != null)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.MARKET_ESCROWED);
            }
            if (item.Locked == locked)
            {
                return GrowthEffectResult.Committed(itemId: item.ItemId, changesLoadout: false);
            }

            EquipmentRecord after = item.Clone();
            after.Locked = locked;
            await context.UpdateEquipmentAsync(item, after, GrowthAssets.HISTORY_LOCK_CHANGED, cancellationToken);
            return GrowthEffectResult.Committed(itemId: item.ItemId);
        }

        private static async Task<GrowthEffectResult> DismantleEffectAsync(GrowthEffectContext context, Guid itemId,
            CancellationToken cancellationToken)
        {
            Dictionary<Guid, EquipmentRecord> items = await context.LockEquipmentAsync(new[] { itemId }, cancellationToken);
            if (!items.TryGetValue(itemId, out EquipmentRecord? item)
                || !context.Content.TryGetEquipment(item.DefinitionId, out GrowthContent.EquipmentDefinition definition))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (item.EscrowKind != null)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.MARKET_ESCROWED);
            }
            if (!string.Equals(item.Origin, GrowthCodes.EquipmentOrigin.CRAFT, StringComparison.Ordinal) || item.IsTraining
                || item.Equipped || item.Locked)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.BOUND_ITEM);
            }

            // 분해는 플레이어가 고른 행위라 회수함으로 보내지 않는다. 반환이 보관 상한을 넘으면 장비를 지우지 않고
            // INVENTORY_FULL로 거절한다(docs/design/growth-economy.md 9장: 공간이 없으면 비용을 소모하지 않고 거절).
            // 회수함은 이미 확정된 획득(정산, 낙찰)만 받는다
            long refund = CraftRules.DismantleRefund(context.Content, definition);
            if (refund > 0)
            {
                await context.LockAccountItemsAsync(new[] { definition.BossMaterialId }, cancellationToken);
                long held = await context.ReadAccountItemQuantityAsync(definition.BossMaterialId, cancellationToken);
                long? limit = RewardStorageLimits.LimitFor(EconomyAsset.ACCOUNT_ITEM, definition.BossMaterialId);
                if (RewardStorageLimits.Fits(limit, held, refund) < refund)
                {
                    return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVENTORY_FULL);
                }
            }

            await context.DeleteEquipmentAsync(item, GrowthAssets.HISTORY_DISMANTLED, cancellationToken);
            if (refund > 0)
            {
                await context.CreditAsync(context.AccountItem(definition.BossMaterialId), refund, cancellationToken);
            }

            return GrowthEffectResult.Committed(itemId: item.ItemId);
        }
    }
}
