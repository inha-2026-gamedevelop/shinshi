// System
using System;
using System.Threading.Tasks;

// Third Party
using Npgsql;

namespace Shinshi.Data.Admin
{
    /// <summary> 수동 복구가 서버 복구 전에 잡는 운영자 작업 키 결속. Matches면 같은 키의 실행을 직렬화하는 잠금과 행위자 행의
    /// FOR SHARE 잠금을 쥐고 있고 DisposeAsync에서 놓는다. Matches가 아니면 아무 잠금도 쥐지 않는다 </summary>
    public sealed class AdminOperationClaim : IAsyncDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly NpgsqlConnection? _connection;
        private readonly NpgsqlTransaction? _transaction;

        /****************************************
        *              Properties
        ****************************************/

        // 이 키가 이번 요청과 같은 대상, 같은 요청 해시에 묶여 있는지. 아니면 IdempotencyConflict다
        public bool Matches { get; }

        // 잠근 행위자 행이 아직 활성이고 역할이 인증 때 이상인지. 아니면 서버 복구를 부르지 않는다
        public bool ActorActive { get; }

        public DateTimeOffset ServerTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal AdminOperationClaim(NpgsqlConnection? connection, NpgsqlTransaction? transaction, bool matches,
            bool actorActive, DateTimeOffset serverTime)
        {
            _connection  = connection;
            _transaction = transaction;
            Matches      = matches;
            ActorActive  = actorActive;
            ServerTime   = serverTime;
        }

        public async ValueTask DisposeAsync()
        {
            // 잠금만 잡은 트랜잭션이라 쓴 것이 없다. 커밋하지 않고 버려 잠금을 놓는다
            try
            {
                if (_transaction != null)
                {
                    await _transaction.DisposeAsync();
                }
            }
            finally
            {
                if (_connection != null)
                {
                    await _connection.DisposeAsync();
                }
            }
        }
    }
}
