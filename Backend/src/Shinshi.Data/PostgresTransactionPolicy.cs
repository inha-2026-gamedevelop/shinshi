// System
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Npgsql;

namespace Shinshi.Data
{
    /// <summary> 쓰기 트랜잭션에만 제한을 적용해 풀의 다음 요청에 세션 설정을 남기지 않는다 </summary>
    public static class PostgresTransactionPolicy
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LOCK_TIMEOUT      = "2s";
        private const string STATEMENT_TIMEOUT = "5s";
        // SQLSTATE 08xxx: connection_exception 계열
        private const string CONNECTION_EXCEPTION_CLASS = "08";

        private const string TIMEOUT_SQL       = "SET LOCAL lock_timeout = '" + LOCK_TIMEOUT
            + "'; SET LOCAL statement_timeout = '" + STATEMENT_TIMEOUT + "';";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> BeginTransactionAsync 직후 호출해 잠금 취득 전부터 대기 시간을 제한한다 </summary>
        public static async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            if (!ReferenceEquals(transaction.Connection, connection))
            {
                throw new ArgumentException("The transaction must belong to the supplied connection.", nameof(transaction));
            }

            await using NpgsqlCommand command = new NpgsqlCommand(TIMEOUT_SQL, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary> 사용자 취소는 재시도하지 않고 DB 경합과 제한 초과만 같은 작업 ID로 다시 시도한다 </summary>
        public static bool IsRetryable(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (exception is RetryableDatabaseException)
            {
                return true;
            }
            if (exception is PostgresException postgres)
            {
                return (postgres.SqlState == PostgresErrorCodes.SerializationFailure)
                    || (postgres.SqlState == PostgresErrorCodes.DeadlockDetected)
                    || IsTimeout(postgres);
            }

            return IsTimeout(exception);
        }

        /// <summary> API가 DB에 닿지 못한 실패다. 연결 거부, 소켓 단절, 서버 종료 중 SQLSTATE(08xxx, 57P01~57P03)를 포함한다.
        /// 문장 실행 결과(제약 위반, 잠금 제한)는 DB에 닿은 것이라 포함하지 않는다 </summary>
        public static bool IsConnectionFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (exception is RetryableDatabaseException retryable)
            {
                return IsConnectionFailure(retryable.InnerException!);
            }
            if (exception is PostgresException postgres)
            {
                return postgres.SqlState.StartsWith(CONNECTION_EXCEPTION_CLASS, StringComparison.Ordinal)
                    || (postgres.SqlState == PostgresErrorCodes.AdminShutdown)
                    || (postgres.SqlState == PostgresErrorCodes.CrashShutdown)
                    || (postgres.SqlState == PostgresErrorCodes.CannotConnectNow);
            }

            return (exception is NpgsqlException) || (exception is SocketException)
                || (exception is TimeoutException);
        }

        /// <summary> PostgreSQL 제한 초과와 드라이버 타임아웃을 호출자의 CancellationToken 취소와 구분한다 </summary>
        public static bool IsTimeout(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (exception is PostgresException postgres)
            {
                return (postgres.SqlState == PostgresErrorCodes.LockNotAvailable)
                    || (postgres.SqlState == PostgresErrorCodes.QueryCanceled);
            }
            if (exception is RetryableDatabaseException retryable)
            {
                return IsTimeout(retryable.InnerException!);
            }

            return (exception is TimeoutException)
                || ((exception is NpgsqlException database) && (database.InnerException is TimeoutException));
        }
    }
}
