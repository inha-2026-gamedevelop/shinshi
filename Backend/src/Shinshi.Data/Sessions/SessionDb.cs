// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json;
using Npgsql;

namespace Shinshi.Data.Sessions
{
    /// <summary> 출정 저장소가 함께 쓰는 잠금 순서, 재시도, 요청 해시 도우미 </summary>
    public static class SessionDb
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_ATTEMPTS   = 3;
        private const int RETRY_DELAY_MS = 50;

        // RFC 4122 이름 기반 UUID의 버전, 변형 비트
        private const int  GUID_BYTES        = 16;
        private const int  VERSION_INDEX     = 7;
        private const int  VARIANT_INDEX     = 8;
        private const byte VERSION_MASK      = 0x0F;
        private const byte NAME_VERSION_BITS = 0x50;
        private const byte VARIANT_MASK      = 0x3F;
        private const byte VARIANT_BITS      = 0x80;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 효과 콜백의 연결과 트랜잭션을 꺼낸다. 출정 저장소는 실제 DB 범위에서만 실행한다 </summary>
        public static (DbConnection Connection, DbTransaction Transaction) Require(OperationExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if ((context.Connection == null) || (context.Transaction == null))
            {
                throw new InvalidOperationException("Session operations require a database transaction.");
            }

            return (context.Connection, context.Transaction);
        }

        /// <summary> 계정 행을 PostgreSQL uuid 오름차순으로 잠근다. 계정 -> 캐릭터 -> 아이템 순서의 첫 단계다 </summary>
        public static async Task<int> LockAccountsAsync(DbConnection connection, DbTransaction transaction,
            IEnumerable<Guid> accountIds, CancellationToken cancellationToken)
        {
            Guid[] ids = accountIds.Distinct().ToArray();
            if (ids.Length == 0)
            {
                return 0;
            }

            // .NET Guid 비교 순서는 PostgreSQL uuid 순서와 다르다. 정렬과 잠금을 한 문장에서 DB 순서로 한다
            IEnumerable<Guid> locked = await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT account_id FROM accounts WHERE account_id = ANY(@Ids) ORDER BY account_id FOR UPDATE;",
                new { Ids = ids }, transaction, cancellationToken: cancellationToken));
            return locked.Count();
        }

        /// <summary> 직렬화 실패, 데드락, 제한 초과만 같은 입력으로 다시 시도한다 </summary>
        public static async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(action);
            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    return await action(cancellationToken);
                }
                catch (Exception exception) when ((attempt < MAX_ATTEMPTS) && !cancellationToken.IsCancellationRequested
                    && PostgresTransactionPolicy.IsRetryable(exception))
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

        /// <summary> 해시용 요청 본문. 키 순서는 해시기가 정규화한다 </summary>
        public static string ToRequestJson(object body)
        {
            ArgumentNullException.ThrowIfNull(body);
            return JsonConvert.SerializeObject(body, Formatting.None);
        }

        /// <summary> 같은 이름에서 항상 같은 UUID를 만든다. API가 스스로 시작하는 복구 작업의 operationId에 쓴다 </summary>
        public static Guid CreateNameBasedGuid(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
            byte[] bytes = new byte[GUID_BYTES];
            Array.Copy(hash, bytes, GUID_BYTES);
            bytes[VERSION_INDEX] = (byte)((bytes[VERSION_INDEX] & VERSION_MASK) | NAME_VERSION_BITS);
            bytes[VARIANT_INDEX] = (byte)((bytes[VARIANT_INDEX] & VARIANT_MASK) | VARIANT_BITS);
            return new Guid(bytes);
        }

        /// <summary> 연결 하나를 열어 쓰기 제한을 적용한 READ COMMITTED 트랜잭션을 시작한다 </summary>
        public static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> BeginAsync(
            NpgsqlDataSource dataSource, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
            NpgsqlTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted,
                    cancellationToken);
                await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
                return (connection, transaction);
            }
            catch
            {
                // 호출자에게 넘기기 전 실패했으므로 여기서 연결까지 정리한다
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
