// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Data;

namespace Shinshi.Backend.Tests
{
    /// <summary> 내장 마이그레이션 목록과 역순 down 체인. 마이그레이션이 늘어도 개수와 되돌리기 순서를 하드코딩하지 않는다 </summary>
    internal static class MigrationTestResources
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PREFIX      = "Shinshi.Data.Migrations.";
        private const string UP_SUFFIX   = ".up.sql";
        private const string DOWN_SUFFIX = ".down.sql";
        private const char   SEPARATOR   = '_';

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 번호 오름차순 (버전, 이름). 이름은 접두사와 확장자를 뺀 값이다 </summary>
        public static IReadOnlyList<(long Version, string Name)> All { get; } = typeof(SqlMigrationRunner).Assembly
            .GetManifestResourceNames()
            .Where(resource => resource.StartsWith(PREFIX, StringComparison.Ordinal)
                && resource.EndsWith(UP_SUFFIX, StringComparison.Ordinal))
            .Select(resource => resource.Substring(PREFIX.Length, resource.Length - PREFIX.Length - UP_SUFFIX.Length))
            .Select(name => (long.Parse(name.Substring(0, name.IndexOf(SEPARATOR)), NumberStyles.None,
                CultureInfo.InvariantCulture), name))
            .OrderBy(entry => entry.Item1)
            .ToList();

        public static int Count => All.Count;

        /****************************************
        *                Methods
        ****************************************/

        public static Task<string> ReadUpAsync(string name)
        {
            return ReadAsync(PREFIX + name + UP_SUFFIX);
        }

        public static Task<string> ReadDownAsync(string name)
        {
            return ReadAsync(PREFIX + name + DOWN_SUFFIX);
        }

        /// <summary> keepVersion보다 큰 마이그레이션의 down을 최신부터 역순으로 같은 트랜잭션에서 실행한다 </summary>
        public static async Task RollBackAboveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            long keepVersion)
        {
            foreach ((long _, string name) in All.Where(entry => entry.Version > keepVersion).Reverse())
            {
                await connection.ExecuteAsync(await ReadDownAsync(name), transaction: transaction);
            }
        }

        /// <summary> 독립 트랜잭션으로 keepVersion까지 되돌린다 </summary>
        public static async Task RollBackAboveAsync(NpgsqlConnection connection, long keepVersion)
        {
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await RollBackAboveAsync(connection, transaction, keepVersion);
            await transaction.CommitAsync();
        }

        private static async Task<string> ReadAsync(string resourceName)
        {
            Assembly assembly = typeof(SqlMigrationRunner).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Migration test resource is missing.");
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
    }
}
