// System
using System;
using System.Threading.Tasks;

// Third-party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Backend.Tests
{
    /// <summary> 실제 PostgreSQL에서 던전 역할 경계와 0002의 적용 및 복원을 검사한다 </summary>
    public sealed class PostgresSchemaConstraintTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    TEST_PORT           = 7777;
        private const string MIGRATION_NAME      = "0002_dungeon_session_constraints";
        private const long   MIGRATION_VERSION   = 2;

        // 현재 스키마의 Reserved 출정은 만료 시각이 필수다
        private const string INSERT_SESSION_SQL  = """
            INSERT INTO game_sessions
                (session_id, mode, map_id, state, starting_count, content_version, instance_id, server_lease, expires_at)
            VALUES (@SessionId, 'Normal', 'test-map', 'Reserved', 1, 'test-content', @InstanceId, @LeaseId,
                clock_timestamp() + interval '150 seconds');
            """;

        // 0002까지 되돌린 스키마에는 0004의 만료 열이 없다
        private const string INSERT_LEGACY_SESSION_SQL = """
            INSERT INTO game_sessions
                (session_id, mode, map_id, state, starting_count, content_version, instance_id, server_lease)
            VALUES (@SessionId, 'Normal', 'test-map', 'Reserved', 1, 'test-content', @InstanceId, @LeaseId);
            """;
        private const string DUPLICATE_INDEX_COUNT_SQL = """
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = current_schema()
                AND indexname IN ('ix_characters_account', 'ix_auth_refresh_tokens_account');
            """;
        private const string ROLE_COLUMN_COUNT_SQL = """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'game_sessions' AND column_name = 'role';
            """;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 0012, 0013은 이벤트 종류 CHECK를 NOT VALID로 건 뒤 VALIDATE한다. 최종 스키마의 CHECK는 검증 완료 상태이고
        /// 범위 밖 종류는 막힌다. 캐릭터 삭제 상한이 읽는 부분 인덱스도 있어야 한다 </summary>
        [PostgresFact]
        public async Task Migrations_EventKindCheckValidatedAndDeleteIndexPresent()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            bool validated = await connection.QuerySingleAsync<bool>(
                """
                SELECT constraint_row.convalidated FROM pg_constraint AS constraint_row
                JOIN pg_class AS table_row ON table_row.oid = constraint_row.conrelid
                JOIN pg_namespace AS schema_row ON schema_row.oid = table_row.relnamespace
                WHERE schema_row.nspname = current_schema() AND table_row.relname = 'session_events'
                    AND constraint_row.conname = 'ck_session_events_event_kind';
                """);
            string definition = await connection.QuerySingleAsync<string>(
                """
                SELECT pg_get_constraintdef(constraint_row.oid) FROM pg_constraint AS constraint_row
                JOIN pg_class AS table_row ON table_row.oid = constraint_row.conrelid
                JOIN pg_namespace AS schema_row ON schema_row.oid = table_row.relnamespace
                WHERE schema_row.nspname = current_schema() AND table_row.relname = 'session_events'
                    AND constraint_row.conname = 'ck_session_events_event_kind';
                """);
            long deleteIndex = await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM pg_indexes
                WHERE schemaname = current_schema() AND indexname = 'ix_characters_account_deleted';
                """);

            Assert.True(validated);
            Assert.Contains("7", definition, StringComparison.Ordinal);
            Assert.DoesNotContain("NOT VALID", definition, StringComparison.Ordinal);
            Assert.Equal(1L, deleteIndex);
        }

        /// <summary> 역할을 생략해도 던전으로 고정하고 마을 서버 참조 및 역할 위조를 거절한다 </summary>
        [PostgresFact]
        public async Task Insert_GameSession_RequiresDungeonServerAndRole()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Guid dungeonId = Guid.NewGuid();
            Guid townId = Guid.NewGuid();
            Guid sessionId = Guid.NewGuid();
            await SeedServersAsync(connection, dungeonId, townId);
            await InsertSessionAsync(connection, sessionId, dungeonId);

            Assert.Equal(ContractCodes.ServerRole.DUNGEON,
                await connection.QuerySingleAsync<int>("SELECT role FROM game_sessions WHERE session_id = @SessionId;",
                    new { SessionId = sessionId }));

            PostgresException townError = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertSessionAsync(connection, Guid.NewGuid(), townId));
            PostgresException roleError = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                """
                INSERT INTO game_sessions
                    (session_id, mode, map_id, state, starting_count, content_version, instance_id, server_lease, role,
                        expires_at)
                VALUES (@SessionId, 'Normal', 'test-map', 'Reserved', 1, 'test-content', @InstanceId, @LeaseId, @Role,
                    clock_timestamp() + interval '150 seconds');
                """, new
                {
                    SessionId = Guid.NewGuid(), InstanceId = townId, LeaseId = Guid.NewGuid(),
                    Role = ContractCodes.ServerRole.TOWN
                }));

            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, townError.SqlState);
            Assert.Equal("fk_game_sessions_instance_role", townError.ConstraintName);
            Assert.Equal(PostgresErrorCodes.CheckViolation, roleError.SqlState);
            Assert.Equal("ck_game_sessions_dungeon_role", roleError.ConstraintName);
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM game_sessions;"));
        }

        /// <summary> 이미 세션이 배정된 서버의 역할을 바꿔 복합 FK를 우회하지 못한다 </summary>
        [PostgresFact]
        public async Task Update_AssignedDungeonToTown_RejectsRoleChange()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Guid dungeonId = Guid.NewGuid();
            await SeedServersAsync(connection, dungeonId, Guid.NewGuid());
            await InsertSessionAsync(connection, Guid.NewGuid(), dungeonId);

            PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "UPDATE server_instances SET role = @Role WHERE instance_id = @InstanceId;",
                new { Role = ContractCodes.ServerRole.TOWN, InstanceId = dungeonId }));

            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
            Assert.Equal(ContractCodes.ServerRole.DUNGEON,
                await connection.QuerySingleAsync<int>("SELECT role FROM server_instances WHERE instance_id = @InstanceId;",
                    new { InstanceId = dungeonId }));
        }

        /// <summary> 중복 일반 인덱스를 제거해도 FK가 사용하는 두 UNIQUE는 보존된다 </summary>
        [PostgresFact]
        public async Task ApplyAsync_InitialSchema_KeepsUniqueIndexesWithoutDuplicateIndexes()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();

            Assert.Equal(0L, await connection.QuerySingleAsync<long>(DUPLICATE_INDEX_COUNT_SQL));
            long uniqueConstraints = await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM pg_constraint constraint_entry
                JOIN pg_class table_entry ON table_entry.oid = constraint_entry.conrelid
                JOIN pg_namespace schema_entry ON schema_entry.oid = table_entry.relnamespace
                WHERE schema_entry.nspname = current_schema() AND constraint_entry.contype = 'u'
                    AND ((table_entry.relname = 'characters'
                            AND pg_get_constraintdef(constraint_entry.oid) = 'UNIQUE (account_id, character_id)')
                        OR (table_entry.relname = 'auth_refresh_tokens'
                            AND pg_get_constraintdef(constraint_entry.oid) = 'UNIQUE (account_id, token_id)'));
                """);

            Assert.Equal(2L, uniqueConstraints);
            Assert.Equal(0, await database.Migrations.ApplyAsync());
        }

        /// <summary> down과 up 사이에도 기존 던전 세션을 보존하고 인덱스를 원래대로 복원한다 </summary>
        [PostgresFact]
        public async Task DownThenUp_ExistingDungeonSession_RestoresIndexesAndPreservesSession()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Guid dungeonId = Guid.NewGuid();
            Guid sessionId = Guid.NewGuid();
            await SeedServersAsync(connection, dungeonId, Guid.NewGuid());
            await InsertSessionAsync(connection, sessionId, dungeonId);

            // 0002 down은 이후 마이그레이션을 먼저 되돌린 상태를 전제로 한다
            await MigrationTestResources.RollBackAboveAsync(connection, MIGRATION_VERSION - 1);

            Assert.Equal(0L, await connection.QuerySingleAsync<long>(ROLE_COLUMN_COUNT_SQL));
            Assert.Equal(2L, await connection.QuerySingleAsync<long>(DUPLICATE_INDEX_COUNT_SQL));
            Assert.Equal(sessionId, await connection.QuerySingleAsync<Guid>("SELECT session_id FROM game_sessions;"));

            await ExecuteMigrationAsync(connection, await MigrationTestResources.ReadUpAsync(MIGRATION_NAME));

            Assert.Equal(0L, await connection.QuerySingleAsync<long>(DUPLICATE_INDEX_COUNT_SQL));
            Assert.Equal(ContractCodes.ServerRole.DUNGEON,
                await connection.QuerySingleAsync<int>("SELECT role FROM game_sessions WHERE session_id = @SessionId;",
                    new { SessionId = sessionId }));
        }

        /// <summary> 기존 마을 배정이 있으면 0002의 인덱스 삭제와 열 추가도 함께 롤백된다 </summary>
        [PostgresFact]
        public async Task Up_ExistingTownSession_RollsBackEntireMigration()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Guid townId = Guid.NewGuid();
            await SeedServersAsync(connection, Guid.NewGuid(), townId);
            await MigrationTestResources.RollBackAboveAsync(connection, MIGRATION_VERSION - 1);
            await connection.ExecuteAsync(INSERT_LEGACY_SESSION_SQL,
                new { SessionId = Guid.NewGuid(), InstanceId = townId, LeaseId = Guid.NewGuid() });
            string upSql = await MigrationTestResources.ReadUpAsync(MIGRATION_NAME);

            await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync())
            {
                PostgresException error = await Assert.ThrowsAsync<PostgresException>(() =>
                    connection.ExecuteAsync(upSql, transaction: transaction));
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
                await transaction.RollbackAsync();
            }

            Assert.Equal(0L, await connection.QuerySingleAsync<long>(ROLE_COLUMN_COUNT_SQL));
            Assert.Equal(2L, await connection.QuerySingleAsync<long>(DUPLICATE_INDEX_COUNT_SQL));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM game_sessions;"));
        }

        private static async Task SeedServersAsync(NpgsqlConnection connection, Guid dungeonId, Guid townId)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state)
                VALUES (@DungeonId, @DungeonRole, '127.0.0.1', @Port, 'test-build', 'test-content', 'Idle'),
                    (@TownId, @TownRole, '127.0.0.1', @Port, 'test-build', 'test-content', 'Idle');
                """, new
                {
                    DungeonId = dungeonId, TownId = townId, Port = TEST_PORT,
                    DungeonRole = ContractCodes.ServerRole.DUNGEON, TownRole = ContractCodes.ServerRole.TOWN
                });
        }

        private static Task InsertSessionAsync(NpgsqlConnection connection, Guid sessionId, Guid instanceId)
        {
            return connection.ExecuteAsync(INSERT_SESSION_SQL,
                new { SessionId = sessionId, InstanceId = instanceId, LeaseId = Guid.NewGuid() });
        }

        private static async Task ExecuteMigrationAsync(NpgsqlConnection connection, string sql)
        {
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync(sql, transaction: transaction);
            await transaction.CommitAsync();
        }
    }
}
