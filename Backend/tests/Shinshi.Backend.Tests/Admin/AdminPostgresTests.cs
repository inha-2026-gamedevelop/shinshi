// System
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 실제 PostgreSQL에서 부트스트랩, 로그인 잠금, TOTP 재사용, 감사 로그 추가 전용과 같은 트랜잭션 기록을 검사한다 </summary>
    public sealed class AdminPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OWNER_NAME            = "owner.one";
        private const string TEST_KIND             = "admin.test.effect";
        private const string TEST_REASON           = "integration test";
        private const string WRONG_PASSWORD        = "wrong password value";
        private const int    CONCURRENT_BOOTSTRAPS = 4;
        private const int    DISABLE_ROUNDS        = 5;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task BootstrapOwner_EmptyThenRepeatedAndConcurrent_CreatesExactlyOneOwner()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            Task<bool>[] attempts = Enumerable.Range(0, CONCURRENT_BOOTSTRAPS)
                .Select(index => fixture.Users.BootstrapOwnerAsync(AdminTestSupport.CreateOperator(fixture.Protector,
                    OWNER_NAME + index, EAdminRole.Owner, AdminTotp.CreateSecret(), null)))
                .ToArray();

            bool[] results = await Task.WhenAll(attempts);
            bool repeated = await fixture.Users.BootstrapOwnerAsync(AdminTestSupport.CreateOperator(fixture.Protector,
                "another.owner", EAdminRole.Owner, AdminTotp.CreateSecret(), null));

            Assert.Equal(1, results.Count(created => created));
            Assert.False(repeated);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource, "SELECT count(*) FROM admin_users;"));
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE actor_kind = 'System' AND admin_id IS NULL AND action = @Action;",
                new { Action = AdminAuditActions.BOOTSTRAP_OWNER }));
        }

        [PostgresFact]
        public async Task BootstrapCommand_SecondRun_PrintsSecretsOnceAndFails()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AdminOptions options = AdminTestSupport.CreateOptions();
            using StringWriter firstOutput = new StringWriter();
            using StringWriter firstError = new StringWriter();
            using StringWriter secondOutput = new StringWriter();
            using StringWriter secondError = new StringWriter();

            int first = await AdminBootstrapCommand.RunAsync(database.DataSource, options, "Owner.One", firstOutput, firstError);
            int second = await AdminBootstrapCommand.RunAsync(database.DataSource, options, "owner.two", secondOutput,
                secondError);

            Assert.Equal(0, first);
            Assert.Contains("loginName: owner.one", firstOutput.ToString());
            Assert.Contains("password: ", firstOutput.ToString());
            Assert.Contains("otpauth://totp/", firstOutput.ToString());
            Assert.Equal(string.Empty, firstError.ToString());
            Assert.Equal(1, second);
            Assert.Equal(string.Empty, secondOutput.ToString());
            Assert.DoesNotContain("password", secondError.ToString());
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource, "SELECT count(*) FROM admin_users;"));
        }

        [PostgresFact]
        public async Task Login_ValidThenReusedTotp_IssuesOneSessionAndRejectsReplay()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            byte[] secret = AdminTotp.CreateSecret();
            AdminNewOperator owner = await fixture.BootstrapAsync(secret);
            string code = AdminTestSupport.CodeAt(secret, fixture.Clock.Now);

            AdminLoginOutcome first = await fixture.Sessions.LoginAsync("OWNER.ONE", AdminTestSupport.TEST_PASSWORD, code, null);
            AdminLoginOutcome replay = await fixture.Sessions.LoginAsync(OWNER_NAME, AdminTestSupport.TEST_PASSWORD, code, null);
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(AdminTotp.STEP_SECONDS);
            AdminLoginOutcome next = await fixture.Sessions.LoginAsync(OWNER_NAME, AdminTestSupport.TEST_PASSWORD,
                AdminTestSupport.CodeAt(secret, fixture.Clock.Now), null);

            Assert.True(first.IsSuccess);
            Assert.Equal(owner.AdminId, first.Identity!.AdminId);
            Assert.Equal(EAdminRole.Owner, first.Identity.Role);
            Assert.False(replay.IsSuccess);
            Assert.True(next.IsSuccess);
            AdminIdentity? validated = await fixture.Sessions.ValidateAsync(first.AccessToken);
            Assert.NotNull(validated);
            Assert.Equal(first.Identity.SessionId, validated.SessionId);
            Assert.Null(await fixture.Sessions.ValidateAsync(new AuthTokenHasher(fixture.AuthOptions).CreateToken()));
            Assert.Equal(2L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_sessions;"));
            Assert.Equal(2L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action;", new { Action = AdminAuditActions.LOGIN }));
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_sessions WHERE token_hash = @Hash;",
                new { Hash = new AuthTokenHasher(fixture.AuthOptions).Hash(AuthTokenHasher.ACCESS_DOMAIN, first.AccessToken) }));
        }

        [PostgresFact]
        public async Task Login_FiveFailures_LocksEvenCorrectCredentialsUntilExpiry()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            byte[] secret = AdminTotp.CreateSecret();
            AdminNewOperator owner = await fixture.BootstrapAsync(secret);

            for (int attempt = 0; attempt < PostgresAdminSessionRepository.MAX_FAILED_ATTEMPTS; ++attempt)
            {
                Assert.False((await fixture.Sessions.LoginAsync(OWNER_NAME, WRONG_PASSWORD,
                    AdminTestSupport.CodeAt(secret, fixture.Clock.Now), null)).IsSuccess);
            }
            AdminLoginOutcome locked = await fixture.Sessions.LoginAsync(OWNER_NAME, AdminTestSupport.TEST_PASSWORD,
                AdminTestSupport.CodeAt(secret, fixture.Clock.Now), null);
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("UPDATE admin_users SET locked_until = clock_timestamp() - interval '1 second';");
            }
            AdminLoginOutcome unlocked = await fixture.Sessions.LoginAsync(OWNER_NAME, AdminTestSupport.TEST_PASSWORD,
                AdminTestSupport.CodeAt(secret, fixture.Clock.Now), null);

            Assert.False(locked.IsSuccess);
            Assert.True(unlocked.IsSuccess);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action AND admin_id = @AdminId;",
                new { Action = AdminAuditActions.LOCKOUT, owner.AdminId }));
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT failed_attempts::bigint FROM admin_users;"));
        }

        [PostgresFact]
        public async Task Login_UnknownOrDisabled_FailsAndLogoutRevokesSession()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            byte[] secret = AdminTotp.CreateSecret();
            await fixture.BootstrapAsync(secret);
            AdminLoginOutcome login = await fixture.Sessions.LoginAsync(OWNER_NAME, AdminTestSupport.TEST_PASSWORD,
                AdminTestSupport.CodeAt(secret, fixture.Clock.Now), null);

            Assert.False((await fixture.Sessions.LoginAsync("nobody.here", AdminTestSupport.TEST_PASSWORD, "000000", null))
                .IsSuccess);
            Assert.True(await fixture.Sessions.LogoutAsync(login.Identity!, null));
            Assert.False(await fixture.Sessions.LogoutAsync(login.Identity!, null));
            Assert.Null(await fixture.Sessions.ValidateAsync(login.AccessToken));

            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync("UPDATE admin_users SET disabled_at = clock_timestamp();");
            }
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(AdminTotp.STEP_SECONDS);
            Assert.False((await fixture.Sessions.LoginAsync(OWNER_NAME, AdminTestSupport.TEST_PASSWORD,
                AdminTestSupport.CodeAt(secret, fixture.Clock.Now), null)).IsSuccess);
        }

        /// <summary> 비활성화와 같은 순서(운영자 행 FOR UPDATE -> 세션 갱신)를 쥔 트랜잭션과 로그아웃이 겹쳐도 교착하지 않는다.
        /// 로그아웃이 세션 행을 먼저 바꾸면 비활성화의 세션 갱신과 로그아웃의 감사 FK 검사가 서로를 기다려 40P01이 난다 </summary>
        [PostgresFact]
        public async Task Logout_WhileDisableHoldsOperatorRow_WaitsWithoutDeadlock()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            byte[] secret = AdminTotp.CreateSecret();
            AdminNewOperator owner = await fixture.BootstrapAsync(secret);
            AdminLoginOutcome login = await fixture.Sessions.LoginAsync(OWNER_NAME, AdminTestSupport.TEST_PASSWORD,
                AdminTestSupport.CodeAt(secret, fixture.Clock.Now), null);
            Assert.True(login.IsSuccess);

            await using NpgsqlConnection disabler = await database.DataSource.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await disabler.BeginTransactionAsync();
            await disabler.ExecuteAsync("SET LOCAL lock_timeout = '5s';", transaction: transaction);
            await disabler.ExecuteAsync("SELECT 1 FROM admin_users WHERE admin_id = @AdminId FOR UPDATE;",
                new { owner.AdminId }, transaction);
            int disablerPid = await disabler.QuerySingleAsync<int>("SELECT pg_backend_pid();", transaction: transaction);

            Task<bool> logout = fixture.Sessions.LogoutAsync(login.Identity!, null);
            await AdminTestSupport.WaitUntilBlockedByAsync(database.DataSource, disablerPid);
            int revokedByDisable = await disabler.ExecuteAsync(
                "UPDATE admin_sessions SET revoked_at = clock_timestamp() WHERE admin_id = @AdminId AND revoked_at IS NULL;",
                new { owner.AdminId }, transaction);
            await transaction.CommitAsync();

            Assert.Equal(1, revokedByDisable);
            Assert.False(await logout);
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action;", new { Action = AdminAuditActions.LOGOUT }));
        }

        [PostgresFact]
        public async Task AuditLog_UpdateDeleteTruncate_AreRejected()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            await fixture.BootstrapAsync(AdminTotp.CreateSecret());
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            foreach (string sql in new[]
            {
                "UPDATE admin_audit_log SET reason = 'changed';",
                "DELETE FROM admin_audit_log;",
                "TRUNCATE admin_audit_log;"
            })
            {
                PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(sql));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource, "SELECT count(*) FROM admin_audit_log;"));
        }

        [PostgresFact]
        public async Task Executor_AuditIsWrittenOnlyWithCommittedEffect()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            AdminNewOperator owner = await fixture.BootstrapAsync(AdminTotp.CreateSecret());
            AdminIdentity admin = new AdminIdentity(owner.AdminId, EAdminRole.Owner, Guid.NewGuid());
            AdminOperationExecutor executor = fixture.CreateExecutor();
            string committedId = Guid.NewGuid().ToString("D");
            string rejectedId = Guid.NewGuid().ToString("D");
            string failedId = Guid.NewGuid().ToString("D");
            string unauditedId = Guid.NewGuid().ToString("D");

            OperationResult committed = await executor.ExecuteAsync(admin, null, Request(committedId), TEST_KIND, "target",
                async (context, token) =>
                {
                    await context.AuditAsync("test_target", "target", null, new { Value = 1 }, token);
                    return OperationDecision.Committed(0, "receipt");
                });
            OperationResult rejected = await executor.ExecuteAsync(admin, null, Request(rejectedId), TEST_KIND, "target",
                async (context, token) =>
                {
                    await context.AuditAsync("test_target", "target", null, new { Value = 2 }, token);
                    return OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE);
                });
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(admin, null, Request(failedId),
                TEST_KIND, "target", async (context, token) =>
                {
                    await context.AuditAsync("test_target", "target", null, new { Value = 3 }, token);
                    throw new InvalidOperationException("effect failure");
                }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(admin, null,
                Request(unauditedId), TEST_KIND, "target",
                (context, token) => Task.FromResult(OperationDecision.Committed(0, "receipt"))));
            OperationResult replay = await executor.ExecuteAsync(admin, null, Request(committedId), TEST_KIND, "target",
                (context, token) => throw new InvalidOperationException("replay must not run the effect"));
            OperationResult conflict = await executor.ExecuteAsync(admin, null, Request(committedId), TEST_KIND, "other",
                (context, token) => throw new InvalidOperationException("conflict must not run the effect"));

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, committed.Status);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, rejected.Status);
            Assert.Equal(committed.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action;", new { Action = TEST_KIND }));
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log a
                JOIN operations o ON o.actor_id = a.admin_id AND o.operation_id = a.operation_id
                WHERE a.operation_id = @OperationId AND o.status = 'Committed' AND a.reason = @Reason
                    AND a.admin_role = 3 AND a.after_state = '{"value":1}'::jsonb;
                """, new { OperationId = Guid.Parse(committedId), Reason = TEST_REASON }));
            Assert.Equal(2L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM operations WHERE actor_id = @AdminId;", new { owner.AdminId }));
        }

        [PostgresFact]
        public async Task AuditWriter_OperationOutsidePendingTransaction_IsRejected()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            AdminNewOperator owner = await fixture.BootstrapAsync(AdminTotp.CreateSecret());
            AdminIdentity admin = new AdminIdentity(owner.AdminId, EAdminRole.Owner, Guid.NewGuid());
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            AdminAuditEntry entry = AdminAuditEntry.ForAdmin(admin, TEST_KIND, "test_target", "target", TEST_REASON,
                operationId: Guid.NewGuid());

            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.AuditWriter.AppendAsync(
                new OperationExecutionContext(connection, transaction, DateTimeOffset.UtcNow), entry));
            await Assert.ThrowsAsync<ArgumentException>(() => fixture.AuditWriter.AppendStandaloneAsync(entry));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.AuditWriter.AppendAsync(
                new OperationExecutionContext(null, null, DateTimeOffset.UtcNow), entry));
        }

        [PostgresFact]
        public async Task ServiceState_Migration_SeedsSingleMaintenanceRow()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            (bool maintenance, int features, int builds, long revision) = await connection
                .QuerySingleAsync<(bool, int, int, long)>(
                    "SELECT maintenance, cardinality(disabled_features), cardinality(allowed_build_ids), revision FROM service_state;");
            PostgresException duplicate = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "INSERT INTO service_state (id, maintenance) VALUES (2, false);"));

            Assert.True(maintenance);
            Assert.Equal(0, features);
            Assert.Equal(0, builds);
            Assert.Equal(0L, revision);
            Assert.Equal(PostgresErrorCodes.CheckViolation, duplicate.SqlState);
        }

        [PostgresFact]
        public async Task Disable_OwnersDisableEachOtherConcurrently_LeavesOneActiveOwner()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            AdminOperationExecutor executor = fixture.CreateExecutor();

            for (int round = 0; round < DISABLE_ROUNDS; ++round)
            {
                await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
                {
                    await connection.ExecuteAsync(
                        "UPDATE admin_users SET disabled_at = clock_timestamp() WHERE disabled_at IS NULL;");
                }
                AdminNewOperator first = await fixture.InsertAsync("owner.a" + round, EAdminRole.Owner);
                AdminNewOperator second = await fixture.InsertAsync("owner.b" + round, EAdminRole.Owner);

                OperationResult[] results = await Task.WhenAll(
                    DisableAsync(fixture.Users, executor, first.AdminId, second.AdminId),
                    DisableAsync(fixture.Users, executor, second.AdminId, first.AdminId));

                Assert.Equal(1, results.Count(result => result.Status == ContractCodes.OperationStatus.COMMITTED));
                Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE,
                    Assert.Single(results, result => result.Status == ContractCodes.OperationStatus.REJECTED).ReasonCode);
                Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                    "SELECT count(*) FROM admin_users WHERE role = @Owner AND disabled_at IS NULL;",
                    new { Owner = (int)EAdminRole.Owner }));
            }
        }

        [PostgresFact]
        public async Task Disable_LastActiveOwnerByNonOwnerActor_IsRejected()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            AdminOperationExecutor executor = fixture.CreateExecutor();
            AdminNewOperator owner = await fixture.BootstrapAsync(AdminTotp.CreateSecret());

            // 미들웨어는 Owner만 이 경로에 들인다. 역할 검사를 거치지 않고 저장소를 부르는 경우에도 마지막 Owner를 지킨다.
            // 인증 때 역할을 DB와 같은 Admin으로 줘야 역할 하향 거절보다 마지막 Owner 검사까지 간다
            AdminNewOperator admin = await fixture.InsertAsync("admin.only", EAdminRole.Admin);
            OperationResult result = await DisableAsync(fixture.Users, executor, admin.AdminId, owner.AdminId,
                EAdminRole.Admin);

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, result.Status);
            Assert.Equal(AdminContractCodes.RejectReason.INVALID_STATE, result.ReasonCode);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_users WHERE role = @Owner AND disabled_at IS NULL;",
                new { Owner = (int)EAdminRole.Owner }));
        }

        // 컨트롤러와 같이 실행기의 행위자 잠금 없이 부르고 같은 결과 변환을 쓴다
        internal static Task<OperationResult> DisableAsync(PostgresAdminUserRepository users,
            AdminOperationExecutor executor, Guid actorId, Guid targetId, EAdminRole actorRole = EAdminRole.Owner)
        {
            AdminIdentity actor = new AdminIdentity(actorId, actorRole, Guid.NewGuid());
            return executor.ExecuteAsync(actor, null, Request(Guid.NewGuid().ToString("D")),
                AdminOperatorsController.DISABLE_OPERATION_KIND, targetId.ToString("D"),
                async (context, token) => AdminOperatorsController.ToDecision(
                    await users.DisableAsync(context, targetId, token), targetId.ToString("D")),
                lockActor: false);
        }

        private static AdminOperatorDisableRequest Request(string operationId)
        {
            return new AdminOperatorDisableRequest(operationId, TEST_REASON);
        }

        internal sealed class Fixture
        {
            public AuthTokenOptions AuthOptions { get; } = AuthTokenHasherTests.CreateOptions();
            public AdminSecretProtector Protector { get; } = AdminSecretProtector.FromBase64(AdminTestSupport.CreateKeyBase64());
            public ManualTimeProvider Clock { get; } = new ManualTimeProvider();
            public NpgsqlDataSource DataSource { get; }
            public PostgresAdminAuditWriter AuditWriter { get; }
            public PostgresAdminUserRepository Users { get; }
            public PostgresAdminSessionRepository Sessions { get; }

            public Fixture(NpgsqlDataSource dataSource)
            {
                DataSource  = dataSource;
                AuditWriter = new PostgresAdminAuditWriter(dataSource);
                Users       = new PostgresAdminUserRepository(dataSource, AuditWriter);
                Sessions    = new PostgresAdminSessionRepository(dataSource, new AdminTokenHasher(AuthOptions),
                    new AdminPasswordHasher(AdminTestSupport.TEST_ITERATIONS), Protector, AuditWriter,
                    new AdminSecuritySettings(AdminTestSupport.TEST_ITERATIONS, AdminSecuritySettings.DEFAULT_SESSION_MINUTES),
                    Clock);
            }

            public async Task<AdminNewOperator> BootstrapAsync(byte[] secret)
            {
                AdminNewOperator owner = AdminTestSupport.CreateOperator(Protector, OWNER_NAME, EAdminRole.Owner, secret, null);
                Assert.True(await Users.BootstrapOwnerAsync(owner));
                return owner;
            }

            // 부트스트랩은 빈 테이블에서만 되므로 두 번째 Owner부터는 행을 직접 넣는다
            public async Task<AdminNewOperator> InsertAsync(string loginName, EAdminRole role)
            {
                AdminNewOperator admin = AdminTestSupport.CreateOperator(Protector, loginName, role,
                    AdminTotp.CreateSecret(), null);
                await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
                await connection.ExecuteAsync(
                    """
                    INSERT INTO admin_users (admin_id, login_name, password_hash, password_salt, password_iterations,
                        totp_secret_encrypted, role)
                    VALUES (@AdminId, @LoginName, @Hash, @Salt, @Iterations, @Totp, @Role);
                    """, new
                    {
                        admin.AdminId, admin.LoginName, admin.Password.Hash, admin.Password.Salt,
                        admin.Password.Iterations, Totp = admin.TotpSecretEncrypted, Role = (int)role
                    });
                return admin;
            }

            public AdminOperationExecutor CreateExecutor()
            {
                return new AdminOperationExecutor(new IdempotentOperationExecutor(new PostgresOperationStore(DataSource)),
                    AuditWriter, new PostgresAdminOperationClaimRepository(DataSource));
            }
        }
    }
}
