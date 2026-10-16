// System
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data
{
    /// <summary> 계정 행을 먼저 잠그고 활성 임대 하나만 허용한다. 만료와 heartbeat는 DB 시각으로 판정한다 </summary>
    public sealed class PostgresAccountLeaseRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버 heartbeat의 유효 기간(초)과 일시 오류 재시도 한도.
        private const int SERVER_HEARTBEAT_TIMEOUT_SECONDS = 15;
        private const int MAX_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 50;
        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAccountLeaseRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 살아 있는 같은 역할 서버에만 임대를 부여한다. 동일 임대 재요청은 기존 만료 시각을 그대로 돌려준다 </summary>
        public async Task<AccountLeaseOutcome> TryAcquireAsync(Guid accountId, Guid leaseId, Guid instanceId,
            int role, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            ValidateIds(accountId, leaseId, instanceId);
            ValidateDuration(duration);
            ValidateRole(role);

            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    return await AcquireOnceAsync(accountId, leaseId, instanceId, role, duration, cancellationToken);
                }
                catch (PostgresException exception) when ((attempt < MAX_ATTEMPTS)
                    && !cancellationToken.IsCancellationRequested && PostgresTransactionPolicy.IsRetryable(exception))
                {
                    await Task.Delay(RETRY_DELAY_MS * attempt, cancellationToken);
                }
                catch (Exception exception) when (PostgresTransactionPolicy.IsRetryable(exception))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new AccountLeaseOutcome(EAccountLeaseStatus.TransientFailure, null);
                }
            }
        }

        /// <summary> 살아 있는 기존 소유권을 해제 없이 새 서버로 이관한다. 같은 기존 임대로 경합하면 한 요청만 이긴다 </summary>
        public async Task<AccountLeaseOutcome> TransferAsync(Guid accountId, Guid existingLeaseId,
            Guid existingInstanceId, Guid newLeaseId, Guid newInstanceId, int role, TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            ValidateIds(accountId, existingLeaseId, existingInstanceId);
            ValidateIds(accountId, newLeaseId, newInstanceId);
            ValidateDuration(duration);
            ValidateRole(role);
            if (existingLeaseId == newLeaseId)
            {
                throw new ArgumentException("Transfer requires a new lease ID.", nameof(newLeaseId));
            }

            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    return await TransferOnceAsync(accountId, existingLeaseId, existingInstanceId,
                        newLeaseId, newInstanceId, role, duration, cancellationToken);
                }
                catch (PostgresException exception) when ((attempt < MAX_ATTEMPTS)
                    && !cancellationToken.IsCancellationRequested && PostgresTransactionPolicy.IsRetryable(exception))
                {
                    await Task.Delay(RETRY_DELAY_MS * attempt, cancellationToken);
                }
                catch (Exception exception) when (PostgresTransactionPolicy.IsRetryable(exception))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new AccountLeaseOutcome(EAccountLeaseStatus.TransientFailure, null);
                }
                catch (PostgresException exception) when ((exception.SqlState == PostgresErrorCodes.UniqueViolation)
                    && (exception.ConstraintName == "account_leases_lease_id_key"))
                {
                    return new AccountLeaseOutcome(EAccountLeaseStatus.LeaseIdConflict, null);
                }
            }
        }

        /// <summary> 만료되지 않은 현재 소유자만 갱신한다. 소실된 서버와 늦게 온 heartbeat는 임대를 되살릴 수 없다 </summary>
        public async Task<bool> RenewAsync(Guid accountId, Guid leaseId, Guid instanceId, TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            ValidateIds(accountId, leaseId, instanceId);
            ValidateDuration(duration);
            return await ExecuteOwnedLeaseChangeAsync(accountId,
                """
                UPDATE account_leases AS lease SET expires_at = clock_timestamp() + @Duration
                WHERE lease.account_id = @AccountId AND lease.lease_id = @LeaseId AND lease.instance_id = @InstanceId
                    AND lease.expires_at > clock_timestamp()
                    AND EXISTS (SELECT 1 FROM server_instances AS instance WHERE instance.instance_id = lease.instance_id
                        AND instance.role = lease.role AND instance.state IN ('Idle', 'Reserved', 'Active', 'Draining')
                        AND instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout FOR SHARE);
                """, new { AccountId = accountId, LeaseId = leaseId, InstanceId = instanceId, Duration = duration,
                    HeartbeatTimeout = TimeSpan.FromSeconds(SERVER_HEARTBEAT_TIMEOUT_SECONDS) },
                cancellationToken);
        }

        /// <summary> account, lease, instance가 모두 같은 소유자만 해제한다. 이전 연결이 새 연결을 해제하지 못한다 </summary>
        public async Task<bool> ReleaseAsync(Guid accountId, Guid leaseId, Guid instanceId,
            CancellationToken cancellationToken = default)
        {
            ValidateIds(accountId, leaseId, instanceId);
            return await ExecuteOwnedLeaseChangeAsync(accountId,
                "DELETE FROM account_leases WHERE account_id = @AccountId AND lease_id = @LeaseId AND instance_id = @InstanceId;",
                new { AccountId = accountId, LeaseId = leaseId, InstanceId = instanceId }, cancellationToken);
        }

        /// <summary> 서버가 접속 종료한 계정의 임대를 lease ID로 푼다. 이미 풀렸거나 이관돼 ID가 바뀌었으면 NotFound(멱등 성공)다.
        /// 다른 인스턴스가 쥔 임대는 풀지 않는다 </summary>
        public async Task<EAccountLeaseReleaseStatus> ReleaseOwnedAsync(Guid instanceId, Guid leaseId,
            CancellationToken cancellationToken = default)
        {
            if ((instanceId == Guid.Empty) || (leaseId == Guid.Empty))
            {
                throw new ArgumentException("Instance and lease IDs are required.");
            }

            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    return await ReleaseOwnedOnceAsync(instanceId, leaseId, cancellationToken);
                }
                catch (PostgresException exception) when ((attempt < MAX_ATTEMPTS)
                    && !cancellationToken.IsCancellationRequested && PostgresTransactionPolicy.IsRetryable(exception))
                {
                    await Task.Delay(RETRY_DELAY_MS * attempt, cancellationToken);
                }
                catch (Exception exception) when (PostgresTransactionPolicy.IsTimeout(exception))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new RetryableDatabaseException(exception);
                }
            }
        }

        private async Task<EAccountLeaseReleaseStatus> ReleaseOwnedOnceAsync(Guid instanceId, Guid leaseId,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);

            // 계정 ID만 잠그지 않고 찾은 뒤 다른 임대 쓰기와 같은 account -> lease 순서로 잠그고 다시 확인한다
            Guid? accountId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT account_id FROM account_leases WHERE lease_id = @LeaseId;",
                new { LeaseId = leaseId }, transaction, cancellationToken: cancellationToken));
            if (!accountId.HasValue || !await LockAccountAsync(connection, transaction, accountId.Value, cancellationToken))
            {
                return EAccountLeaseReleaseStatus.NotFound;
            }

            LeaseRow? row = await ReadLockedLeaseAsync(connection, transaction, accountId.Value, cancellationToken);
            if ((row == null) || (row.LeaseId != leaseId))
            {
                return EAccountLeaseReleaseStatus.NotFound;
            }
            if (row.InstanceId != instanceId)
            {
                return EAccountLeaseReleaseStatus.OtherOwner;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM account_leases WHERE account_id = @AccountId AND lease_id = @LeaseId AND instance_id = @InstanceId;",
                new { AccountId = accountId.Value, LeaseId = leaseId, InstanceId = instanceId }, transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return EAccountLeaseReleaseStatus.Released;
        }

        /// <summary> 출정 종결(정산, 장애 취소, 예약 해제) 트랜잭션 안에서 참가자 계정이 그 던전에 남긴 접속 임대를 지운다.
        /// 호출자가 계정 행을 이미 잠갔어야 한다. 임대 행은 계정 순서로 잠가 heartbeat 연장과 같은 방향을 지킨다 </summary>
        public static Task<int> ReleaseForClosedSessionAsync(DbConnection connection, DbTransaction transaction,
            Guid dungeonInstanceId, IEnumerable<Guid> accountIds, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(accountIds);
            Guid[] accounts = accountIds.Distinct().ToArray();
            if (accounts.Length == 0)
            {
                return Task.FromResult(0);
            }

            return connection.ExecuteAsync(new CommandDefinition(
                """
                WITH target AS (
                    SELECT account_id FROM account_leases
                    WHERE account_id = ANY(@Accounts) AND instance_id = @InstanceId
                    ORDER BY account_id
                    FOR UPDATE
                )
                DELETE FROM account_leases AS lease USING target
                WHERE lease.account_id = target.account_id AND lease.instance_id = @InstanceId;
                """, new { Accounts = accounts, InstanceId = dungeonInstanceId }, transaction,
                cancellationToken: cancellationToken));
        }

        private async Task<bool> ExecuteOwnedLeaseChangeAsync(Guid accountId, string sql, object parameters,
            CancellationToken cancellationToken)
        {
            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted, cancellationToken);
                    await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
                    if (!await LockAccountAsync(connection, transaction, accountId, cancellationToken))
                    {
                        return false;
                    }

                    // 잠금 순서: account FOR UPDATE -> lease FOR UPDATE -> instance FOR SHARE.
                    // 모든 임대 쓰기가 같은 순서를 지켜 이관과 이전 연결의 갱신/해제를 직렬화한다.
                    await ReadLockedLeaseAsync(connection, transaction, accountId, cancellationToken);
                    int changed = await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction,
                        cancellationToken: cancellationToken));
                    await transaction.CommitAsync(cancellationToken);
                    return changed == 1;
                }
                catch (PostgresException exception) when ((attempt < MAX_ATTEMPTS)
                    && !cancellationToken.IsCancellationRequested && PostgresTransactionPolicy.IsRetryable(exception))
                {
                    await Task.Delay(RETRY_DELAY_MS * attempt, cancellationToken);
                }
                catch (Exception exception) when (PostgresTransactionPolicy.IsTimeout(exception))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new RetryableDatabaseException(exception);
                }
            }
        }

        private async Task<AccountLeaseOutcome> AcquireOnceAsync(Guid accountId, Guid leaseId, Guid instanceId,
            int role, TimeSpan duration, CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
            if (!await LockAccountAsync(connection, transaction, accountId, cancellationToken))
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.AccountNotFound, null);
            }

            // 잠금 순서: account FOR UPDATE -> lease FOR UPDATE -> instance FOR SHARE.
            // JOIN 실행 계획에 잠금 순서를 맡기지 않고 별도 명령으로 순서를 고정한다.
            LeaseRow? row = await ReadLockedLeaseAsync(connection, transaction, accountId, cancellationToken);
            if ((row != null) && row.IsAlive)
            {
                InstanceRow? previous = await ReadSharedInstanceAsync(connection, transaction, row.InstanceId,
                    cancellationToken);
                if ((previous != null) && (previous.Role == row.Role) && previous.IsAlive)
                {
                    // 서버 SHARE 잠금을 기다리는 사이 임대가 만료될 수 있다.
                    row = await ReadLockedLeaseAsync(connection, transaction, accountId, cancellationToken);
                    if ((row != null) && row.IsAlive)
                    {
                        bool sameOwner = (row.LeaseId == leaseId) && (row.InstanceId == instanceId) && (row.Role == role);
                        return new AccountLeaseOutcome(sameOwner ? EAccountLeaseStatus.Acquired : EAccountLeaseStatus.AlreadyConnected,
                            sameOwner ? ToLease(row) : null);
                    }
                }
            }

            InstanceRow? instance = await ReadSharedInstanceAsync(connection, transaction, instanceId, cancellationToken);
            if ((instance == null) || (instance.Role != role) || !instance.IsAlive || (instance.State == "Draining"))
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.ServerUnavailable, null);
            }

            // 이 경로는 캐릭터를 모른다(운영 호출자 없음, 시험과 이전 경로용). 덮어쓸 때 이전 캐릭터의 접속 표시가 남지 않게
            // character_id를 비운다(소셜에서는 오프라인)
            LeaseRow? acquired = await connection.QuerySingleOrDefaultAsync<LeaseRow>(new CommandDefinition(
                """
                INSERT INTO account_leases (account_id, lease_id, instance_id, role, expires_at)
                SELECT @AccountId, @LeaseId, @InstanceId, @Role, clock_timestamp() + @Duration
                WHERE EXISTS (SELECT 1 FROM server_instances AS target
                    WHERE target.instance_id = @InstanceId AND target.role = @Role
                        AND target.state IN ('Idle', 'Reserved', 'Active')
                        AND target.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)
                ON CONFLICT (account_id) DO UPDATE SET lease_id = EXCLUDED.lease_id,
                    instance_id = EXCLUDED.instance_id, role = EXCLUDED.role, expires_at = EXCLUDED.expires_at,
                    character_id = NULL
                    WHERE account_leases.expires_at <= clock_timestamp()
                        OR NOT EXISTS (SELECT 1 FROM server_instances AS previous_instance
                            WHERE previous_instance.instance_id = account_leases.instance_id
                                AND previous_instance.role = account_leases.role
                                AND previous_instance.state IN ('Idle', 'Reserved', 'Active', 'Draining')
                                AND previous_instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)
                RETURNING account_id AS "AccountId", lease_id AS "LeaseId", instance_id AS "InstanceId",
                    role AS "Role", expires_at AS "ExpiresAt";
                """, new { AccountId = accountId, LeaseId = leaseId, InstanceId = instanceId, Role = role, Duration = duration,
                    HeartbeatTimeout = TimeSpan.FromSeconds(SERVER_HEARTBEAT_TIMEOUT_SECONDS) },
                transaction, cancellationToken: cancellationToken));
            if (acquired == null)
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.ServerUnavailable, null);
            }
            await transaction.CommitAsync(cancellationToken);
            return new AccountLeaseOutcome(EAccountLeaseStatus.Acquired, ToLease(acquired));
        }

        private async Task<AccountLeaseOutcome> TransferOnceAsync(Guid accountId, Guid existingLeaseId,
            Guid existingInstanceId, Guid newLeaseId, Guid newInstanceId, int role, TimeSpan duration,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);

            // 잠금 순서: account FOR UPDATE -> lease FOR UPDATE -> instance FOR SHARE.
            // Acquire와 같아야 새 로그인과 이관이 기존 소유권을 동시에 교체할 수 없다.
            if (!await LockAccountAsync(connection, transaction, accountId, cancellationToken))
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.AccountNotFound, null);
            }
            LeaseRow? existing = await ReadLockedLeaseAsync(connection, transaction, accountId, cancellationToken);
            if (existing == null)
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.LeaseNotFound, null);
            }
            if ((existing.LeaseId != existingLeaseId) || (existing.InstanceId != existingInstanceId))
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.LeaseOwnershipMismatch, null);
            }
            if (!existing.IsAlive)
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.LeaseExpired, null);
            }
            InstanceRow? source = await ReadSharedInstanceAsync(connection, transaction, existingInstanceId,
                cancellationToken);
            if ((source == null) || (source.Role != existing.Role) || !source.IsAlive)
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.SourceServerUnavailable, null);
            }
            InstanceRow? target = await ReadSharedInstanceAsync(connection, transaction, newInstanceId,
                cancellationToken);
            if (target == null)
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.ServerUnavailable, null);
            }
            if (target.Role != role)
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.ServerRoleMismatch, null);
            }
            if (!target.IsAlive || (target.State == "Draining"))
            {
                return new AccountLeaseOutcome(EAccountLeaseStatus.ServerUnavailable, null);
            }

            // SHARE 잠금을 기다린 사이 만료될 수 있으므로 마지막 UPDATE도 DB 시각으로 재검증한다.
            LeaseRow? transferred = await connection.QuerySingleOrDefaultAsync<LeaseRow>(new CommandDefinition(
                """
                UPDATE account_leases AS lease
                SET lease_id = @NewLeaseId, instance_id = @NewInstanceId, role = @Role,
                    expires_at = clock_timestamp() + @Duration
                WHERE lease.account_id = @AccountId AND lease.lease_id = @ExistingLeaseId
                    AND lease.instance_id = @ExistingInstanceId AND lease.expires_at > clock_timestamp()
                    AND EXISTS (SELECT 1 FROM server_instances AS source
                        WHERE source.instance_id = lease.instance_id AND source.role = lease.role
                            AND source.state IN ('Idle', 'Reserved', 'Active', 'Draining')
                            AND source.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)
                    AND EXISTS (SELECT 1 FROM server_instances AS target
                        WHERE target.instance_id = @NewInstanceId AND target.role = @Role
                            AND target.state IN ('Idle', 'Reserved', 'Active')
                            AND target.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)
                RETURNING account_id AS "AccountId", lease_id AS "LeaseId", instance_id AS "InstanceId",
                    role AS "Role", expires_at AS "ExpiresAt";
                """, new { AccountId = accountId, ExistingLeaseId = existingLeaseId, ExistingInstanceId = existingInstanceId,
                    NewLeaseId = newLeaseId, NewInstanceId = newInstanceId, Role = role, Duration = duration,
                    HeartbeatTimeout = TimeSpan.FromSeconds(SERVER_HEARTBEAT_TIMEOUT_SECONDS) },
                transaction, cancellationToken: cancellationToken));
            if (transferred == null)
            {
                LeaseRow? current = await ReadLockedLeaseAsync(connection, transaction, accountId, cancellationToken);
                if ((current == null) || !current.IsAlive)
                {
                    return new AccountLeaseOutcome(EAccountLeaseStatus.LeaseExpired, null);
                }
                InstanceRow? currentSource = await ReadSharedInstanceAsync(connection, transaction, existingInstanceId,
                    cancellationToken);
                return new AccountLeaseOutcome((currentSource == null) || !currentSource.IsAlive
                    ? EAccountLeaseStatus.SourceServerUnavailable : EAccountLeaseStatus.ServerUnavailable, null);
            }
            await transaction.CommitAsync(cancellationToken);
            return new AccountLeaseOutcome(EAccountLeaseStatus.Transferred, ToLease(transferred));
        }

        private static async Task<bool> LockAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid accountId, CancellationToken cancellationToken)
        {
            Guid? account = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT account_id FROM accounts WHERE account_id = @AccountId FOR UPDATE;",
                new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
            return account.HasValue;
        }

        private static Task<LeaseRow?> ReadLockedLeaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid accountId, CancellationToken cancellationToken)
        {
            return connection.QuerySingleOrDefaultAsync<LeaseRow>(new CommandDefinition(
                """
                SELECT account_id AS "AccountId", lease_id AS "LeaseId", instance_id AS "InstanceId",
                    role AS "Role", expires_at AS "ExpiresAt", expires_at > clock_timestamp() AS "IsAlive"
                FROM account_leases WHERE account_id = @AccountId FOR UPDATE;
                """, new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
        }

        private static Task<InstanceRow?> ReadSharedInstanceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid instanceId, CancellationToken cancellationToken)
        {
            return connection.QuerySingleOrDefaultAsync<InstanceRow>(new CommandDefinition(
                """
                SELECT role AS "Role", state AS "State",
                    (state IN ('Idle', 'Reserved', 'Active', 'Draining')
                        AND heartbeat_at > clock_timestamp() - @HeartbeatTimeout) AS "IsAlive"
                FROM server_instances WHERE instance_id = @InstanceId FOR SHARE;
                """, new { InstanceId = instanceId, HeartbeatTimeout = TimeSpan.FromSeconds(SERVER_HEARTBEAT_TIMEOUT_SECONDS) },
                transaction, cancellationToken: cancellationToken));
        }

        private static AccountLease ToLease(LeaseRow row)
        {
            return new AccountLease(row.AccountId, row.LeaseId, row.InstanceId, row.Role, new DateTimeOffset(row.ExpiresAt));
        }

        private static void ValidateIds(Guid accountId, Guid leaseId, Guid instanceId)
        {
            if ((accountId == Guid.Empty) || (leaseId == Guid.Empty) || (instanceId == Guid.Empty))
            {
                throw new ArgumentException("Account, lease and instance IDs are required.");
            }
        }

        private static void ValidateDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }
        }

        private static void ValidateRole(int role)
        {
            if ((role != ContractCodes.ServerRole.TOWN) && (role != ContractCodes.ServerRole.DUNGEON))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        private sealed class LeaseRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid AccountId { get; set; }
            public Guid LeaseId { get; set; }
            public Guid InstanceId { get; set; }
            public int Role { get; set; }
            public DateTime ExpiresAt { get; set; }
            public bool IsAlive { get; set; }
        }

        private sealed class InstanceRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public int Role { get; set; }
            public string State { get; set; } = string.Empty;
            public bool IsAlive { get; set; }
        }
    }
}
