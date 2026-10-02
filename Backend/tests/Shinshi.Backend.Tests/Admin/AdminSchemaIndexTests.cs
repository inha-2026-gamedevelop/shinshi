// System
using System.Threading.Tasks;

// Third Party
using Xunit;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 0008이 어드민 조회용 인덱스와 보정 지급 중복 방지 유일 인덱스를 만드는지 실제 PostgreSQL 카탈로그로 검사한다 </summary>
    public sealed class AdminSchemaIndexTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string INDEX_DEFINITION_SQL =
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = current_schema() AND tablename = @Table AND indexname = @Index;
            """;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Migration_AdminQueryIndexes_Exist()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            (string Table, string Index, string Columns)[] expected =
            {
                ("session_participants", "ix_session_participants_account", "(account_id, session_id)"),
                ("economy_ledger", "ix_economy_ledger_account_time", "(account_id, occurred_at, ledger_id)"),
                ("game_sessions", "ix_game_sessions_state_created", "(state, created_at, session_id)"),
                ("admin_audit_log", "ix_admin_audit_log_target_kind", "(target_kind, occurred_at, audit_id)")
            };

            foreach ((string table, string index, string columns) in expected)
            {
                Assert.Contains(columns, await ReadDefinitionAsync(database, table, index));
            }
        }

        [PostgresFact]
        public async Task Migration_RecoveryCompensationRef_IsUniqueWhileNotCancelled()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();

            string definition = await ReadDefinitionAsync(database, "admin_grants", "ux_admin_grants_recovery_ref");

            Assert.StartsWith("CREATE UNIQUE INDEX", definition);
            Assert.Contains("(source_ref)", definition);
            Assert.Contains("'RecoveryCompensation'", definition);
            Assert.Contains("'Cancelled'", definition);
        }

        private static async Task<string> ReadDefinitionAsync(PostgresTestDatabase database, string table, string index)
        {
            await using Npgsql.NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            string? definition = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<string>(connection,
                INDEX_DEFINITION_SQL, new { Table = table, Index = index });
            Assert.NotNull(definition);
            return definition;
        }
    }
}
