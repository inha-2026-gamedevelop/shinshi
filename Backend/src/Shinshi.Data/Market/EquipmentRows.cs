// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Market
{
    /// <summary> 경매장과 1:1 거래가 장비 행을 잠그고 소유, 보관 상태를 바꿀 때 쓰는 도우미. 이력 일련번호는 장비 행을 잠근 뒤 정한다 </summary>
    internal static class EquipmentRows
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string COLUMNS =
            """
            item_id AS "ItemId", account_id AS "AccountId", character_id AS "CharacterId",
            definition_id AS "DefinitionId", slot AS "Slot", grade AS "Grade", is_training AS "IsTraining",
            origin AS "Origin", enhancement AS "Enhancement", equipped AS "Equipped", locked AS "Locked", bound AS "Bound",
            escrow_kind AS "EscrowKind", escrow_ref AS "EscrowRef"
            """;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> item_id 오름차순으로 잠근다. 없는 ID는 결과에서 빠진다 </summary>
        public static async Task<Dictionary<Guid, EquipmentRecord>> LockAsync(DbConnection connection,
            DbTransaction transaction, IEnumerable<Guid> itemIds, CancellationToken cancellationToken)
        {
            Guid[] ids = itemIds.Distinct().ToArray();
            if (ids.Length == 0)
            {
                return new Dictionary<Guid, EquipmentRecord>();
            }

            IEnumerable<EquipmentRecord> rows = await connection.QueryAsync<EquipmentRecord>(new CommandDefinition(
                "SELECT " + COLUMNS + """
                 FROM equipment_instances WHERE item_id = ANY(@Ids) ORDER BY item_id FOR UPDATE;
                """, new { Ids = ids }, transaction, cancellationToken: cancellationToken));
            return rows.ToDictionary(row => row.ItemId);
        }

        /// <summary> 잠그지 않은 읽기. prepare의 형식 사전 검사처럼 확정 판정이 아닌 곳에만 쓴다 </summary>
        public static async Task<Dictionary<Guid, EquipmentRecord>> ReadAsync(DbConnection connection,
            IEnumerable<Guid> itemIds, CancellationToken cancellationToken)
        {
            Guid[] ids = itemIds.Distinct().ToArray();
            if (ids.Length == 0)
            {
                return new Dictionary<Guid, EquipmentRecord>();
            }

            IEnumerable<EquipmentRecord> rows = await connection.QueryAsync<EquipmentRecord>(new CommandDefinition(
                "SELECT " + COLUMNS + " FROM equipment_instances WHERE item_id = ANY(@Ids);", new { Ids = ids },
                cancellationToken: cancellationToken));
            return rows.ToDictionary(row => row.ItemId);
        }

        /// <summary> 캐릭터의 착용하지 않은 장비 수(보관 칸). 호출자가 캐릭터 행을 잠근 뒤 센다 </summary>
        public static Task<int> CountStoredAsync(DbConnection connection, DbTransaction transaction, Guid characterId,
            CancellationToken cancellationToken)
        {
            return connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT count(*)::integer FROM equipment_instances WHERE character_id = @CharacterId AND NOT equipped;",
                new { CharacterId = characterId }, transaction, cancellationToken: cancellationToken));
        }

        /// <summary> 보관 표시와 소유 캐릭터를 한 문장으로 바꾼다. 소유 계정과 캐릭터는 FK로 묶여 있어 함께 바꾼다 </summary>
        public static async Task UpdateOwnershipAsync(DbConnection connection, DbTransaction transaction,
            EquipmentRecord after, CancellationToken cancellationToken)
        {
            int updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE equipment_instances
                SET account_id = @AccountId, character_id = @CharacterId, escrow_kind = @EscrowKind, escrow_ref = @EscrowRef
                WHERE item_id = @ItemId;
                """, after, transaction, cancellationToken: cancellationToken));
            if (updated != 1)
            {
                throw new InvalidOperationException("Equipment ownership update did not affect exactly one row.");
            }
        }

        public static async Task AppendHistoryAsync(DbConnection connection, DbTransaction transaction,
            OperationExecutionContext context, PostgresItemHistoryWriter history, string eventKind, EquipmentRecord before,
            EquipmentRecord after, Guid ownerId, Guid actorId, Guid operationId, CancellationToken cancellationToken)
        {
            long sequence = await connection.QuerySingleAsync<long>(new CommandDefinition(
                "SELECT COALESCE(MAX(seq) + 1, 0) FROM item_history WHERE item_id = @ItemId;",
                new { before.ItemId }, transaction, cancellationToken: cancellationToken));
            await history.AppendAsync(context, new ItemHistoryEntry(before.ItemId, sequence, eventKind,
                before.ToHistoryJson(), after.ToHistoryJson(), ownerId, actorId, operationId), cancellationToken);
        }
    }
}
