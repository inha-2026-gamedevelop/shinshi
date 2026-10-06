// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Growth
{
    /// <summary> 출정 예약 트랜잭션에서 참가자 캐릭터의 착용 장비, 수련, 부적 조합을 JSON으로 고정한다. 호출자가 캐릭터 행을 먼저 잠근다 </summary>
    public static class CombatLoadoutBuilder
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 캐릭터별 CharacterCombatLoadout JSON. 표마다 한 문장으로 모든 참가자를 함께 읽는다 </summary>
        public static async Task<Dictionary<Guid, string>> BuildAsync(DbConnection connection, DbTransaction transaction,
            IReadOnlyCollection<Guid> characterIds, string contentVersion, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(characterIds);
            Guid[] ids = characterIds.Distinct().ToArray();
            using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                SELECT character_id AS "CharacterId", class_id AS "ClassId" FROM characters WHERE character_id = ANY(@Ids);
                SELECT character_id AS "CharacterId", slot AS "Slot", definition_id AS "DefinitionId", grade AS "Grade",
                    is_training AS "IsTraining", enhancement AS "Enhancement"
                FROM equipment_instances WHERE character_id = ANY(@Ids) AND equipped;
                SELECT character_id AS "CharacterId", attack AS "Attack", health AS "Health", defense AS "Defense",
                    crit AS "Crit", energy AS "Energy"
                FROM character_training WHERE character_id = ANY(@Ids);
                SELECT loadout.character_id AS "CharacterId", loadout.skill_id AS "SkillId", loadout.tier AS "Tier",
                    loadout.effect_id AS "EffectId", progress.level AS "Level"
                FROM talisman_loadout AS loadout
                JOIN talisman_progress AS progress
                    ON progress.character_id = loadout.character_id AND progress.effect_id = loadout.effect_id
                WHERE loadout.character_id = ANY(@Ids)
                ORDER BY loadout.character_id, loadout.skill_id, loadout.tier;
                """, new { Ids = ids }, transaction, cancellationToken: cancellationToken));
            Dictionary<Guid, int> classes = (await grid.ReadAsync<ClassRow>()).ToDictionary(row => row.CharacterId,
                row => row.ClassId);
            ILookup<Guid, EquipmentRow> equipment = (await grid.ReadAsync<EquipmentRow>()).ToLookup(row => row.CharacterId);
            Dictionary<Guid, PostgresGrowthReader.TrainingRow> training = (await grid.ReadAsync<PostgresGrowthReader.TrainingRow>()).ToDictionary(row => row.CharacterId);
            ILookup<Guid, TalismanRow> talismans = (await grid.ReadAsync<TalismanRow>()).ToLookup(row => row.CharacterId);

            Dictionary<Guid, string> snapshots = new Dictionary<Guid, string>();
            foreach ((Guid characterId, int classId) in classes)
            {
                CombatEquipmentEntry[] equipped = new CombatEquipmentEntry[GrowthContent.SLOT_COUNT];
                foreach (EquipmentRow row in equipment[characterId])
                {
                    equipped[row.Slot] = new CombatEquipmentEntry(row.DefinitionId, row.Grade, row.IsTraining, row.Enhancement);
                }

                int[] allocation = training.TryGetValue(characterId, out PostgresGrowthReader.TrainingRow? stats)
                    ? stats.ToArray() : new int[GrowthContent.TRAINING_STAT_COUNT];
                CombatTalismanEntry[] loadout = talismans[characterId]
                    .Select(row => new CombatTalismanEntry(row.SkillId, row.Tier, row.EffectId, row.Level)).ToArray();
                snapshots[characterId] = JsonConvert.SerializeObject(new CharacterCombatLoadout(classId, equipped, allocation,
                    loadout, contentVersion), ContractJson.CreateSettings());
            }

            return snapshots;
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class ClassRow
        {
            public Guid CharacterId { get; set; }
            public int  ClassId     { get; set; }
        }

        private sealed class EquipmentRow
        {
            public Guid   CharacterId  { get; set; }
            public int    Slot         { get; set; }
            public string DefinitionId { get; set; } = string.Empty;
            public int    Grade        { get; set; }
            public bool   IsTraining   { get; set; }
            public int    Enhancement  { get; set; }
        }

        private sealed class TalismanRow
        {
            public Guid   CharacterId { get; set; }
            public string SkillId     { get; set; } = string.Empty;
            public int    Tier        { get; set; }
            public string EffectId    { get; set; } = string.Empty;
            public int    Level       { get; set; }
        }
    }
}
