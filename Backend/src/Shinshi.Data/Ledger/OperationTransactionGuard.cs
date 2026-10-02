// System
using System;
using System.Data;

// Third Party
using Npgsql;

namespace Shinshi.Data.Ledger
{
    /// <summary> 운영 쓰기는 executor가 제공한 활성 PostgreSQL 트랜잭션 안에서만 수행한다 </summary>
    internal static class OperationTransactionGuard
    {
        /****************************************
        *                Methods
        ****************************************/

        public static NpgsqlConnection Require(OperationExecutionContext context, out NpgsqlTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(context);
            if ((context.Connection is not NpgsqlConnection connection)
                || (context.Transaction is not NpgsqlTransaction suppliedTransaction))
            {
                throw new InvalidOperationException("An active PostgreSQL operation transaction is required.");
            }
            if ((connection.State != ConnectionState.Open)
                || (!ReferenceEquals(suppliedTransaction.Connection, connection)))
            {
                throw new InvalidOperationException("The transaction must be active on the supplied connection.");
            }

            transaction = suppliedTransaction;
            return connection;
        }
    }
}
