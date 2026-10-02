// System
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Admin
{
    /// <summary> 모든 운영자 업무 변경과 수동 복구가 효과 전에 공유하는 추가 전용 작업 키 결속 </summary>
    public sealed class PostgresAdminOperationClaimRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminOperationClaimRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 최초 요청의 종류, 대상과 본문 해시를 짧은 트랜잭션으로 커밋한다. 다른 요청은 operations 행이나 효과 없이
        /// 거절해야 기존 복구의 최종 결과를 가로채지 않는다. 결속 도입 전에 저장된 operations의 해시도 확인한다 </summary>
        public async Task<AdminOperationBinding> BindAsync(OperationRequest operation, string targetKey,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(targetKey);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
            BindingRow? existing = await connection.QuerySingleOrDefaultAsync<BindingRow>(new CommandDefinition(
                """
                SELECT request_hash AS "RequestHash", clock_timestamp() AS "ServerTime"
                FROM operations WHERE actor_id = @ActorId AND operation_id = @OperationId;
                """, operation, transaction, cancellationToken: cancellationToken));
            if ((existing != null) && (!string.Equals(existing.RequestHash, operation.RequestHash,
                StringComparison.Ordinal)))
            {
                return ToBinding(existing, operation);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO admin_operation_claims (admin_id, operation_id, operation_kind, target_key, request_hash)
                VALUES (@ActorId, @OperationId, @OperationKind, @TargetKey, @RequestHash)
                ON CONFLICT (admin_id, operation_id) DO NOTHING;
                """, new
                {
                    operation.ActorId, operation.OperationId, operation.OperationKind, TargetKey = targetKey,
                    operation.RequestHash
                }, transaction, cancellationToken: cancellationToken));
            BindingRow row = await connection.QuerySingleAsync<BindingRow>(new CommandDefinition(
                """
                SELECT request_hash AS "RequestHash", clock_timestamp() AS "ServerTime"
                FROM admin_operation_claims WHERE admin_id = @ActorId AND operation_id = @OperationId;
                """, operation, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return ToBinding(row, operation);
        }

        private static AdminOperationBinding ToBinding(BindingRow row, OperationRequest operation)
        {
            return new AdminOperationBinding(string.Equals(row.RequestHash, operation.RequestHash,
                StringComparison.Ordinal), PostgresAdminUserRepository.ToOffset(row.ServerTime));
        }

        private sealed class BindingRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public string RequestHash { get; set; } = string.Empty;
            public DateTime ServerTime { get; set; }
        }
    }
}
