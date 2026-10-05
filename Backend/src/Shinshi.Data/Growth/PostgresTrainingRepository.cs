// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 정기 수련 투자와 재분배. 투자 정기는 반환하지 않으며 재분배는 총 투자를 유지한 채 배분만 바꾼다 </summary>
    public sealed class PostgresTrainingRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string TRAIN_KIND  = "growth.training.invest";
        public const string RESPEC_KIND = "growth.training.respec";

        private readonly GrowthOperationRunner _runner;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresTrainingRepository(GrowthOperationRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _runner = runner;
        }

        /// <summary> 한 항목에 steps단계를 투자한다. 항목 또는 총 상한을 넘으면 비용 없이 INVALID_TARGET이다 </summary>
        public Task<GrowthOperationOutcome> TrainAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, int stat, int steps, bool replayOnly, CancellationToken cancellationToken = default)
        {
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision, TRAIN_KIND,
                ContractCodes.EconomyReason.TRAINING, new { stat, steps });
            return _runner.RunAsync(command, (context, token) => TrainEffectAsync(context, stat, steps, token), replayOnly,
                cancellationToken);
        }

        /// <summary> 다섯 항목 배분을 바꾼다. 총 투자 0이거나 배분이 같으면 비용을 받지 않는다 </summary>
        public Task<GrowthOperationOutcome> RespecAsync(Guid townInstanceId, Guid operationId, Guid characterId,
            long expectedRevision, int[] allocation, bool replayOnly, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocation);
            GrowthCommand command = new GrowthCommand(townInstanceId, operationId, characterId, expectedRevision,
                RESPEC_KIND, ContractCodes.EconomyReason.TRAINING, new { allocation });
            return _runner.RunAsync(command, (context, token) => RespecEffectAsync(context, allocation, token), replayOnly,
                cancellationToken);
        }

        private static async Task<GrowthEffectResult> TrainEffectAsync(GrowthEffectContext context, int stat, int steps,
            CancellationToken cancellationToken)
        {
            int[] current = await LockTrainingAsync(context, cancellationToken);
            int reason = TrainingRules.CheckInvest(context.Content, current, stat, steps);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return GrowthEffectResult.Rejected(reason);
            }

            long cost = TrainingRules.InvestCost(context.Content, current.Sum(), steps);
            if (!await context.TryDebitAsync(context.Essence(), cost, cancellationToken))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            }

            current[stat] += steps;
            await SaveTrainingAsync(context, current, cancellationToken);
            return GrowthEffectResult.Committed();
        }

        private static async Task<GrowthEffectResult> RespecEffectAsync(GrowthEffectContext context, int[] allocation,
            CancellationToken cancellationToken)
        {
            int[] current = await LockTrainingAsync(context, cancellationToken);
            int reason = TrainingRules.CheckRespec(context.Content, current, allocation);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return GrowthEffectResult.Rejected(reason);
            }
            if (current.SequenceEqual(allocation))
            {
                return GrowthEffectResult.Committed(changesLoadout: false);
            }

            long cost = TrainingRules.RespecCost(context.Content, current.Sum());
            if (!await context.TryDebitAsync(context.Essence(), cost, cancellationToken))
            {
                return GrowthEffectResult.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            }

            await SaveTrainingAsync(context, allocation, cancellationToken);
            return GrowthEffectResult.Committed();
        }

        private static async Task<int[]> LockTrainingAsync(GrowthEffectContext context, CancellationToken cancellationToken)
        {
            PostgresGrowthReader.TrainingRow? row = await context.Connection.QuerySingleOrDefaultAsync<PostgresGrowthReader.TrainingRow>(
                new CommandDefinition(
                    """
                    SELECT attack AS "Attack", health AS "Health", defense AS "Defense", crit AS "Crit", energy AS "Energy"
                    FROM character_training WHERE character_id = @CharacterId FOR UPDATE;
                    """, new { context.CharacterId }, context.Transaction, cancellationToken: cancellationToken));
            return row?.ToArray() ?? new int[GrowthContent.TRAINING_STAT_COUNT];
        }

        private static Task SaveTrainingAsync(GrowthEffectContext context, int[] allocation, CancellationToken cancellationToken)
        {
            return context.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO character_training (character_id, attack, health, defense, crit, energy)
                VALUES (@CharacterId, @Attack, @Health, @Defense, @Crit, @Energy)
                ON CONFLICT (character_id) DO UPDATE
                    SET attack = EXCLUDED.attack, health = EXCLUDED.health, defense = EXCLUDED.defense,
                        crit = EXCLUDED.crit, energy = EXCLUDED.energy;
                """, PostgresGrowthReader.TrainingRow.FromArray(context.CharacterId, allocation), context.Transaction,
                cancellationToken: cancellationToken));
        }
    }
}
