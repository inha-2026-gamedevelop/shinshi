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

        private async Task<AuthExchangeOutcome> ReplayRotationAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, RefreshRow parent, string parentToken, string requestId, CancellationToken token)
        {
            string refresh = _hasher.CreateRotationToken(AuthTokenHasher.REFRESH_DOMAIN, parentToken, requestId);
            string access = _hasher.CreateRotationToken(AuthTokenHasher.ACCESS_DOMAIN, parentToken, requestId);
            RotationReplayRow? child = await connection.QuerySingleOrDefaultAsync<RotationReplayRow>(Command(
                """
                SELECT refresh.expires_at AS "RefreshExpiresAt", access.expires_at AS "AccessExpiresAt"
                FROM auth_refresh_tokens refresh
                JOIN auth_access_tokens access ON access.refresh_token_id = refresh.token_id
                WHERE refresh.token_id = @ChildId AND refresh.family_id = @FamilyId
                    AND refresh.token_hash = @RefreshHash AND access.token_hash = @AccessHash
                    AND refresh.consumed_at IS NULL AND refresh.revoked_at IS NULL AND access.revoked_at IS NULL;
                """, new { ChildId = parent.ReplacedByTokenId, parent.FamilyId,
                    RefreshHash = _hasher.Hash(AuthTokenHasher.REFRESH_DOMAIN, refresh),
                    AccessHash = _hasher.Hash(AuthTokenHasher.ACCESS_DOMAIN, access) }, transaction, token));
            if (child == null)
            {
                return AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            DateTime now = await ReadTimeAsync(connection, transaction, token);
            if (child.RefreshExpiresAt <= now)
            {
                return AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED);
            }

            return AuthExchangeOutcome.Success(new AuthExchangeResponse(parent.AccountId.ToString("D"), access,
                UnixMilliseconds(child.AccessExpiresAt), refresh, UnixMilliseconds(child.RefreshExpiresAt), UnixMilliseconds(now)));
        }

        private sealed class RotationReplayRow
        {
            public DateTime RefreshExpiresAt { get; set; }
            public DateTime AccessExpiresAt { get; set; }
        }
    }
}
