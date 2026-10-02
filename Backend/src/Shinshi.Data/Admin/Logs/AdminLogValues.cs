// System
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace Shinshi.Data.Admin
{
    /// <summary> 어드민 로그 조회가 함께 쓰는 값 변환과 읽기 트랜잭션 </summary>
    internal static class AdminLogValues
    {
        /****************************************
        *                Fields
        ****************************************/

        // 요청 IP HMAC에서 보여 줄 앞 바이트 수(hex 8자). 같은 출처인지 대조만 할 수 있게 한다
        private const int IP_HASH_PREFIX_BYTES = 4;

        /****************************************
        *                Methods
        ****************************************/

        public static long ToUnixMs(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        public static long? ToUnixMs(DateTime? value)
        {
            return value.HasValue ? ToUnixMs(value.Value) : null;
        }

        public static string? ToText(Guid? value)
        {
            return value?.ToString("D");
        }

        // jsonb를 다시 파싱하지 않고 그대로 싣는다. 파싱하면 날짜처럼 보이는 문자열과 큰 수가 바뀔 수 있다
        public static JToken? ToJson(string? jsonText)
        {
            return jsonText == null ? null : new JRaw(jsonText);
        }

        public static string? ToHashPrefix(byte[]? hash)
        {
            if ((hash == null) || (hash.Length < IP_HASH_PREFIX_BYTES))
            {
                return null;
            }

            return Convert.ToHexString(hash, 0, IP_HASH_PREFIX_BYTES).ToLowerInvariant();
        }

        /// <summary> 여러 조회가 한 snapshot을 보도록 읽기 전용 REPEATABLE READ 트랜잭션을 연다. 문장 제한은 쓰기 경로와 같다 </summary>
        public static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> BeginReadAsync(
            NpgsqlDataSource dataSource, CancellationToken cancellationToken)
        {
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
            NpgsqlTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
                await connection.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY;", transaction: transaction,
                    cancellationToken: cancellationToken));
                await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
                return (connection, transaction);
            }
            catch
            {
                // 호출자에게 넘기기 전에 실패했으므로 여기서 연결까지 정리한다
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
                await connection.DisposeAsync();
                throw;
            }
        }
    }
}
