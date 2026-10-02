// System
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json;
using Npgsql;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data
{
    /// <summary> 공유 Npgsql 풀을 사용한다. 같은 작업 키의 INSERT 경합을 유일 제약으로 기다린 뒤 최초 응답을 읽는다 </summary>
    public sealed class PostgresOperationStore : IOperationStore
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string EFFECT_SAVEPOINT = "operation_effects";
        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresOperationStore(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        public bool IsRetryable(Exception exception)
        {
            return PostgresTransactionPolicy.IsRetryable(exception);
        }

        public async Task<IOperationScope> BeginAsync(OperationRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            NpgsqlTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
                int inserted = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO operations (actor_id, operation_id, operation_kind, request_hash, status)
                    VALUES (@ActorId, @OperationId, @OperationKind, @RequestHash, 'Pending')
                    ON CONFLICT (actor_id, operation_id) DO NOTHING;
                    """, request, transaction, cancellationToken: cancellationToken));
                StoredOperation? existing = null;
                if (inserted == 0)
                {
                    OperationRow row = await connection.QuerySingleAsync<OperationRow>(new CommandDefinition(
                        """
                        SELECT request_hash AS "RequestHash", result_json::text AS "ResultJson"
                        FROM operations WHERE actor_id = @ActorId AND operation_id = @OperationId FOR UPDATE;
                        """, request, transaction, cancellationToken: cancellationToken));
                    if (row.ResultJson == null)
                    {
                        throw new InvalidOperationException("A committed operation must contain its terminal result.");
                    }
                    OperationResult result = JsonConvert.DeserializeObject<OperationResult>(row.ResultJson,
                        ContractJson.CreateSettings())
                        ?? throw new InvalidOperationException("Stored operation result is invalid.");
                    existing = new StoredOperation(row.RequestHash, result);
                }

                DateTime serverTime = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                    "SELECT clock_timestamp();", transaction: transaction, cancellationToken: cancellationToken));
                if (inserted != 0)
                {
                    await transaction.SaveAsync(EFFECT_SAVEPOINT, cancellationToken);
                }

                return new PostgresOperationScope(connection, transaction, request, existing,
                    new DateTimeOffset(serverTime));
            }
            catch
            {
                // 트랜잭션을 소유한 scope를 반환하기 전 실패했으므로 여기서 연결까지 정리한다.
                try
                {
                    if (transaction != null)
                    {
                        await transaction.DisposeAsync();
                    }
                }
                finally
                {
                    await connection.DisposeAsync();
                }
                throw;
            }
        }

        private sealed class OperationRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public string RequestHash { get; set; } = string.Empty;
            public string? ResultJson { get; set; }
        }

        private sealed class PostgresOperationScope : IOperationScope
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly NpgsqlConnection _connection;
            private readonly NpgsqlTransaction _transaction;
            private readonly OperationRequest _request;
            private bool _saved;
            private bool _committed;

            /****************************************
            *              Properties
            ****************************************/

            public StoredOperation? ExistingOperation { get; }
            public OperationExecutionContext Context { get; }

            /****************************************
            *                Methods
            ****************************************/

            public PostgresOperationScope(NpgsqlConnection connection, NpgsqlTransaction transaction,
                OperationRequest request, StoredOperation? existing, DateTimeOffset serverTime)
            {
                _connection      = connection;
                _transaction     = transaction;
                _request         = request;
                ExistingOperation = existing;
                Context          = new OperationExecutionContext(connection, transaction, serverTime);
            }

            public Task RollbackEffectsAsync(CancellationToken cancellationToken)
            {
                EnsureWritable();
                if (_saved)
                {
                    throw new InvalidOperationException("Effects cannot be rolled back after saving the result.");
                }
                return _transaction.RollbackAsync(EFFECT_SAVEPOINT, cancellationToken);
            }

            public async Task SaveResultAsync(OperationResult result, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(result);
                EnsureWritable();
                if (_saved)
                {
                    throw new InvalidOperationException("The operation result is already saved.");
                }
                if (!string.Equals(result.OperationId, _request.OperationId.ToString("D"), StringComparison.Ordinal))
                {
                    throw new ArgumentException("Operation result ID does not match the request.", nameof(result));
                }
                if ((result.Status != ContractCodes.OperationStatus.COMMITTED)
                    && (result.Status != ContractCodes.OperationStatus.REJECTED))
                {
                    throw new ArgumentException("Only terminal operation results can be committed.", nameof(result));
                }
                if (((result.Status == ContractCodes.OperationStatus.REJECTED)
                        && (result.ReasonCode == ContractCodes.RejectReason.NONE))
                    || ((result.Status == ContractCodes.OperationStatus.COMMITTED)
                        && (result.ReasonCode != ContractCodes.RejectReason.NONE)))
                {
                    throw new ArgumentException("Operation result reason does not match its status.", nameof(result));
                }
                if ((result.Status == ContractCodes.OperationStatus.COMMITTED) && string.IsNullOrWhiteSpace(result.ReceiptId))
                {
                    throw new ArgumentException("A committed result requires a receipt ID.", nameof(result));
                }
                if (result.CommittedRevision < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(result));
                }

                string resultJson = JsonConvert.SerializeObject(result, ContractJson.CreateSettings());
                int updated = await _connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE operations
                    SET status = @Status, result_json = CAST(@ResultJson AS jsonb),
                        committed_revision = @Revision, receipt_id = @ReceiptId, committed_at = clock_timestamp()
                    WHERE actor_id = @ActorId AND operation_id = @OperationId AND status = 'Pending';
                    """, new
                    {
                        _request.ActorId,
                        _request.OperationId,
                        Status = result.Status == ContractCodes.OperationStatus.COMMITTED ? "Committed" : "Rejected",
                        ResultJson = resultJson,
                        Revision = result.CommittedRevision,
                        ReceiptId = string.IsNullOrEmpty(result.ReceiptId) ? null : result.ReceiptId
                    }, _transaction, cancellationToken: cancellationToken));
                if (updated != 1)
                {
                    throw new InvalidOperationException("Operation terminal transition did not update exactly one row.");
                }
                _saved = true;
            }

            public async Task CommitAsync(CancellationToken cancellationToken)
            {
                EnsureWritable();
                if (!_saved)
                {
                    throw new InvalidOperationException("Operation result must be saved before commit.");
                }
                await _transaction.CommitAsync(cancellationToken);
                _committed = true;
            }

            public async ValueTask DisposeAsync()
            {
                // 미커밋 트랜잭션의 Dispose는 효과와 Pending 행을 함께 롤백한다.
                try
                {
                    await _transaction.DisposeAsync();
                }
                finally
                {
                    await _connection.DisposeAsync();
                }
            }

            private void EnsureWritable()
            {
                if ((ExistingOperation != null) || _committed)
                {
                    throw new InvalidOperationException("A replayed or committed operation is read-only.");
                }
            }
        }
    }
}




