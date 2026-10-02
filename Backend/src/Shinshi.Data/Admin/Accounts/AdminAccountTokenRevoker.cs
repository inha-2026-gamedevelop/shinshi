// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

namespace Shinshi.Data.Admin
{
    /// <summary> 계정의 로그인 묶음과 하위 토큰을 한 트랜잭션에서 폐기한다. 호출자가 accounts 행을 먼저 잠가야 한다 </summary>
    internal static class AdminAccountTokenRevoker
    {
        /****************************************
        *                Methods
        ****************************************/

        // 인증 저장소의 로그아웃, 갱신, 초대 재발급과 같은 account -> family 잠금 순서를 지킨다. 계정 행 잠금이
        // 그 계정의 family 쓰기를 모두 직렬화하므로 목록을 따로 잠그지 않는다.
        // 이미 폐기된 행은 시각을 덮지 않고 세지도 않는다. 소비된 입장 토큰은 다시 쓸 수 없어 건드리지 않는다
        public static async Task<AdminRevokedTokens> RevokeAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, Guid accountId, bool includeInvites, CancellationToken cancellationToken)
        {
            object parameters = new { AccountId = accountId };
            int families = await ExecuteAsync(connection, transaction,
                "UPDATE auth_token_families SET revoked_at = clock_timestamp() WHERE account_id = @AccountId AND revoked_at IS NULL;",
                parameters, cancellationToken);
            int refresh = await ExecuteAsync(connection, transaction,
                "UPDATE auth_refresh_tokens SET revoked_at = clock_timestamp() WHERE account_id = @AccountId AND revoked_at IS NULL;",
                parameters, cancellationToken);
            int access = await ExecuteAsync(connection, transaction,
                "UPDATE auth_access_tokens SET revoked_at = clock_timestamp() WHERE account_id = @AccountId AND revoked_at IS NULL;",
                parameters, cancellationToken);
            int gameSessions = await ExecuteAsync(connection, transaction,
                "UPDATE auth_game_sessions SET revoked_at = clock_timestamp() WHERE account_id = @AccountId AND revoked_at IS NULL;",
                parameters, cancellationToken);
            int joinTokens = await ExecuteAsync(connection, transaction,
                """
                UPDATE auth_join_tokens SET revoked_at = clock_timestamp()
                WHERE account_id = @AccountId AND revoked_at IS NULL AND consumed_at IS NULL;
                """, parameters, cancellationToken);
            int invites = includeInvites
                ? await ExecuteAsync(connection, transaction,
                    "UPDATE auth_invites SET revoked_at = clock_timestamp() WHERE account_id = @AccountId AND revoked_at IS NULL;",
                    parameters, cancellationToken)
                : 0;
            return new AdminRevokedTokens(families, refresh, access, gameSessions, joinTokens, invites);
        }

        /// <summary> 계정 행을 잠근다. 없으면 false </summary>
        public static async Task<bool> LockAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid accountId, CancellationToken cancellationToken)
        {
            Guid? locked = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT account_id FROM accounts WHERE account_id = @AccountId FOR UPDATE;",
                new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
            return locked.HasValue;
        }

        private static Task<int> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
            object parameters, CancellationToken cancellationToken)
        {
            return connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction,
                cancellationToken: cancellationToken));
        }
    }
}
