using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Dapper;
using Npgsql;
using Xunit;

using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Backend.Tests
{
    /// <summary> 제공된 PostgreSQL의 격리 스키마에서 마이그레이션과 트랜잭션 경계를 검사한다 </summary>
    public sealed class PostgresIntegrationTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int  CONCURRENT_CALLS = 16;
        private const int  TIMEOUT_SECONDS  = 30;
        private const int  TEST_PORT        = 7777;
        private const long TEST_REVISION    = 9007199254740993L;
        private const string OPERATION_KIND = "test-operation";
        private const string ORIGINAL_BODY  = "{\"quantity\":1}";
        private const string DIFFERENT_BODY = "{\"quantity\":2}";
        private const string RECEIPT_ID     = "receipt-placeholder";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> up은 반복 적용되고 down SQL은 같은 격리 스키마의 테이블을 되돌린다 </summary>
        [PostgresFact]
        public async Task ApplyAsync_EmptySchema_RepeatsAndDownSqlAllowsReapplication()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(applyMigrations: false);

            Assert.Equal(MigrationTestResources.Count, await database.Migrations.ApplyAsync());
            Assert.Equal(0, await database.Migrations.ApplyAsync());

            // 나중 마이그레이션이 앞의 테이블을 참조하므로 최신 down부터 역순으로 되돌린다
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync())
            {
                await MigrationTestResources.RollBackAboveAsync(connection, transaction, 0);
                await connection.ExecuteAsync("DELETE FROM schema_migrations;", transaction: transaction);
                await transaction.CommitAsync();
            }

            long remaining = await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name <> 'schema_migrations';");
            Assert.Equal(0L, remaining);
            Assert.Equal(MigrationTestResources.Count, await database.Migrations.ApplyAsync());
        }

        /// <summary> 이미 적용한 SQL의 이력 체크섬이 다르면 추가 적용을 중단한다 </summary>
        [PostgresFact]
        public async Task ApplyAsync_ChangedHistoryChecksum_RejectsMigrationDrift()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("UPDATE schema_migrations SET checksum = repeat('0', 64);");

            await Assert.ThrowsAsync<InvalidOperationException>(() => database.Migrations.ApplyAsync());
            Assert.Equal((long)MigrationTestResources.Count,
                await connection.QuerySingleAsync<long>("SELECT count(*) FROM schema_migrations;"));
        }

        /// <summary> 실제 DB 경합에서 동일 operation은 효과와 결과를 한 번만 저장한다 </summary>
        [PostgresFact]
        public async Task ExecuteAsync_ConcurrentSameKey_SavesOneEffectAndOneResult()
        {
            await using PostgresTestDatabase database = await CreateEffectDatabaseAsync();
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource));
            OperationRequest request = CreateRequest();
            TaskCompletionSource<bool> entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<OperationDecision> Effect(OperationExecutionContext context, CancellationToken cancellationToken)
            {
                await InsertEffectAsync(context, request, cancellationToken);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
                return OperationDecision.Committed(TEST_REVISION, RECEIPT_ID);
            }

            Task<OperationResult>[] requests = Enumerable.Range(0, CONCURRENT_CALLS)
                .Select(_ => executor.ExecuteAsync(request, Effect, timeout.Token)).ToArray();
            await entered.Task.WaitAsync(timeout.Token);
            release.SetResult(true);
            OperationResult[] results = await Task.WhenAll(requests).WaitAsync(timeout.Token);

            Assert.All(results, result =>
            {
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
                Assert.Equal(RECEIPT_ID, result.ReceiptId);
                Assert.Equal(TEST_REVISION, result.CommittedRevision);
                Assert.Equal(results[0].ServerTimeUnixMs, result.ServerTimeUnixMs);
            });
            Assert.Equal(1L, await CountRowsAsync(database, "test_effects"));
            Assert.Equal(1L, await CountRowsAsync(database, "operations"));
        }

        /// <summary> 다른 본문은 거절되며 다른 행위자는 같은 operationId로 독립 실행한다 </summary>
        [PostgresFact]
        public async Task ExecuteAsync_ChangedBodyAndOtherActor_EnforcesActorPartition()
        {
            await using PostgresTestDatabase database = await CreateEffectDatabaseAsync();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource));
            OperationRequest first = CreateRequest();
            OperationRequest changed = new OperationRequest(first.ActorId, first.OperationId, OPERATION_KIND, DIFFERENT_BODY);
            OperationRequest otherActor = new OperationRequest(Guid.NewGuid(), first.OperationId, OPERATION_KIND, DIFFERENT_BODY);

            await executor.ExecuteAsync(first, async (context, cancellationToken) =>
            {
                await InsertEffectAsync(context, first, cancellationToken);
                return OperationDecision.Committed(TEST_REVISION, "first-receipt");
            });
            OperationResult conflict = await executor.ExecuteAsync(changed, (context, cancellationToken) =>
                throw new InvalidOperationException("Conflicting requests must not run effects."));
            OperationResult other = await executor.ExecuteAsync(otherActor, async (context, cancellationToken) =>
            {
                await InsertEffectAsync(context, otherActor, cancellationToken);
                return OperationDecision.Committed(TEST_REVISION, "other-receipt");
            });

            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, other.Status);
            Assert.Equal("other-receipt", other.ReceiptId);
            Assert.Equal(2L, await CountRowsAsync(database, "test_effects"));
            Assert.Equal(2L, await CountRowsAsync(database, "operations"));
        }

        /// <summary> 효과 저장 뒤 예외가 나면 효과와 Pending 행을 모두 되돌린다 </summary>
        [PostgresFact]
        public async Task ExecuteAsync_EffectThrows_RollsBackPendingRecordAndEffect()
        {
            await using PostgresTestDatabase database = await CreateEffectDatabaseAsync();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource));
            OperationRequest request = CreateRequest();

            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(request,
                async (context, cancellationToken) =>
                {
                    await InsertEffectAsync(context, request, cancellationToken);
                    throw new InvalidOperationException("Test effect failed.");
                }));

            Assert.Equal(0L, await CountRowsAsync(database, "test_effects"));
            Assert.Equal(0L, await CountRowsAsync(database, "operations"));
            OperationResult retry = await executor.ExecuteAsync(request, async (context, cancellationToken) =>
            {
                await InsertEffectAsync(context, request, cancellationToken);
                return OperationDecision.Committed(TEST_REVISION, RECEIPT_ID);
            });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, retry.Status);
            Assert.Equal(1L, await CountRowsAsync(database, "test_effects"));
            Assert.Equal(1L, await CountRowsAsync(database, "operations"));
        }

        /// <summary> 거절은 효과를 저장점으로 취소하고 응답만 확정해 재전송 때 재실행하지 않는다 </summary>
        [PostgresFact]
        public async Task ExecuteAsync_RejectedAfterEffect_CommitsOnlyStableRejection()
        {
            await using PostgresTestDatabase database = await CreateEffectDatabaseAsync();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource));
            OperationRequest request = CreateRequest();

            OperationResult first = await executor.ExecuteAsync(request, async (context, cancellationToken) =>
            {
                await InsertEffectAsync(context, request, cancellationToken);
                return OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM);
            });
            OperationResult replay = await executor.ExecuteAsync(request, (context, cancellationToken) =>
                throw new InvalidOperationException("Rejected replays must not run effects."));

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, replay.Status);
            Assert.Equal(first.ReasonCode, replay.ReasonCode);
            Assert.Equal(first.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(0L, await CountRowsAsync(database, "test_effects"));
            Assert.Equal(1L, await CountRowsAsync(database, "operations"));
        }

        /// <summary> 계정별 동시 임대 경합에서 한 연결만 승인되고 다른 연결은 명시적으로 거절한다 </summary>
        [PostgresFact]
        public async Task TryAcquireAsync_ConcurrentSameAccount_RejectsSecondActiveConnection()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            Guid instanceId = Guid.NewGuid();
            await SeedAccountAndServerAsync(database, accountId, instanceId);
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(database.DataSource);

            AccountLeaseOutcome[] results = await Task.WhenAll(
                leases.TryAcquireAsync(accountId, Guid.NewGuid(), instanceId, ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(1)),
                leases.TryAcquireAsync(accountId, Guid.NewGuid(), instanceId, ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(1)));

            AccountLeaseOutcome acquired = Assert.Single(results, result => result.Status == EAccountLeaseStatus.Acquired);
            AccountLeaseOutcome rejected = Assert.Single(results, result => result.Status == EAccountLeaseStatus.AlreadyConnected);
            Assert.NotNull(acquired.Lease);
            Assert.Null(rejected.Lease);
            Assert.Equal("이미 접속 중인 계정", rejected.Message);
            Assert.Equal(1L, await CountRowsAsync(database, "account_leases"));
        }

        /// <summary> 이전 임대 ID로 새 연결을 갱신하거나 해제하지 못하고 만료된 연결은 갱신하지 못한다 </summary>
        [PostgresFact]
        public async Task RenewAsync_StaleOrExpiredLease_DoesNotModifyCurrentOwner()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            Guid instanceId = Guid.NewGuid();
            Guid leaseId = Guid.NewGuid();
            await SeedAccountAndServerAsync(database, accountId, instanceId);
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(database.DataSource);
            AccountLeaseOutcome acquired = await leases.TryAcquireAsync(accountId, leaseId, instanceId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(1));
            Assert.Equal(EAccountLeaseStatus.Acquired, acquired.Status);

            Assert.False(await leases.RenewAsync(accountId, Guid.NewGuid(), instanceId, TimeSpan.FromMinutes(1)));
            Assert.False(await leases.ReleaseAsync(accountId, Guid.NewGuid(), instanceId));
            Assert.True(await leases.RenewAsync(accountId, leaseId, instanceId, TimeSpan.FromMinutes(1)));

            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("UPDATE account_leases SET expires_at = clock_timestamp() - interval '1 second';");
            Assert.False(await leases.RenewAsync(accountId, leaseId, instanceId, TimeSpan.FromMinutes(1)));
            Guid replacementId = Guid.NewGuid();
            AccountLeaseOutcome replacement = await leases.TryAcquireAsync(accountId, replacementId, instanceId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(1));
            Assert.Equal(EAccountLeaseStatus.Acquired, replacement.Status);
            Assert.False(await leases.ReleaseAsync(accountId, leaseId, instanceId));
            Assert.True(await leases.ReleaseAsync(accountId, replacementId, instanceId));
        }

        /// <summary> DB CHECK 제약으로 재화 음수와 캐릭터 잘못된 직업 코드를 거절한다 </summary>
        [PostgresFact]
        public async Task Insert_InvalidEconomyOrClass_CheckConstraintsRejectValues()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);", new { AccountId = accountId });

            PostgresException wallet = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "INSERT INTO account_wallets (account_id, currency) VALUES (@AccountId, -1);", new { AccountId = accountId }));
            PostgresException character = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "INSERT INTO characters (character_id, account_id, class_id) VALUES (@CharacterId, @AccountId, -1);",
                new { CharacterId = Guid.NewGuid(), AccountId = accountId }));

            Assert.Equal(PostgresErrorCodes.CheckViolation, wallet.SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, character.SqlState);
            Assert.Equal(0L, await CountRowsAsync(database, "account_wallets"));
            Assert.Equal(0L, await CountRowsAsync(database, "characters"));
        }

        /// <summary> Draining 서버의 기존 연결은 유지하고 heartbeat가 끊어진 뒤에만 다른 서버로 대체한다 </summary>
        [PostgresFact]
        public async Task TryAcquireAsync_DrainingThenStaleServer_PreservesOwnerUntilHeartbeatExpires()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            Guid originalInstance = Guid.NewGuid();
            Guid replacementInstance = Guid.NewGuid();
            Guid originalLease = Guid.NewGuid();
            await SeedAccountAndServerAsync(database, accountId, originalInstance);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state)
                VALUES (@InstanceId, @Role, '127.0.0.1', @Port, 'test-build', 'test-content', 'Idle');
                """, new { InstanceId = replacementInstance, Role = ContractCodes.ServerRole.TOWN, Port = TEST_PORT });
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(database.DataSource);
            AccountLeaseOutcome original = await leases.TryAcquireAsync(accountId, originalLease, originalInstance,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(1));
            Assert.Equal(EAccountLeaseStatus.Acquired, original.Status);

            await connection.ExecuteAsync("UPDATE server_instances SET state = 'Draining' WHERE instance_id = @InstanceId;",
                new { InstanceId = originalInstance });
            Assert.True(await leases.RenewAsync(accountId, originalLease, originalInstance, TimeSpan.FromMinutes(1)));
            AccountLeaseOutcome blocked = await leases.TryAcquireAsync(accountId, Guid.NewGuid(), replacementInstance,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(1));
            Assert.Equal(EAccountLeaseStatus.AlreadyConnected, blocked.Status);

            await connection.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '1 minute' WHERE instance_id = @InstanceId;",
                new { InstanceId = originalInstance });
            Assert.False(await leases.RenewAsync(accountId, originalLease, originalInstance, TimeSpan.FromMinutes(1)));
            Guid replacementLease = Guid.NewGuid();
            AccountLeaseOutcome replacement = await leases.TryAcquireAsync(accountId, replacementLease, replacementInstance,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(1));
            Assert.Equal(EAccountLeaseStatus.Acquired, replacement.Status);
            Assert.NotNull(replacement.Lease);
            Assert.Equal(replacementLease, replacement.Lease.LeaseId);
            Assert.False(await leases.ReleaseAsync(accountId, originalLease, originalInstance));
            Assert.Equal(1L, await CountRowsAsync(database, "account_leases"));
        }

        /// <summary> 하위 게임 세션은 다른 계정의 부모 토큰을 참조하지 못한다 </summary>
        [PostgresFact]
        public async Task Insert_GameSessionWithOtherAccountParentToken_RejectsCrossAccountLink()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid parentAccount = Guid.NewGuid();
            Guid otherAccount = Guid.NewGuid();
            Guid parentToken = Guid.NewGuid();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts (account_id) VALUES (@ParentAccount), (@OtherAccount);
                INSERT INTO auth_token_families (family_id, account_id, origin) VALUES (@ParentToken, @ParentAccount, 'invite');
                INSERT INTO auth_refresh_tokens (token_id, account_id, family_id, token_hash, expires_at)
                VALUES (@ParentToken, @ParentAccount, @ParentToken, decode(repeat('01', 32), 'hex'),
                    clock_timestamp() + interval '1 hour');
                """, new { ParentAccount = parentAccount, OtherAccount = otherAccount, ParentToken = parentToken });

            PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                """
                INSERT INTO auth_game_sessions (game_session_id, parent_token_id, account_id, expires_at)
                VALUES (@SessionId, @ParentToken, @OtherAccount, clock_timestamp() + interval '1 hour');
                """, new { SessionId = Guid.NewGuid(), ParentToken = parentToken, OtherAccount = otherAccount }));

            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
            Assert.Equal(0L, await CountRowsAsync(database, "auth_game_sessions"));
        }

        private static OperationRequest CreateRequest()
        {
            return new OperationRequest(Guid.NewGuid(), Guid.NewGuid(), OPERATION_KIND, ORIGINAL_BODY);
        }

        private static async Task<PostgresTestDatabase> CreateEffectDatabaseAsync()
        {
            PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            try
            {
                await using NpgsqlCommand create = database.DataSource.CreateCommand(
                    "CREATE TABLE test_effects (actor_id uuid NOT NULL, operation_id uuid NOT NULL);");
                await create.ExecuteNonQueryAsync();
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        private static async Task InsertEffectAsync(OperationExecutionContext context, OperationRequest request,
            CancellationToken cancellationToken)
        {
            Assert.NotNull(context.Connection);
            Assert.NotNull(context.Transaction);
            await context.Connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO test_effects (actor_id, operation_id) VALUES (@ActorId, @OperationId);",
                new { request.ActorId, request.OperationId }, context.Transaction, cancellationToken: cancellationToken));
        }

        private static async Task<long> CountRowsAsync(PostgresTestDatabase database, string table)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<long>("SELECT count(*) FROM " + table + ";");
        }

        private static async Task SeedAccountAndServerAsync(PostgresTestDatabase database, Guid accountId, Guid instanceId)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts (account_id) VALUES (@AccountId);
                INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state)
                VALUES (@InstanceId, @Role, '127.0.0.1', @Port, 'test-build', 'test-content', 'Idle');
                """, new { AccountId = accountId, InstanceId = instanceId, Role = ContractCodes.ServerRole.TOWN, Port = TEST_PORT });
        }
    }
}
