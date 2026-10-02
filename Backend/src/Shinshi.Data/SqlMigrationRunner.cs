// System
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data
{
    /// <summary> 내장 SQL을 순서대로 적용한다. 이력과 up/down 체크섬은 같은 트랜잭션에 저장하고 DB 잠금으로 동시 적용을 막는다 </summary>
    public sealed class SqlMigrationRunner
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long MIGRATION_LOCK_KEY = 0x5348494E534849;
        private const int MIGRATION_TIMEOUT_SECONDS = 60;
        private const string RESOURCE_PREFIX = "Shinshi.Data.Migrations.";
        private const string UP_SUFFIX = ".up.sql";
        private const string DOWN_SUFFIX = ".down.sql";

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public SqlMigrationRunner(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 로컬 개발 DB에만 호출한다. 이미 적용한 SQL 변경, 알 수 없는 이력, 순번 누락은 오류로 중단한다 </summary>
        public async Task<int> ApplyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await ApplyOnceAsync(cancellationToken);
            }
            catch (Exception exception) when (PostgresTransactionPolicy.IsTimeout(exception))
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new RetryableDatabaseException(exception);
            }
        }

        private async Task<int> ApplyOnceAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<SqlMigration> migrations = ReadMigrations();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(@Key);",
                new { Key = MIGRATION_LOCK_KEY }, transaction, MIGRATION_TIMEOUT_SECONDS,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                CREATE TABLE IF NOT EXISTS schema_migrations
                (
                    version bigint PRIMARY KEY CHECK (version > 0),
                    name text NOT NULL,
                    checksum text NOT NULL CHECK (checksum ~ '^[0-9A-F]{64}$'),
                    applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
                );
                """, transaction: transaction, commandTimeout: MIGRATION_TIMEOUT_SECONDS,
                cancellationToken: cancellationToken));
            List<MigrationRow> applied = (await connection.QueryAsync<MigrationRow>(new CommandDefinition(
                "SELECT version AS \"Version\", name AS \"Name\", checksum AS \"Checksum\" FROM schema_migrations ORDER BY version;",
                transaction: transaction, commandTimeout: MIGRATION_TIMEOUT_SECONDS,
                cancellationToken: cancellationToken))).ToList();
            ValidateHistory(migrations, applied);

            int count = 0;
            foreach (SqlMigration migration in migrations.Skip(applied.Count))
            {
                await connection.ExecuteAsync(new CommandDefinition(migration.UpSql, transaction: transaction,
                    commandTimeout: MIGRATION_TIMEOUT_SECONDS, cancellationToken: cancellationToken));
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO schema_migrations (version, name, checksum) VALUES (@Version, @Name, @Checksum);",
                    migration, transaction, MIGRATION_TIMEOUT_SECONDS, cancellationToken: cancellationToken));
                ++count;
            }
            await transaction.CommitAsync(cancellationToken);
            return count;
        }

        private static IReadOnlyList<SqlMigration> ReadMigrations()
        {
            Assembly assembly = typeof(SqlMigrationRunner).Assembly;
            List<SqlMigration> migrations = new List<SqlMigration>();
            foreach (string resource in assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(RESOURCE_PREFIX, StringComparison.Ordinal)
                    && name.EndsWith(UP_SUFFIX, StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal))
            {
                string name = resource.Substring(RESOURCE_PREFIX.Length,
                    resource.Length - RESOURCE_PREFIX.Length - UP_SUFFIX.Length);
                int separator = name.IndexOf('_');
                if ((separator <= 0) || !long.TryParse(name.AsSpan(0, separator), NumberStyles.None,
                    CultureInfo.InvariantCulture, out long version))
                {
                    throw new InvalidOperationException("Migration resources must use a numbered name.");
                }
                string downResource = RESOURCE_PREFIX + name + DOWN_SUFFIX;
                string upSql = ReadResource(assembly, resource);
                string downSql = ReadResource(assembly, downResource);
                string checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    upSql + "\n-- down migration --\n" + downSql)));
                migrations.Add(new SqlMigration(version, name, upSql, checksum));
            }

            migrations.Sort((left, right) => left.Version.CompareTo(right.Version));
            long expected = 1;
            foreach (SqlMigration migration in migrations)
            {
                if (migration.Version != expected)
                {
                    throw new InvalidOperationException("Migration sequence contains a gap or duplicate.");
                }
                ++expected;
            }
            if (migrations.Count == 0)
            {
                throw new InvalidOperationException("No SQL migrations are embedded in this assembly.");
            }
            return migrations;
        }

        private static string ReadResource(Assembly assembly, string name)
        {
            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Migration is missing its required up/down SQL resource.");
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private static void ValidateHistory(IReadOnlyList<SqlMigration> migrations, IReadOnlyList<MigrationRow> applied)
        {
            if (applied.Count > migrations.Count)
            {
                throw new InvalidOperationException("Database migration history is newer than this application.");
            }
            for (int index = 0; index < applied.Count; ++index)
            {
                if ((applied[index].Version != migrations[index].Version)
                    || !string.Equals(applied[index].Name, migrations[index].Name, StringComparison.Ordinal)
                    || !string.Equals(applied[index].Checksum, migrations[index].Checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Applied migration history or SQL checksum has changed.");
                }
            }
        }

        private sealed class MigrationRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public long Version { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Checksum { get; set; } = string.Empty;
        }

        private sealed record SqlMigration(long Version, string Name, string UpSql, string Checksum);
    }
}


