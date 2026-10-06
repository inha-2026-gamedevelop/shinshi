// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 캐릭터 성장 상태 조회. 한 번의 왕복에 표마다 한 문장씩 읽어 반복 조회를 하지 않는다 </summary>
    public sealed class PostgresGrowthReader
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly NpgsqlDataSource _dataSource;
        private readonly GrowthContent _content;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresGrowthReader(NpgsqlDataSource dataSource, GrowthContent content)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(content);
            _dataSource = dataSource;
            _content    = content;
        }

        /// <summary> 캐릭터가 있고 그 계정의 살아 있는 접속 임대가 이 마을 인스턴스에 있는지 본다 </summary>
        public async Task<EGrowthAccess> CheckTownAccessAsync(Guid characterId, Guid townInstanceId,
            CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            bool? inTown = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM account_leases AS lease
                    WHERE lease.account_id = character.account_id AND lease.instance_id = @Town AND lease.role = @TownRole
                        AND lease.expires_at > clock_timestamp())
                FROM characters AS character WHERE character.character_id = @CharacterId AND character.deleted_at IS NULL;
                """, new { CharacterId = characterId, Town = townInstanceId, TownRole = ContractCodes.ServerRole.TOWN },
                cancellationToken: cancellationToken));
            if (!inTown.HasValue)
            {
                return EGrowthAccess.NotFound;
            }

            return inTown.Value ? EGrowthAccess.Allowed : EGrowthAccess.WrongLocation;
        }

        /// <summary> 성장 상태 전체. 캐릭터가 없으면 null </summary>
        public async Task<CharacterGrowthSnapshot?> ReadAsync(Guid characterId, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                SELECT character_id AS "CharacterId", account_id AS "AccountId", class_id AS "ClassId",
                    loadout_revision AS "LoadoutRevision", essence AS "Essence", shards AS "Shards",
                    clock_timestamp() AS "ServerTime"
                FROM characters WHERE character_id = @CharacterId AND deleted_at IS NULL;
                SELECT attack AS "Attack", health AS "Health", defense AS "Defense", crit AS "Crit", energy AS "Energy"
                FROM character_training WHERE character_id = @CharacterId;
                SELECT item_id AS "ItemId", definition_id AS "DefinitionId", slot AS "Slot", grade AS "Grade",
                    is_training AS "IsTraining", origin AS "Origin", enhancement AS "Enhancement", equipped AS "Equipped",
                    locked AS "Locked", bound AS "Bound"
                FROM equipment_instances WHERE character_id = @CharacterId ORDER BY slot, item_id;
                SELECT slot AS "Slot", stage AS "Stage", failures AS "Failures"
                FROM enhancement_pity WHERE character_id = @CharacterId AND failures > 0 ORDER BY slot, stage;
                SELECT effect_id AS "EffectId", level AS "Level", register_failures AS "RegisterFailures"
                FROM talisman_progress WHERE character_id = @CharacterId ORDER BY effect_id;
                SELECT skill_id AS "SkillId", tier AS "Tier", effect_id AS "EffectId"
                FROM talisman_loadout WHERE character_id = @CharacterId ORDER BY skill_id, tier;
                SELECT item.definition_id AS "DefinitionId", item.binding_kind AS "BindingKind",
                    item.quantity AS "Quantity", item.reserved AS "Reserved"
                FROM account_items AS item JOIN characters AS character ON character.account_id = item.account_id
                WHERE character.character_id = @CharacterId AND character.deleted_at IS NULL AND item.quantity > 0
                ORDER BY item.definition_id, item.binding_kind;
                """, new { CharacterId = characterId }, cancellationToken: cancellationToken));

            CharacterRow? character = await grid.ReadSingleOrDefaultAsync<CharacterRow>();
            TrainingRow? training = await grid.ReadSingleOrDefaultAsync<TrainingRow>();
            List<EquipmentRecord> equipment = (await grid.ReadAsync<EquipmentRecord>()).ToList();
            List<PityRow> pity = (await grid.ReadAsync<PityRow>()).ToList();
            List<ProgressRow> progress = (await grid.ReadAsync<ProgressRow>()).ToList();
            List<LoadoutRow> loadout = (await grid.ReadAsync<LoadoutRow>()).ToList();
            List<ItemRow> items = (await grid.ReadAsync<ItemRow>()).ToList();
            if (character == null)
            {
                return null;
            }

            int[] allocation = training?.ToArray() ?? new int[GrowthContent.TRAINING_STAT_COUNT];
            return new CharacterGrowthSnapshot(character.CharacterId.ToString("D"), character.ClassId,
                character.LoadoutRevision, character.Essence, character.Shards, allocation,
                equipment.Select(ToView).ToArray(),
                pity.Select(row => new EnhancementPityView(row.Slot, row.Stage, row.Failures)).ToArray(),
                progress.Select(row => new TalismanProgressView(row.EffectId, row.Level, row.RegisterFailures)).ToArray(),
                loadout.Select(row => new TalismanLoadoutEntry(row.SkillId, row.Tier, row.EffectId)).ToArray(),
                items.Select(row => new GrowthItemView(row.DefinitionId, row.BindingKind, row.Quantity, row.Reserved)).ToArray(),
                equipment.Count(row => !row.Equipped), _content.Equipment.StorageSlotsPerCharacter, _content.ContentVersion,
                new DateTimeOffset(DateTime.SpecifyKind(character.ServerTime, DateTimeKind.Utc)).ToUnixTimeMilliseconds());
        }

        private static EquipmentView ToView(EquipmentRecord row)
        {
            return new EquipmentView(row.ItemId.ToString("D"), row.DefinitionId, row.Slot, row.Grade, row.IsTraining,
                row.Origin, row.Enhancement, row.Equipped, row.Locked, row.Bound);
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class CharacterRow
        {
            public Guid     CharacterId     { get; set; }
            public Guid     AccountId       { get; set; }
            public int      ClassId         { get; set; }
            public long     LoadoutRevision { get; set; }
            public long     Essence         { get; set; }
            public long     Shards          { get; set; }
            public DateTime ServerTime      { get; set; }
        }

        /// <summary> character_training 한 행, ETrainingStat 순서 배열과 서로 바꾼다 </summary>
        public sealed class TrainingRow
        {
            public Guid CharacterId { get; set; }
            public int Attack  { get; set; }
            public int Health  { get; set; }
            public int Defense { get; set; }
            public int Crit    { get; set; }
            public int Energy  { get; set; }

            public int[] ToArray()
            {
                return new[] { Attack, Health, Defense, Crit, Energy };
            }

            public static TrainingRow FromArray(Guid characterId, int[] allocation)
            {
                ArgumentNullException.ThrowIfNull(allocation);
                if (allocation.Length != GrowthContent.TRAINING_STAT_COUNT)
                {
                    throw new ArgumentException("Training allocation must have one value per stat.", nameof(allocation));
                }

                int index = 0;
                return new TrainingRow
                {
                    CharacterId = characterId,
                    Attack = allocation[index++],
                    Health = allocation[index++],
                    Defense = allocation[index++],
                    Crit = allocation[index++],
                    Energy = allocation[index]
                };
            }
        }

        private sealed class PityRow
        {
            public int Slot     { get; set; }
            public int Stage    { get; set; }
            public int Failures { get; set; }
        }

        private sealed class ProgressRow
        {
            public string EffectId         { get; set; } = string.Empty;
            public int    Level            { get; set; }
            public int    RegisterFailures { get; set; }
        }

        private sealed class LoadoutRow
        {
            public string SkillId  { get; set; } = string.Empty;
            public int    Tier     { get; set; }
            public string EffectId { get; set; } = string.Empty;
        }

        private sealed class ItemRow
        {
            public string DefinitionId { get; set; } = string.Empty;
            public string BindingKind  { get; set; } = string.Empty;
            public long   Quantity     { get; set; }
            public long   Reserved     { get; set; }
        }
    }
}
