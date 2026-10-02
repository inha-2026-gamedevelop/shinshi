using System;
using System.Threading.Tasks;

using Npgsql;
using Xunit;

using Shinshi.Data;

namespace Shinshi.Backend.Tests
{
    /// <summary> DB 접속 환경이 없을 때 통합 검증 생략 이유를 테스트 결과에 표시한다 </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class PostgresFactAttribute : FactAttribute
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CONNECTION_VARIABLE = "SHINSHI_TEST_CONNECTION_STRING";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> PostgreSQL 테스트 인스턴스가 제공된 경우에만 테스트를 실행한다 </summary>
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CONNECTION_VARIABLE)))
            {
                Skip = "SHINSHI_TEST_CONNECTION_STRING is not set; no PostgreSQL/Docker test instance was supplied.";
            }
        }
    }

    internal sealed class PostgresTestDatabase : IAsyncDisposable
    {
        private const string SCHEMA_PREFIX = "shinshi_test_";

        private readonly NpgsqlDataSource _administration;
        private readonly string _schema;

        public NpgsqlDataSource DataSource { get; }
        public SqlMigrationRunner Migrations { get; }

        private PostgresTestDatabase(NpgsqlDataSource administration, NpgsqlDataSource dataSource, string schema)
        {
            _administration = administration;
            _schema         = schema;
            DataSource      = dataSource;
            Migrations      = new SqlMigrationRunner(dataSource);
        }

        public static async Task<PostgresTestDatabase> CreateAsync(bool applyMigrations = true)
        {
            string? connectionString = Environment.GetEnvironmentVariable(PostgresFactAttribute.CONNECTION_VARIABLE);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("A PostgreSQL test connection must be provided.");
            }

            string schema = SCHEMA_PREFIX + Guid.NewGuid().ToString("N");
            NpgsqlDataSource administration = NpgsqlDataSource.Create(connectionString);
            try
            {
                await using NpgsqlCommand create = administration.CreateCommand("CREATE SCHEMA \"" + schema + "\"");
                await create.ExecuteNonQueryAsync();
            }
            catch
            {
                await administration.DisposeAsync();
                throw;
            }

            NpgsqlConnectionStringBuilder settings = new NpgsqlConnectionStringBuilder(connectionString)
            {
                SearchPath      = schema,
                ApplicationName = "Shinshi.Backend.Tests"
            };
            PostgresTestDatabase database = new PostgresTestDatabase(administration,
                NpgsqlDataSource.Create(settings.ConnectionString), schema);
            try
            {
                if (applyMigrations)
                {
                    await database.Migrations.ApplyAsync();
                }

                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DataSource.DisposeAsync();
            try
            {
                // 이 인스턴스가 만든 GUID 스키마만 지우며 연결 문자열에서 삭제 대상을 받지 않는다
                if ((!_schema.StartsWith(SCHEMA_PREFIX, StringComparison.Ordinal))
                    || (!Guid.TryParseExact(_schema[SCHEMA_PREFIX.Length..], "N", out _)))
                {
                    throw new InvalidOperationException("Invalid test schema identifier.");
                }

                await using NpgsqlCommand drop = _administration.CreateCommand("DROP SCHEMA \"" + _schema + "\" CASCADE");
                await drop.ExecuteNonQueryAsync();
            }
            finally
            {
                await _administration.DisposeAsync();
            }
        }
    }
}
