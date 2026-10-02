// System
using System;
using System.Data.Common;

namespace Shinshi.Data
{
    /// <summary> 효과 콜백이 사용하는 동일 DB 연결과 트랜잭션. DB 시각은 UTC이며 테스트 대역만 연결을 생략한다 </summary>
    public sealed class OperationExecutionContext
    {
        /****************************************
        *              Properties
        ****************************************/

        public DbConnection? Connection { get; }
        public DbTransaction? Transaction { get; }
        public DateTimeOffset ServerTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        public OperationExecutionContext(DbConnection? connection, DbTransaction? transaction, DateTimeOffset serverTime)
        {
            if ((connection == null) != (transaction == null))
            {
                throw new ArgumentException("Connection and transaction must be supplied together.");
            }

            Connection  = connection;
            Transaction = transaction;
            ServerTime  = serverTime.ToUniversalTime();
        }
    }
}

