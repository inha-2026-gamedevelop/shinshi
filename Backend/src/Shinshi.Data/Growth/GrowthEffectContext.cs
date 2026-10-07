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
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Growth
{
    /// <summary> 성장 작업 효과가 쓰는 같은 트랜잭션의 잔액 변경, 원장, 장비 이력 도우미. 잔액을 바꾸는 모든 경로가 여기서 원장 한 줄을 쓴다 </summary>
    public sealed class GrowthEffectContext
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SOURCE_TYPE = "GrowthOperation";

        private const string EQUIPMENT_COLUMNS =
            """
            item_id AS "ItemId", account_id AS "AccountId", character_id AS "CharacterId",
            definition_id AS "DefinitionId", slot AS "Slot", grade AS "Grade", is_training AS "IsTraining",
            origin AS "Origin", enhancement AS "Enhancement", equipped AS "Equipped", locked AS "Locked", bound AS "Bound",
            escrow_kind AS "EscrowKind"
            """;

        private readonly IEconomyLedgerEntryWriter _ledger;
        private readonly PostgresItemHistoryWriter _history;

        // 이 작업에서 쓴 원장 줄 수. entry_key의 작업 내 일련번호다
        private int _ledgerSequence;

        /****************************************
        *              Properties
        ****************************************/

        public OperationExecutionContext Operation   { get; }
        public DbConnection              Connection  { get; }
        public DbTransaction             Transaction { get; }
        public GrowthContent             Content     { get; }
        public Guid                      ActorId     { get; }
        public string                    ActorKind   { get; }
        public Guid                      OperationId { get; }
        public Guid                      AccountId   { get; }
        public Guid                      CharacterId { get; }
        public int                       ClassId     { get; }

        // 이 작업의 원장 사유(ContractCodes.EconomyReason)
        public int ReasonCode { get; }

        /****************************************
        *                Methods
        ****************************************/

        public GrowthEffectContext(OperationExecutionContext operation, GrowthContent content,
            IEconomyLedgerEntryWriter ledger, PostgresItemHistoryWriter history, Guid actorId, string actorKind,
            Guid operationId, Guid accountId, Guid characterId, int classId, int reasonCode)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(ledger);
            ArgumentNullException.ThrowIfNull(history);
            (Connection, Transaction) = Sessions.SessionDb.Require(operation);
            Operation   = operation;
            Content     = content;
            _ledger     = ledger;
            _history    = history;
            ActorId     = actorId;
            ActorKind   = actorKind;
            OperationId = operationId;
            AccountId   = accountId;
            CharacterId = characterId;
            ClassId     = classId;
            ReasonCode  = reasonCode;
        }

        public EconomyAsset Essence()
        {
            return EconomyAsset.Essence(AccountId, CharacterId);
        }

        public EconomyAsset Shards()
        {
            return EconomyAsset.Shards(AccountId, CharacterId);
        }

        public EconomyAsset AccountItem(string definitionId)
        {
            return EconomyAsset.AccountItem(AccountId, definitionId, GrowthAssets.ACCOUNT_BINDING);
        }

        /// <summary> 정확히 amount를 빼고 원장을 쓴다. 모자라면 아무것도 바꾸지 않고 false다 </summary>
        public async Task<bool> TryDebitAsync(EconomyAsset asset, long amount, CancellationToken cancellationToken)
        {
            if (amount == 0)
            {
                return true;
            }

            long? after = await EconomyDebits.TryDebitExactAsync(Connection, Transaction, asset, amount, cancellationToken);
            if (!after.HasValue)
            {
                return false;
            }

            await AppendLedgerAsync(asset, -amount, after.Value, cancellationToken);
            return true;
        }

        /// <summary> amount를 더하고 원장을 쓴다. 지갑, 아이템 행이 없으면 만든다 </summary>
        public async Task CreditAsync(EconomyAsset asset, long amount, CancellationToken cancellationToken)
        {
            if (amount == 0)
            {
                return;
            }

            long after = await RewardBalances.CreditAsync(Connection, Transaction, asset, amount, cancellationToken);
            await AppendLedgerAsync(asset, amount, after, cancellationToken);
        }

        /// <summary> 이 계정의 account_items 행을 정의 ID 순서로 잠근다. 여러 줄을 바꾸는 작업이 서로 교착하지 않게 차감 전에 부른다 </summary>
        public async Task LockAccountItemsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken)
        {
            string[] ids = definitionIds.Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length == 0)
            {
                return;
            }

            await Connection.QueryAsync<string>(new CommandDefinition(
                """
                SELECT definition_id FROM account_items
                WHERE account_id = @AccountId AND definition_id = ANY(@Ids) AND binding_kind = @Binding
                ORDER BY definition_id FOR UPDATE;
                """, new { AccountId, Ids = ids, Binding = GrowthAssets.ACCOUNT_BINDING }, Transaction,
                cancellationToken: cancellationToken));
        }

        /// <summary> 이 계정의 계정 귀속 account_items 보유량. 행이 없으면 0이다. LockAccountItemsAsync 뒤에 부른다 </summary>
        public Task<long> ReadAccountItemQuantityAsync(string definitionId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(definitionId);
            return Connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                SELECT COALESCE((SELECT quantity FROM account_items
                    WHERE account_id = @AccountId AND definition_id = @Id AND binding_kind = @Binding), 0);
                """, new { AccountId, Id = definitionId, Binding = GrowthAssets.ACCOUNT_BINDING }, Transaction,
                cancellationToken: cancellationToken));
        }

        /// <summary> 이 캐릭터의 장비를 item_id 순서로 잠근다. 없는 ID는 결과에서 빠진다 </summary>
        public async Task<Dictionary<Guid, EquipmentRecord>> LockEquipmentAsync(IEnumerable<Guid> itemIds,
            CancellationToken cancellationToken)
        {
            Guid[] ids = itemIds.Distinct().ToArray();
            IEnumerable<EquipmentRecord> rows = await Connection.QueryAsync<EquipmentRecord>(new CommandDefinition(
                "SELECT " + EQUIPMENT_COLUMNS + """
                 FROM equipment_instances
                WHERE character_id = @CharacterId AND account_id = @AccountId AND item_id = ANY(@Ids)
                ORDER BY item_id FOR UPDATE;
                """, new { CharacterId, AccountId, Ids = ids }, Transaction, cancellationToken: cancellationToken));
            return rows.ToDictionary(row => row.ItemId);
        }

        /// <summary> 이 캐릭터의 착용 장비를 잠근다. 착용 교체가 같은 부위의 기존 장비를 내릴 때 쓴다 </summary>
        public async Task<EquipmentRecord?> LockEquippedAsync(int slot, CancellationToken cancellationToken)
        {
            return await Connection.QuerySingleOrDefaultAsync<EquipmentRecord>(new CommandDefinition(
                "SELECT " + EQUIPMENT_COLUMNS + """
                 FROM equipment_instances
                WHERE character_id = @CharacterId AND slot = @Slot AND equipped
                FOR UPDATE;
                """, new { CharacterId, Slot = slot }, Transaction, cancellationToken: cancellationToken));
        }

        /// <summary> 착용하지 않은 장비 수. 보관 칸 판정에 쓴다 </summary>
        public Task<int> CountStoredAsync(CancellationToken cancellationToken)
        {
            return Connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT count(*)::integer FROM equipment_instances WHERE character_id = @CharacterId AND NOT equipped;",
                new { CharacterId }, Transaction, cancellationToken: cancellationToken));
        }

        /// <summary> 새 장비를 넣고 생성 이력을 남긴다 </summary>
        public async Task InsertEquipmentAsync(EquipmentRecord record, string eventKind, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(record);
            await Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO equipment_instances (item_id, account_id, character_id, definition_id, slot, grade, is_training,
                    origin, enhancement, equipped, locked, bound)
                VALUES (@ItemId, @AccountId, @CharacterId, @DefinitionId, @Slot, @Grade, @IsTraining,
                    @Origin, @Enhancement, @Equipped, @Locked, @Bound);
                """, record, Transaction, cancellationToken: cancellationToken));
            await AppendHistoryAsync(record.ItemId, eventKind, null, record, isNew: true, cancellationToken);
        }

        /// <summary> 잠근 장비의 가변 상태를 저장하고 이전, 이후 이력을 남긴다 </summary>
        public async Task UpdateEquipmentAsync(EquipmentRecord before, EquipmentRecord after, string eventKind,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);
            await Connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE equipment_instances
                SET enhancement = @Enhancement, equipped = @Equipped, locked = @Locked, bound = @Bound
                WHERE item_id = @ItemId;
                """, after, Transaction, cancellationToken: cancellationToken));
            await AppendHistoryAsync(after.ItemId, eventKind, before, after, isNew: false, cancellationToken);
        }

        /// <summary> 장비를 지우고 마지막 상태를 이력에 남긴다. item_history는 지운 장비도 보존한다 </summary>
        public async Task DeleteEquipmentAsync(EquipmentRecord before, string eventKind, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(before);
            await Connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM equipment_instances WHERE item_id = @ItemId;", new { before.ItemId }, Transaction,
                cancellationToken: cancellationToken));
            await AppendHistoryAsync(before.ItemId, eventKind, before, null, isNew: false, cancellationToken);
        }

        /// <summary> 상태 변화 없는 기록(강화 실패)도 이전, 이후를 같은 값으로 남긴다 </summary>
        public Task AppendAttemptAsync(EquipmentRecord record, string eventKind, CancellationToken cancellationToken)
        {
            return AppendHistoryAsync(record.ItemId, eventKind, record, record, isNew: false, cancellationToken);
        }

        private async Task AppendHistoryAsync(Guid itemId, string eventKind, EquipmentRecord? before,
            EquipmentRecord? after, bool isNew, CancellationToken cancellationToken)
        {
            // 장비 행을 잠근 뒤라 같은 장비의 일련번호를 다른 작업이 동시에 잡지 않는다
            long sequence = isNew ? 0 : await Connection.QuerySingleAsync<long>(new CommandDefinition(
                "SELECT COALESCE(MAX(seq) + 1, 0) FROM item_history WHERE item_id = @ItemId;", new { ItemId = itemId },
                Transaction, cancellationToken: cancellationToken));
            await _history.AppendAsync(Operation, new ItemHistoryEntry(itemId, sequence, eventKind,
                before?.ToHistoryJson(), after?.ToHistoryJson(), AccountId, ActorId, OperationId), cancellationToken);
        }

        private Task<bool> AppendLedgerAsync(EconomyAsset asset, long delta, long balanceAfter,
            CancellationToken cancellationToken)
        {
            // 작업 안 일련번호를 앞에 붙여 같은 자산을 두 줄(차감 뒤 지급 등) 써도 키가 겹치지 않는다. 직렬화 실패 재시도는
            // 효과를 새 컨텍스트로 다시 실행하므로 번호도 0부터 다시 매겨 같은 키가 된다
            int sequence = _ledgerSequence;
            ++_ledgerSequence;
            string entryKey = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + asset.Kind + ":"
                + asset.AccountId.ToString("N") + ":"
                + (asset.CharacterId?.ToString("N") ?? string.Empty) + ":" + asset.AssetId + ":" + asset.BindingKind;
            return _ledger.AppendAsync(Operation, new EconomyLedgerEntry(Guid.NewGuid(), entryKey, asset, delta,
                balanceAfter, ReasonCode, SOURCE_TYPE, OperationId.ToString("D"), ActorKind, ActorId, OperationId),
                cancellationToken);
        }
    }
}
