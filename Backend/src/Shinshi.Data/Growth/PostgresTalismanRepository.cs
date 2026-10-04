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

namespace Shinshi.Data.Growth
{
    /// <summary> 부적 등록, 확정 강화, 조합 전체 교체, 카드 분해. 등록 확률 입력은 마을 서버가 뽑고 API는 저장된 보정과 비교만 한다 </summary>
    public sealed class PostgresTalismanRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string REGISTER_KIND    = "growth.talisman.register";
        public const string UPGRADE_KIND     = "growth.talisman.upgrade";
        public const string LOADOUT_KIND     = "growth.talisman.loadout";
        public const string DISASSEMBLE_KIND = "growth.talisman.disassemble";

        private readonly GrowthOperationRunner _runner;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresTalismanRepository(GrowthOperationRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _runner = runner;
        }

        /// <summary> 같은 효과 카드 1장과 정기를 쓰고 보정된 성공률로 판정한다. 실패해도 비용은 돌아오지 않고 실패 보정만 오른다 </summary>
        public Task<GrowthOperationOutcome> RegisterAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, string effectId, int rollBasisPoints, bool replayOnly,
            CancellationToken cancellationToken = default)
        {
            if (!EnhancementRules.IsValidRoll(rollBasisPoints))
            {
                throw new ArgumentOutOfRangeException(nameof(rollBasisPoints));
            }

            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision,
                REGISTER_KIND, ContractCodes.EconomyReason.TALISMAN_REGISTER, new { effectId }, rollBasisPoints);
            return _runner.RunAsync(command, (context, token) => RegisterEffectAsync(context, effectId, rollBasisPoints, token),
                replayOnly, cancellationToken);
        }

        /// <summary> 등록한 효과를 한 레벨 확정 강화한다. 레벨 5나 미등록 효과는 비용 없이 거절한다 </summary>
        public Task<GrowthOperationOutcome> UpgradeAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, string effectId, bool replayOnly, CancellationToken cancellationToken = default)
        {
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision,
                UPGRADE_KIND, ContractCodes.EconomyReason.TALISMAN_UPGRADE, new { effectId });
            return _runner.RunAsync(command, (context, token) => UpgradeEffectAsync(context, effectId, token), replayOnly,
                cancellationToken);
        }

        /// <summary> 조합 전체를 요청 목록으로 바꾼다. 비용은 없다 </summary>
        public Task<GrowthOperationOutcome> ReplaceLoadoutAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, IReadOnlyList<TalismanLoadoutEntry> entries, bool replayOnly,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entries);
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision,
                LOADOUT_KIND, ContractCodes.EconomyReason.TALISMAN_UPGRADE, new
                {
                    entries = entries.Select(entry => new { skillId = entry.SkillId, tier = entry.Tier, effectId = entry.EffectId })
                });
            return _runner.RunAsync(command, (context, token) => LoadoutEffectAsync(context, entries, token), replayOnly,
                cancellationToken);
        }

        /// <summary> 같은 직업의 카드 quantity장을 소모하고 장당 정기를 준다. 다른 직업 카드는 INVALID_TARGET이다 </summary>
        public Task<GrowthOperationOutcome> DisassembleAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, string effectId, int quantity, bool replayOnly, CancellationToken cancellationToken = default)
        {
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity));
            }

            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision,
                DISASSEMBLE_KIND, ContractCodes.EconomyReason.DISMANTLE, new { effectId, quantity });
            return _runner.RunAsync(command, (context, token) => DisassembleEffectAsync(context, effectId, quantity, token),
                replayOnly, cancellationToken);
        }

        private static async Task<GrowthEffectResult> RegisterEffectAsync(GrowthEffectContext context, string effectId,
            int rollBasisPoints, CancellationToken cancellationToken)
        {
            if (!IsOwnClassEffect(context, effectId))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }

            ProgressRow? progress = await LockProgressAsync(context, effectId, cancellationToken);
            if ((progress != null) && (progress.Level > 0))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }

            GrowthContent content = context.Content;
            if (!await context.TryDebitAsync(context.Essence(), content.Growth.RegisterEssenceCost, cancellationToken)
                || !await context.TryDebitAsync(context.AccountItem(effectId), content.Growth.RegisterCardCost, cancellationToken))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            }

            int failures = progress?.RegisterFailures ?? 0;
            bool success = EnhancementRules.IsSuccess(TalismanRules.RegisterSuccessBasisPoints(content, failures),
                rollBasisPoints);
            await context.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO talisman_progress (character_id, effect_id, level, register_failures)
                VALUES (@CharacterId, @EffectId, @Level, @Failures)
                ON CONFLICT (character_id, effect_id) DO UPDATE
                    SET level = EXCLUDED.level, register_failures = EXCLUDED.register_failures, updated_at = clock_timestamp();
                """, new
                {
                    context.CharacterId,
                    EffectId = effectId,
                    Level = success ? 1 : 0,
                    Failures = success ? 0 : failures + 1
                }, context.Transaction, cancellationToken: cancellationToken));
            return GrowthEffectResult.Committed(success ? GrowthCodes.Outcome.SUCCEEDED : GrowthCodes.Outcome.FAILED);
        }

        private static async Task<GrowthEffectResult> UpgradeEffectAsync(GrowthEffectContext context, string effectId,
            CancellationToken cancellationToken)
        {
            if (!IsOwnClassEffect(context, effectId))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }

            ProgressRow? progress = await LockProgressAsync(context, effectId, cancellationToken);
            if ((progress == null) || (progress.Level < 1))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.MISSING_UNLOCK);
            }
            if (progress.Level >= GrowthContent.MAX_EFFECT_LEVEL)
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }

            int targetLevel = progress.Level + 1;
            GrowthContent.EffectUpgradeCost cost = context.Content.GetEffectUpgradeCost(targetLevel);
            if (!await context.TryDebitAsync(context.Essence(), cost.EssenceCost, cancellationToken)
                || !await context.TryDebitAsync(context.AccountItem(effectId), cost.CardCost, cancellationToken))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            }

            await context.Connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE talisman_progress SET level = @Level, updated_at = clock_timestamp()
                WHERE character_id = @CharacterId AND effect_id = @EffectId;
                """, new { context.CharacterId, EffectId = effectId, Level = targetLevel }, context.Transaction,
                cancellationToken: cancellationToken));
            return GrowthEffectResult.Committed(GrowthCodes.Outcome.SUCCEEDED);
        }

        private static async Task<GrowthEffectResult> LoadoutEffectAsync(GrowthEffectContext context,
            IReadOnlyList<TalismanLoadoutEntry> entries, CancellationToken cancellationToken)
        {
            Dictionary<string, int> levels = (await context.Connection.QueryAsync<ProgressRow>(new CommandDefinition(
                """
                SELECT effect_id AS "EffectId", level AS "Level", register_failures AS "RegisterFailures"
                FROM talisman_progress WHERE character_id = @CharacterId AND effect_id = ANY(@EffectIds);
                """, new { context.CharacterId, EffectIds = entries.Select(entry => entry.EffectId).ToArray() },
                context.Transaction, cancellationToken: cancellationToken)))
                .ToDictionary(row => row.EffectId, row => row.Level, StringComparer.Ordinal);
            int reason = TalismanRules.CheckLoadout(context.Content, context.ClassId, entries, levels);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return GrowthEffectResult.Rejected(reason);
            }

            await context.Connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM talisman_loadout WHERE character_id = @CharacterId;
                INSERT INTO talisman_loadout (character_id, skill_id, tier, effect_id)
                SELECT @CharacterId, entry.skill_id, entry.tier, entry.effect_id
                FROM unnest(@Skills::text[], @Tiers::integer[], @Effects::text[]) AS entry(skill_id, tier, effect_id);
                """, new
                {
                    context.CharacterId,
                    Skills = entries.Select(entry => entry.SkillId).ToArray(),
                    Tiers = entries.Select(entry => entry.Tier).ToArray(),
                    Effects = entries.Select(entry => entry.EffectId).ToArray()
                }, context.Transaction, cancellationToken: cancellationToken));
            return GrowthEffectResult.Committed();
        }

        private static async Task<GrowthEffectResult> DisassembleEffectAsync(GrowthEffectContext context, string effectId,
            int quantity, CancellationToken cancellationToken)
        {
            // TODO(민성): 다른 직업 카드 분해 허용 여부는 기획 미정(docs/design/growth-economy.md 7장). 확정 전 거절한다
            if (!IsOwnClassEffect(context, effectId))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }

            if (!await context.TryDebitAsync(context.AccountItem(effectId), quantity, cancellationToken))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            }

            await context.CreditAsync(context.Essence(), context.Content.Growth.CardDisassembleEssence * quantity,
                cancellationToken);
            return GrowthEffectResult.Committed();
        }

        private static bool IsOwnClassEffect(GrowthEffectContext context, string effectId)
        {
            return context.Content.TryGetEffect(effectId, out GrowthContent.TalismanEffect effect)
                && (effect.PlayerClass == context.ClassId);
        }

        private static Task<ProgressRow?> LockProgressAsync(GrowthEffectContext context, string effectId,
            CancellationToken cancellationToken)
        {
            return context.Connection.QuerySingleOrDefaultAsync<ProgressRow?>(new CommandDefinition(
                """
                SELECT effect_id AS "EffectId", level AS "Level", register_failures AS "RegisterFailures"
                FROM talisman_progress WHERE character_id = @CharacterId AND effect_id = @EffectId FOR UPDATE;
                """, new { context.CharacterId, EffectId = effectId }, context.Transaction,
                cancellationToken: cancellationToken));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class ProgressRow
        {
            public string EffectId         { get; set; } = string.Empty;
            public int    Level            { get; set; }
            public int    RegisterFailures { get; set; }
        }
    }
}
