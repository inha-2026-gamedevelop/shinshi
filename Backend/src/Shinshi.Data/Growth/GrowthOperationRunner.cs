// System
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Growth
{
    /// <summary> 성장 쓰기의 공통 경계. 멱등 실행, 잠금 순서(계정 -> 캐릭터), 위치, 열린 출정, revision 검사, 영수증과
    /// loadout_revision 증가를 한 트랜잭션에 묶는다. 작업별 효과는 그 사이에서 장비, account_items 순서로 잠근다 </summary>
    public sealed class GrowthOperationRunner
    {
        /****************************************
        *                Fields
        ****************************************/

        // 멱등 actor가 계정이므로 원장의 actor_kind도 요청한 플레이어다. 중계한 마을은 growth_receipts.town_instance_id에 남는다
        private const string ACTOR_KIND = "Player";

        // 저장하지 않은 거절은 아무것도 확정하지 않는다
        private const long NO_REVISION = 0;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly IEconomyLedgerEntryWriter _ledger;
        private readonly PostgresItemHistoryWriter _history;
        private readonly PostgresGrowthReader _reader;

        /****************************************
        *              Properties
        ****************************************/

        public GrowthContent Content { get; }

        /****************************************
        *                Methods
        ****************************************/

        public GrowthOperationRunner(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            IEconomyLedgerEntryWriter ledger, PostgresItemHistoryWriter history, PostgresGrowthReader reader,
            GrowthContent content)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(ledger);
            ArgumentNullException.ThrowIfNull(history);
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(content);
            _dataSource = dataSource;
            _executor   = executor;
            _ledger     = ledger;
            _history    = history;
            _reader     = reader;
            Content     = content;
        }

        /// <summary> replayOnly면 효과 없이 저장된 결과만 찾는다(기능 차단 중 재전송). 처음 보는 operationId면 Result가 null이다.
        /// 멱등 actor는 캐릭터의 계정이므로 실행 전에 캐릭터에서 계정을 읽는다. 없는 캐릭터는 저장할 actor가 없어 저장 없이 거절한다 </summary>
        public async Task<GrowthOperationOutcome> RunAsync(GrowthCommand command,
            Func<GrowthEffectContext, CancellationToken, Task<GrowthEffectResult>> effect, bool replayOnly,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(effect);
            OwnerRow owner = await ReadOwnerAsync(command.CharacterId, cancellationToken);
            if (!owner.AccountId.HasValue)
            {
                OperationResult? missing = replayOnly ? null : new OperationResult(command.OperationId.ToString("D"),
                    ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.INVALID_TARGET, NO_REVISION,
                    owner.ServerTimeUnixMs, string.Empty);
                return new GrowthOperationOutcome(missing, GrowthCodes.Outcome.NONE, null, null);
            }

            Guid accountId = owner.AccountId.Value;
            OperationRequest request = command.ToOperationRequest(accountId);
            OperationResult? result = replayOnly
                ? await _executor.FindStoredAsync(request, cancellationToken)
                : await _executor.ExecuteAsync(request,
                    (context, token) => ExecuteEffectAsync(command, accountId, effect, context, token), cancellationToken);
            if (result == null)
            {
                return new GrowthOperationOutcome(null, GrowthCodes.Outcome.NONE, null, null);
            }

            ReceiptRow? receipt = null;
            if ((result.Status == ContractCodes.OperationStatus.COMMITTED) && Guid.TryParse(result.ReceiptId, out Guid receiptId))
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                receipt = await connection.QuerySingleOrDefaultAsync<ReceiptRow>(new CommandDefinition(
                    """
                    SELECT outcome AS "Outcome", item_id AS "ItemId" FROM growth_receipts
                    WHERE receipt_id = @ReceiptId AND actor_id = @ActorId AND operation_id = @OperationId;
                    """, new { ReceiptId = receiptId, ActorId = accountId, command.OperationId },
                    cancellationToken: cancellationToken));
            }

            CharacterGrowthSnapshot? growth = await _reader.ReadAsync(command.CharacterId, cancellationToken);
            return new GrowthOperationOutcome(result, receipt?.Outcome ?? GrowthCodes.Outcome.NONE, receipt?.ItemId, growth);
        }

        // 삭제한 캐릭터도 계정을 읽는다. 삭제 전에 확정한 작업의 재전송이 저장 결과를 받아야 하기 때문이다(새 효과는 잠금 뒤 삭제 여부로 거절)
        private async Task<OwnerRow> ReadOwnerAsync(Guid characterId, CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleAsync<OwnerRow>(new CommandDefinition(
                """
                SELECT (SELECT account_id FROM characters WHERE character_id = @CharacterId) AS "AccountId",
                    (extract(epoch FROM clock_timestamp()) * 1000)::bigint AS "ServerTimeUnixMs";
                """, new { CharacterId = characterId }, cancellationToken: cancellationToken));
        }

        private async Task<OperationDecision> ExecuteEffectAsync(GrowthCommand command, Guid accountId,
            Func<GrowthEffectContext, CancellationToken, Task<GrowthEffectResult>> effect, OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 잠금 순서: 계정 -> 캐릭터 -> 장비(item_id) -> account_items(정의 ID). 예약도 계정을 먼저 잠그므로
            // 열린 출정 검사와 예약 생성이 서로 끼어들지 못한다
            await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken);
            // 삭제는 같은 계정 행 잠금 뒤에 deleted_at을 채우므로, 잠금 뒤 다시 본 삭제 여부가 최종 판정이다
            LockedCharacter? character = await connection.QuerySingleOrDefaultAsync<LockedCharacter>(new CommandDefinition(
                """
                SELECT class_id AS "ClassId", loadout_revision AS "LoadoutRevision",
                    EXISTS (SELECT 1 FROM account_leases AS lease WHERE lease.account_id = character.account_id
                        AND lease.instance_id = @Town AND lease.role = @TownRole
                        AND lease.expires_at > clock_timestamp()) AS "InTown",
                    EXISTS (SELECT 1 FROM session_participants AS participant
                        WHERE participant.account_id = character.account_id
                            AND participant.state IN ('Reserved', 'Active')) AS "InSortie"
                FROM characters AS character
                WHERE character.character_id = @CharacterId AND character.account_id = @AccountId
                    AND character.deleted_at IS NULL
                FOR UPDATE OF character;
                """, new
                {
                    command.CharacterId,
                    AccountId = accountId,
                    Town = command.TownInstanceId,
                    TownRole = ContractCodes.ServerRole.TOWN
                }, transaction, cancellationToken: cancellationToken));
            if (character == null)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }
            if (!character.InTown)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.WRONG_LOCATION, character.LoadoutRevision);
            }
            if (character.InSortie)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.LOCKED, character.LoadoutRevision);
            }
            if (character.LoadoutRevision != command.ExpectedRevision)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.STALE_REVISION, character.LoadoutRevision);
            }

            GrowthEffectContext growth = new GrowthEffectContext(context, Content, _ledger, _history, accountId,
                ACTOR_KIND, command.OperationId, accountId, command.CharacterId, character.ClassId, command.ReasonCode);
            GrowthEffectResult outcome = await effect(growth, cancellationToken);
            if (outcome.IsRejected)
            {
                return OperationDecision.Rejected(outcome.ReasonCode, character.LoadoutRevision);
            }

            long revision = outcome.ChangesLoadout
                ? await connection.QuerySingleAsync<long>(new CommandDefinition(
                    """
                    UPDATE characters SET loadout_revision = loadout_revision + 1
                    WHERE character_id = @CharacterId RETURNING loadout_revision;
                    """, new { command.CharacterId }, transaction, cancellationToken: cancellationToken))
                : character.LoadoutRevision;
            Guid receiptId = Guid.NewGuid();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO growth_receipts (receipt_id, actor_id, operation_id, account_id, character_id, operation_kind,
                    outcome, item_id, loadout_revision, town_instance_id, roll_basis_points)
                VALUES (@ReceiptId, @ActorId, @OperationId, @AccountId, @CharacterId, @Kind, @Outcome, @ItemId, @Revision,
                    @Town, @Roll);
                """, new
                {
                    ReceiptId = receiptId,
                    ActorId = accountId,
                    command.OperationId,
                    AccountId = accountId,
                    Town = command.TownInstanceId,
                    Roll = command.RollBasisPoints,
                    command.CharacterId,
                    Kind = command.OperationKind,
                    outcome.Outcome,
                    outcome.ItemId,
                    Revision = revision
                }, transaction, cancellationToken: cancellationToken));
            return OperationDecision.Committed(revision, receiptId.ToString("D"));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class LockedCharacter
        {
            public int  ClassId         { get; set; }
            public long LoadoutRevision { get; set; }
            public bool InTown          { get; set; }
            public bool InSortie        { get; set; }
        }

        private sealed class OwnerRow
        {
            public Guid? AccountId        { get; set; }
            public long  ServerTimeUnixMs { get; set; }
        }

        private sealed class ReceiptRow
        {
            public int   Outcome { get; set; }
            public Guid? ItemId  { get; set; }
        }
    }
}
