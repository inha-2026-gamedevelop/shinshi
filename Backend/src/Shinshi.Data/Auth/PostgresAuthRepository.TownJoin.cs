// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Auth
{
    public sealed partial class PostgresAuthRepository
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 로그인 주체가 소유한 캐릭터로 등록된 마을 서버에 들어갈 60초 일회 토큰을 발급한다. 같은 계정의 미교환 마을 토큰은 폐기한다 </summary>
        public async Task<AuthValidationOutcome<IssuedAuthToken>> IssueTownJoinTokenAsync(PlayerAuthIdentity identity,
            Guid characterId, Guid townInstanceId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ValidatePlayerIdentity(identity);
            if ((characterId == Guid.Empty) || (townInstanceId == Guid.Empty))
            {
                return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            AuthValidationOutcome<TownJoinIssuance> outcome = await IssueTownJoinAsync(identity, characterId,
                townInstanceId, cancellationToken);
            return outcome.IsSuccess
                ? AuthValidationOutcome<IssuedAuthToken>.Success(outcome.Identity!.Token)
                : AuthValidationOutcome<IssuedAuthToken>.Rejected(outcome.ReasonCode);
        }

        /// <summary> 본인 캐릭터의 입장권을 발급하고 현재 등록된 프로세스가 살아 있는 마을을 API가 선택한다 </summary>
        public async Task<AuthValidationOutcome<JoinTicket>> IssueTownJoinTokenAsync(PlayerAuthIdentity identity,
            Guid characterId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ValidatePlayerIdentity(identity);
            if (characterId == Guid.Empty)
            {
                return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            AuthValidationOutcome<TownJoinIssuance> outcome = await IssueTownJoinAsync(identity, characterId, null,
                cancellationToken);
            if (!outcome.IsSuccess)
            {
                return AuthValidationOutcome<JoinTicket>.Rejected(outcome.ReasonCode);
            }

            TownJoinIssuance issued = outcome.Identity!;
            return AuthValidationOutcome<JoinTicket>.Success(new JoinTicket(issued.Token.Token,
                issued.Token.ExpiresAtUnixMs, ContractCodes.ServerRole.TOWN, issued.Target.InstanceId.ToString("D"),
                issued.Target.Address, issued.Target.Port, characterId.ToString("D"), string.Empty,
                issued.Token.ServerTimeUnixMs));
        }

        private Task<AuthValidationOutcome<TownJoinIssuance>> IssueTownJoinAsync(PlayerAuthIdentity identity,
            Guid characterId, Guid? townInstanceId, CancellationToken cancellationToken)
        {
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                // 잠금 순서: 계정 -> 로그인 묶음 -> 캐릭터 -> 인스턴스, 임대
                await LockAccountAsync(connection, transaction, identity.AccountId, token);
                if (!await LockActiveIdentityAsync(connection, transaction, identity, token))
                {
                    return AuthValidationOutcome<TownJoinIssuance>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }
                JoinCharacterRow? selected = await LockOwnedCharacterAsync(connection, transaction, identity.AccountId,
                    characterId, token);
                if (selected == null)
                {
                    return AuthValidationOutcome<TownJoinIssuance>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }
                // 새 직업(3~5)은 서버 설정으로 열기 전까지 마을 입장도 거절한다
                if (!_classPolicy.IsEnabled(selected.ClassId))
                {
                    return AuthValidationOutcome<TownJoinIssuance>.Rejected(ContractCodes.RejectReason.FEATURE_DISABLED);
                }

                // 선택과 발급 사이에 등록 boot나 임대가 바뀌지 않도록 같은 트랜잭션에서 잠근다
                TownJoinTarget? target = await connection.QuerySingleOrDefaultAsync<TownJoinTarget>(Command(
                    """
                    SELECT lease.lease_id AS "LeaseId", instance.instance_id AS "InstanceId",
                        instance.address AS "Address", instance.port AS "Port"
                    FROM auth_server_leases AS lease JOIN server_instances AS instance
                        ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                    WHERE (CAST(@TownInstanceId AS uuid) IS NULL OR lease.instance_id = @TownInstanceId)
                        AND lease.role = @Town
                        AND lease.revoked_at IS NULL AND lease.expires_at > clock_timestamp()
                        AND instance.state IN ('Idle','Reserved','Active') AND lease.boot_id = instance.boot_id
                        AND instance.heartbeat_at > clock_timestamp() - @Heartbeat
                    ORDER BY instance.instance_id, lease.expires_at DESC, lease.lease_id DESC
                    LIMIT 1 FOR SHARE OF instance, lease;
                    """, new { TownInstanceId = townInstanceId, Town = ContractCodes.ServerRole.TOWN,
                        Heartbeat = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) }, transaction, token));
                if (target == null)
                {
                    return AuthValidationOutcome<TownJoinIssuance>.Rejected(townInstanceId.HasValue
                        ? ContractCodes.RejectReason.SESSION_MISMATCH : ContractCodes.RejectReason.NO_CAPACITY);
                }

                Guid id = Guid.NewGuid();
                string raw = _hasher.CreateToken();
                DateTime now = await ReadTimeAsync(connection, transaction, token);
                DateTime expires = await connection.QuerySingleAsync<DateTime>(Command(
                    """
                    SELECT LEAST(@RequestedExpires, expires_at) FROM auth_refresh_tokens
                    WHERE account_id = @AccountId AND family_id = @FamilyId AND token_id = @ParentId;
                    """, new { RequestedExpires = now.AddSeconds(JOIN_SECONDS), identity.AccountId, identity.FamilyId,
                        ParentId = identity.RefreshTokenId }, transaction, token));
                if (expires <= now)
                {
                    return AuthValidationOutcome<TownJoinIssuance>.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED);
                }

                await connection.ExecuteAsync(Command(
                    """
                    UPDATE auth_join_tokens SET revoked_at = clock_timestamp()
                        WHERE account_id = @AccountId AND server_role = @Town
                            AND consumed_at IS NULL AND revoked_at IS NULL;
                    INSERT INTO auth_join_tokens(token_id,account_id,family_id,character_id,participant_id,session_id,
                        instance_id,server_lease_id,token_hash,expires_at,server_role)
                    VALUES (@Id,@AccountId,@FamilyId,@CharacterId,NULL,NULL,@TownInstanceId,@LeaseId,@Hash,@Expires,@Town);
                    """, new { Id = id, identity.AccountId, identity.FamilyId, CharacterId = characterId,
                        TownInstanceId = target.InstanceId, target.LeaseId, Town = ContractCodes.ServerRole.TOWN,
                        Hash = _hasher.Hash(AuthTokenHasher.JOIN_DOMAIN, raw), Expires = expires }, transaction, token));
                return AuthValidationOutcome<TownJoinIssuance>.Success(new TownJoinIssuance(new IssuedAuthToken(raw, id,
                    UnixMilliseconds(expires), UnixMilliseconds(now)), target));
            }, cancellationToken);
        }

        private async Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeTownJoinAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, JoinRow found, byte[] hash, Guid serverLeaseId, CancellationToken token)
        {
            Guid instanceId = found.InstanceId;
            await LockAccountAsync(connection, transaction, found.AccountId, token);
            FamilyRow? family = await LockFamilyAsync(connection, transaction, found.AccountId, found.FamilyId, token);
            if ((family == null) || family.RevokedAt.HasValue)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
            }
            JoinCharacterRow? character = await LockOwnedCharacterAsync(connection, transaction, found.AccountId,
                found.CharacterId, token);
            if (character == null)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            Guid? lease = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                """
                SELECT lease.lease_id FROM auth_server_leases AS lease JOIN server_instances AS instance
                    ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                WHERE lease.lease_id = @LeaseId AND lease.instance_id = @InstanceId AND lease.role = @Town
                    AND lease.revoked_at IS NULL AND lease.expires_at > clock_timestamp() AND instance.state <> 'Offline'
                FOR SHARE OF instance, lease;
                """, new { LeaseId = serverLeaseId, InstanceId = instanceId, Town = ContractCodes.ServerRole.TOWN },
                transaction, token));
            if (!lease.HasValue)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            // 이미 소비, 폐기, 만료된 토큰의 재전송은 접속 여부보다 토큰 거절을 먼저 알려 준다. 계정 행을 잠근 뒤라
            // 같은 토큰의 동시 교환과 섞이지 않는다
            bool consumable = await connection.QuerySingleAsync<bool>(Command(
                """
                SELECT consumed_at IS NULL AND revoked_at IS NULL AND expires_at > clock_timestamp()
                FROM auth_join_tokens WHERE token_hash = @Hash;
                """, new { Hash = hash }, transaction, token));
            if (!consumable)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(
                    await ReadUnconsumedReasonAsync(connection, transaction, hash, token));
            }

            // 같은 계정의 새 마을 접속은 같은 마을이어도 거절한다(사용자 결정 7.1). 기존 연결은 서버가 해제 경로로 풀거나
            // heartbeat 연장이 끊겨 만료돼야 한다. heartbeat가 끊긴 서버의 임대는 비정상 종료의 잔여물이라 막지 않는다
            bool connected = await connection.QuerySingleAsync<bool>(Command(
                """
                SELECT EXISTS (SELECT 1 FROM account_leases AS lease JOIN server_instances AS instance
                    ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                    WHERE lease.account_id = @AccountId AND lease.expires_at > clock_timestamp()
                        AND instance.state <> 'Offline'
                        AND instance.heartbeat_at > clock_timestamp() - @Heartbeat);
                """, new { found.AccountId, Heartbeat = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) },
                transaction, token));
            if (connected)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.ALREADY_CONNECTED);
            }

            JoinRow? consumed = await connection.QuerySingleOrDefaultAsync<JoinRow>(Command(
                """
                UPDATE auth_join_tokens SET consumed_at = clock_timestamp()
                WHERE token_hash = @Hash AND instance_id = @InstanceId AND server_role = @Town
                    AND expires_at > clock_timestamp() AND consumed_at IS NULL AND revoked_at IS NULL
                RETURNING account_id AS "AccountId", character_id AS "CharacterId", instance_id AS "InstanceId";
                """, new { Hash = hash, InstanceId = instanceId, Town = ContractCodes.ServerRole.TOWN },
                transaction, token));
            if (consumed == null)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(
                    await ReadUnconsumedReasonAsync(connection, transaction, hash, token));
            }

            // 토큰 소비와 같은 트랜잭션에서 계정 접속 임대를 얻는다. 계정 행을 잠근 채라 다른 입장과 이관이 끼어들지 못한다.
            // 위에서 살아 있는 임대가 없음을 확인했으므로 남은 행(만료, 소실 서버)은 덮어쓴다. 연장은 마을 heartbeat가 한다
            Guid accountLeaseId = Guid.NewGuid();
            await connection.ExecuteAsync(Command(
                """
                INSERT INTO account_leases (account_id, lease_id, instance_id, role, expires_at)
                VALUES (@AccountId, @LeaseId, @InstanceId, @Town, clock_timestamp() + @Duration)
                ON CONFLICT (account_id) DO UPDATE SET lease_id = EXCLUDED.lease_id, instance_id = EXCLUDED.instance_id,
                    role = EXCLUDED.role, expires_at = EXCLUDED.expires_at;
                """, new { consumed.AccountId, LeaseId = accountLeaseId, InstanceId = instanceId,
                    Town = ContractCodes.ServerRole.TOWN, Duration = ServerPoolTimings.AccountLease }, transaction, token));

            // 마을 입장은 출정과 참가자가 없다. 빈 ID는 응답에서 값 없음으로 바뀐다
            DateTime now = await ReadTimeAsync(connection, transaction, token);
            return AuthValidationOutcome<JoinAuthIdentity>.Success(new JoinAuthIdentity(consumed.AccountId,
                consumed.CharacterId, character.ClassId, Guid.Empty, Guid.Empty, consumed.InstanceId, UnixMilliseconds(now),
                accountLeaseId, character.Name, appearanceJson: character.Appearance));
        }

        // 계정 소유 캐릭터를 잠그고 직업, 닉네임, 외형 원문을 돌려준다, 소유하지 않으면 null.
        // name/appearance는 레거시 행에서 NULL일 수 있고, 그 NULL 자체가 표시 resolver가 읽는 유효한 값이다
        private static Task<JoinCharacterRow?> LockOwnedCharacterAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, Guid accountId, Guid characterId, CancellationToken token)
        {
            return connection.QuerySingleOrDefaultAsync<JoinCharacterRow>(Command(
                """
                SELECT class_id AS "ClassId", name AS "Name", appearance::text AS "Appearance" FROM characters
                WHERE account_id = @AccountId AND character_id = @CharacterId AND deleted_at IS NULL FOR SHARE;
                """,
                new { AccountId = accountId, CharacterId = characterId }, transaction, token));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed record TownJoinIssuance(IssuedAuthToken Token, TownJoinTarget Target);

        private sealed class TownJoinTarget
        {
            public Guid LeaseId { get; set; }
            public Guid InstanceId { get; set; }
            public string Address { get; set; } = string.Empty;
            public int Port { get; set; }
        }
    }
}
