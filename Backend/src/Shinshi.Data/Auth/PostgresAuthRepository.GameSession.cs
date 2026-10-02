// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Auth
{
    public sealed partial class PostgresAuthRepository
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 검증된 로그인 주체에서만 부모 수명을 넘지 않는 짧은 게임 하위 세션을 발급한다.
        /// 검증과 발급 사이에 부모가 폐기되면 TOKEN_REVOKED다 </summary>
        public Task<AuthValidationOutcome<IssuedAuthToken>> IssueGameSessionForPlayerAsync(PlayerAuthIdentity identity,
            TimeSpan duration, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ValidatePlayerIdentity(identity);
            ValidateDuration(duration, TimeSpan.FromMinutes(ACCESS_MINUTES));
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                await LockAccountAsync(connection, transaction, identity.AccountId, token);
                if (!await LockActiveIdentityAsync(connection, transaction, identity, token))
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                return AuthValidationOutcome<IssuedAuthToken>.Success(await InsertGameSessionAsync(connection,
                    transaction, identity.RefreshTokenId, duration, token));
            }, cancellationToken);
        }

        /// <summary> 제시한 하위 세션을 폐기하고 같은 부모 아래 새 하위 세션을 한 트랜잭션에서 발급한다.
        /// 계정 -> family 순서로 잠근 뒤 revoked_at IS NULL 조건부 갱신으로 같은 토큰의 동시 회전 중 하나만 이긴다 </summary>
        public Task<AuthValidationOutcome<IssuedAuthToken>> RotateGameSessionAsync(string gameSessionToken,
            TimeSpan duration, CancellationToken cancellationToken = default)
        {
            ValidateDuration(duration, TimeSpan.FromMinutes(ACCESS_MINUTES));
            if (!_hasher.IsAuthentic(gameSessionToken))
            {
                return Task.FromResult(AuthValidationOutcome<IssuedAuthToken>.Rejected(
                    ContractCodes.RejectReason.TOKEN_REVOKED));
            }

            byte[] hash = _hasher.Hash(AuthTokenHasher.GAME_SESSION_DOMAIN, gameSessionToken);
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                GameSessionRow? found = await connection.QuerySingleOrDefaultAsync<GameSessionRow>(Command(
                    """
                    SELECT child.game_session_id AS "GameSessionId", child.account_id AS "AccountId",
                        child.parent_token_id AS "ParentTokenId", parent.family_id AS "FamilyId"
                    FROM auth_game_sessions AS child JOIN auth_refresh_tokens AS parent
                        ON parent.account_id = child.account_id AND parent.token_id = child.parent_token_id
                    WHERE child.token_hash = @Hash;
                    """, new { Hash = hash }, transaction, token));
                if (found == null)
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                // 로그아웃, 갱신 토큰 회전과 같은 account -> family 순서로 잠근다. 같은 토큰의 두 번째 회전은 여기서 기다린다
                await LockAccountAsync(connection, transaction, found.AccountId, token);
                PlayerAuthIdentity parent = new PlayerAuthIdentity(found.AccountId, found.FamilyId, found.ParentTokenId);
                if (!await LockActiveIdentityAsync(connection, transaction, parent, token))
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                int changed = await connection.ExecuteAsync(Command(
                    """
                    UPDATE auth_game_sessions SET revoked_at = clock_timestamp()
                    WHERE game_session_id = @Id AND revoked_at IS NULL AND expires_at > clock_timestamp();
                    """, new { Id = found.GameSessionId }, transaction, token));
                if (changed == 0)
                {
                    int reason = await connection.QuerySingleAsync<int>(Command(
                        """
                        SELECT CASE WHEN revoked_at IS NOT NULL THEN @Revoked ELSE @Expired END
                        FROM auth_game_sessions WHERE game_session_id = @Id;
                        """, new { Id = found.GameSessionId, Revoked = ContractCodes.RejectReason.TOKEN_REVOKED,
                            Expired = ContractCodes.RejectReason.TOKEN_EXPIRED }, transaction, token));
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(reason);
                }

                return AuthValidationOutcome<IssuedAuthToken>.Success(await InsertGameSessionAsync(connection,
                    transaction, found.ParentTokenId, duration, token));
            }, cancellationToken);
        }

        /// <summary> 하위 세션 하나만 폐기한다. 이미 폐기, 만료된 행도 찾으면 true라 재시도가 같은 결과를 받는다 </summary>
        public Task<bool> RevokeGameSessionAsync(string gameSessionToken, CancellationToken cancellationToken = default)
        {
            if (!_hasher.IsAuthentic(gameSessionToken))
            {
                return Task.FromResult(false);
            }

            byte[] hash = _hasher.Hash(AuthTokenHasher.GAME_SESSION_DOMAIN, gameSessionToken);
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                Guid? revoked = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                    """
                    UPDATE auth_game_sessions SET revoked_at = COALESCE(revoked_at, clock_timestamp())
                    WHERE token_hash = @Hash
                    RETURNING game_session_id;
                    """, new { Hash = hash }, transaction, token));
                return revoked.HasValue;
            }, cancellationToken);
        }

        // 호출자가 계정, family를 잠그고 부모가 살아 있음을 확인한 뒤에만 부른다. 만료는 부모 만료를 넘지 않는다
        private async Task<IssuedAuthToken> InsertGameSessionAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, Guid parentTokenId, TimeSpan duration, CancellationToken token)
        {
            DateTime now = await ReadTimeAsync(connection, transaction, token);
            Guid id = Guid.NewGuid();
            string raw = _hasher.CreateToken();
            DateTime expires = now.Add(duration);
            DateTime storedExpires = await connection.QuerySingleAsync<DateTime>(Command(
                """
                INSERT INTO auth_game_sessions(game_session_id,parent_token_id,account_id,token_hash,expires_at)
                SELECT @Id,token_id,account_id,@Hash,LEAST(@Expires,expires_at)
                FROM auth_refresh_tokens WHERE token_id = @ParentId
                RETURNING expires_at;
                """, new { Id = id, ParentId = parentTokenId,
                    Hash = _hasher.Hash(AuthTokenHasher.GAME_SESSION_DOMAIN, raw), Expires = expires },
                transaction, token));
            return new IssuedAuthToken(raw, id, UnixMilliseconds(storedExpires), UnixMilliseconds(now));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class GameSessionRow
        {
            public Guid GameSessionId { get; set; }
            public Guid AccountId { get; set; }
            public Guid ParentTokenId { get; set; }
            public Guid FamilyId { get; set; }
        }
    }
}
