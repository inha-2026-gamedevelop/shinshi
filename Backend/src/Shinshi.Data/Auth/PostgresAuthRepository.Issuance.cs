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
        *                Fields
        ****************************************/

        // 서버 임대가 처음 만든 등록 전 인스턴스 행의 자리 값. 등록이 실제 값으로 덮는다
        private const string UNREGISTERED_TEXT = "unregistered";
        private const int    UNREGISTERED_PORT = 1;
        private const string INSTANCE_OFFLINE  = "Offline";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 운영 권한을 검증한 내부 호출만 사용하며 복구 초대는 기존 로그인 묶음을 폐기한다 </summary>
        public Task<IssuedAuthToken> IssueInviteAsync(Guid accountId, long maxUses = 1,
            bool revokeExistingSessions = true, CancellationToken cancellationToken = default)
        {
            ValidateId(accountId, nameof(accountId));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUses);
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                await LockAccountAsync(connection, transaction, accountId, token);
                if (revokeExistingSessions)
                {
                    // 계정 잠금이 모든 family 쓰기를 직렬화하므로 목록별 쿼리 없이 한 번에 폐기한다.
                    await connection.ExecuteAsync(Command(
                        """
                        UPDATE auth_token_families SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                            WHERE account_id = @AccountId;
                        UPDATE auth_refresh_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                            WHERE account_id = @AccountId;
                        UPDATE auth_access_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                            WHERE account_id = @AccountId;
                        UPDATE auth_game_sessions SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                            WHERE account_id = @AccountId;
                        UPDATE auth_join_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                            WHERE account_id = @AccountId;
                        UPDATE auth_invites SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                            WHERE account_id = @AccountId;
                        """, new { AccountId = accountId }, transaction, token));
                }

                string raw = _hasher.CreateToken();
                Guid id = Guid.NewGuid();
                DateTime now = await ReadTimeAsync(connection, transaction, token);
                DateTime expires = now.AddHours(INVITE_HOURS);
                await connection.ExecuteAsync(Command(
                    """
                    INSERT INTO auth_invites(invite_id,account_id,code_hash,expires_at,max_uses)
                    VALUES (@Id,@AccountId,@Hash,@Expires,@MaxUses);
                    """, new { Id = id, AccountId = accountId,
                        Hash = _hasher.Hash(AuthTokenHasher.INVITE_DOMAIN, raw), Expires = expires, MaxUses = maxUses },
                    transaction, token));
                return new IssuedAuthToken(raw, id, UnixMilliseconds(expires), UnixMilliseconds(now));
            }, cancellationToken);
        }

        /// <summary> 서비스 자격을 검증한 API 내부 호출만 쓴다. 인스턴스 행이 없으면 등록 전 자리로 만들고 bootId에 묶은 임대를 발급한다 </summary>
        public Task<AuthValidationOutcome<IssuedAuthToken>> IssueServerLeaseAsync(Guid serviceId, Guid instanceId,
            int role, Guid bootId, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            ValidateId(bootId, nameof(bootId));
            return IssueServerLeaseCoreAsync(serviceId, instanceId, role, bootId, duration, cancellationToken);
        }

        /// <summary> 이미 등록된 현재 프로세스에 임대를 발급한다. 행이 없거나 역할이 다르거나 Offline이면 예외다 </summary>
        public async Task<IssuedAuthToken> IssueServerLeaseAsync(Guid serviceId, Guid instanceId, int role,
            TimeSpan duration, CancellationToken cancellationToken = default)
        {
            AuthValidationOutcome<IssuedAuthToken> outcome = await IssueServerLeaseCoreAsync(serviceId, instanceId, role,
                null, duration, cancellationToken);
            if (!outcome.IsSuccess)
            {
                throw new ArgumentException("The server instance must exist, match the trusted role and be online.");
            }

            return outcome.Identity!;
        }

        private Task<AuthValidationOutcome<IssuedAuthToken>> IssueServerLeaseCoreAsync(Guid serviceId, Guid instanceId,
            int role, Guid? bootId, TimeSpan duration, CancellationToken cancellationToken)
        {
            ValidateId(serviceId, nameof(serviceId));
            ValidateId(instanceId, nameof(instanceId));
            ValidateDuration(duration, TimeSpan.FromHours(REFRESH_HOURS));
            if ((role != ContractCodes.ServerRole.TOWN) && (role != ContractCodes.ServerRole.DUNGEON))
            {
                throw new ArgumentOutOfRangeException(nameof(role));
            }

            return InTransactionAsync(async (connection, transaction, token) =>
            {
                if (bootId.HasValue)
                {
                    // 첫 등록 전 프로세스도 임대가 있어야 등록 경로를 부를 수 있다. 등록 전 자리는 Offline, boot 없음이라
                    // 배정, 계정 임대, 접속에서 빠지고 등록이 주소와 콘텐츠 버전을 채우며 Idle로 바꾼다
                    await connection.ExecuteAsync(Command(
                        """
                        INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state)
                        VALUES (@InstanceId, @Role, @Unregistered, @UnregisteredPort, @Unregistered, @Unregistered, 'Offline')
                        ON CONFLICT (instance_id) DO NOTHING;
                        """, new { InstanceId = instanceId, Role = role, Unregistered = UNREGISTERED_TEXT,
                            UnregisteredPort = UNREGISTERED_PORT }, transaction, token));
                }

                // 인스턴스 행 하나만 잠근다. 세션 -> 계정 -> 인스턴스 순서의 다른 작업과 대기 사슬을 만들지 않는다
                LeaseInstanceRow? instance = await connection.QuerySingleOrDefaultAsync<LeaseInstanceRow>(Command(
                    """
                    SELECT role AS "Role", state AS "State", boot_id AS "BootId"
                    FROM server_instances WHERE instance_id = @InstanceId FOR UPDATE;
                    """, new { InstanceId = instanceId }, transaction, token));
                if ((instance == null) || (instance.Role != role))
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                // 장애 취소가 Offline으로 확정한 boot는 되살리지 않는다. 같은 인스턴스라도 새 bootId 프로세스는 받는다
                bool offline = instance.State == INSTANCE_OFFLINE;
                if (offline && (!bootId.HasValue || (instance.BootId == bootId)))
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_TERMINAL);
                }

                // 같은 인스턴스의 다른 boot가 아직 살아 있으면 새 임대를 주지 않는다. 새 임대는 이전 임대를 폐기하므로
                // 살아 있는 프로세스를 끊고 두 프로세스가 번갈아 임대를 빼앗게 된다.
                // 요청 boot가 전에 이 인스턴스의 임대를 받은 적이 있으면 이미 다른 boot로 대체된 프로세스다. 영영 돌아올 수 없으므로
                // SESSION_TERMINAL(409)로 프로세스를 내리게 한다. 처음 오는 boot는 이전 boot가 끊길 때까지 일시 거절(503)한다
                if (bootId.HasValue && (instance.BootId != bootId)
                    && await IsOtherBootAliveAsync(connection, transaction, instanceId, token))
                {
                    bool superseded = await connection.QuerySingleAsync<bool>(Command(
                        "SELECT EXISTS (SELECT 1 FROM auth_server_leases WHERE instance_id = @InstanceId AND boot_id = @BootId);",
                        new { InstanceId = instanceId, BootId = bootId }, transaction, token));
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(superseded
                        ? ContractCodes.RejectReason.SESSION_TERMINAL : ContractCodes.RejectReason.BACKEND_UNAVAILABLE);
                }

                Guid id = Guid.NewGuid();
                string raw = _hasher.CreateToken();
                DateTime now = await ReadTimeAsync(connection, transaction, token);
                DateTime expires = now.Add(duration);
                await connection.ExecuteAsync(Command(
                    """
                    UPDATE auth_server_leases SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                        WHERE instance_id = @InstanceId;
                    INSERT INTO auth_server_leases(lease_id,service_id,instance_id,role,token_hash,expires_at,boot_id)
                    VALUES (@Id,@ServiceId,@InstanceId,@Role,@Hash,@Expires,@BootId);
                    """, new { Id = id, ServiceId = serviceId, InstanceId = instanceId, Role = role,
                        Hash = _hasher.Hash(AuthTokenHasher.SERVER_LEASE_DOMAIN, raw), Expires = expires,
                        BootId = bootId ?? instance.BootId }, transaction, token));
                return AuthValidationOutcome<IssuedAuthToken>.Success(new IssuedAuthToken(raw, id,
                    UnixMilliseconds(expires), UnixMilliseconds(now)));
            }, cancellationToken);
        }

        /// <summary> 검증된 로그인 주체에서만 부모 수명을 넘지 않는 짧은 게임 하위 세션을 발급한다 </summary>
        public async Task<IssuedAuthToken> IssueGameSessionAsync(PlayerAuthIdentity identity, TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            AuthValidationOutcome<IssuedAuthToken> outcome = await IssueGameSessionForPlayerAsync(identity, duration,
                cancellationToken);
            if (!outcome.IsSuccess)
            {
                throw new ArgumentException("The parent login is revoked or expired.", nameof(identity));
            }

            return outcome.Identity!;
        }

        /// <summary> 하위 세션 행뿐 아니라 직접 부모와 전체 로그인 묶음의 폐기를 항상 확인한다 </summary>
        public async Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateGameSessionAsync(string gameSessionToken,
            CancellationToken cancellationToken = default)
        {
            if (!_hasher.IsAuthentic(gameSessionToken))
            {
                return AuthValidationOutcome<PlayerAuthIdentity>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            AccessRow? row = await connection.QuerySingleOrDefaultAsync<AccessRow>(new CommandDefinition(
                """
                SELECT child.account_id AS "AccountId", parent.family_id AS "FamilyId",
                    parent.token_id AS "RefreshTokenId",
                    CASE WHEN child.revoked_at IS NOT NULL OR parent.revoked_at IS NOT NULL
                        OR family.revoked_at IS NOT NULL THEN @Revoked
                        WHEN child.expires_at <= clock_timestamp() OR parent.expires_at <= clock_timestamp()
                        THEN @Expired ELSE @None END AS "ReasonCode"
                FROM auth_game_sessions AS child JOIN auth_refresh_tokens AS parent
                    ON child.account_id = parent.account_id AND child.parent_token_id = parent.token_id
                JOIN auth_token_families AS family ON family.family_id = parent.family_id
                    AND family.account_id = parent.account_id
                WHERE child.token_hash = @Hash;
                """, new { Hash = _hasher.Hash(AuthTokenHasher.GAME_SESSION_DOMAIN, gameSessionToken),
                    Revoked = ContractCodes.RejectReason.TOKEN_REVOKED, Expired = ContractCodes.RejectReason.TOKEN_EXPIRED,
                    None = ContractCodes.RejectReason.NONE }, cancellationToken: cancellationToken));
            if ((row == null) || (row.ReasonCode != ContractCodes.RejectReason.NONE))
            {
                return AuthValidationOutcome<PlayerAuthIdentity>.Rejected(
                    row?.ReasonCode ?? ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            return AuthValidationOutcome<PlayerAuthIdentity>.Success(
                new PlayerAuthIdentity(row.AccountId, row.FamilyId, row.RefreshTokenId));
        }

        /// <summary> 호출자는 참가자 명단에서 participant와 계정, 캐릭터의 결합을 검증한 뒤 내부에서만 발급한다 </summary>
        public Task<IssuedAuthToken> IssueJoinTokenAsync(PlayerAuthIdentity identity, Guid characterId,
            Guid participantId, Guid sessionId, Guid instanceId, Guid serverLeaseId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ValidatePlayerIdentity(identity);
            ValidateId(characterId, nameof(characterId));
            ValidateId(participantId, nameof(participantId));
            ValidateId(sessionId, nameof(sessionId));
            ValidateId(instanceId, nameof(instanceId));
            ValidateId(serverLeaseId, nameof(serverLeaseId));
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                if (!await LockJoinSessionAsync(connection, transaction, sessionId, token))
                {
                    throw new ArgumentException("The sortie session must exist.", nameof(sessionId));
                }
                await LockAccountAsync(connection, transaction, identity.AccountId, token);
                if (!await LockActiveIdentityAsync(connection, transaction, identity, token))
                {
                    throw new ArgumentException("The parent login is revoked or expired.", nameof(identity));
                }

                Guid? character = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                    """
                    SELECT character_id FROM characters WHERE account_id = @AccountId AND character_id = @CharacterId
                    FOR SHARE;
                    """, new { identity.AccountId, CharacterId = characterId }, transaction, token));
                if (!character.HasValue)
                {
                    throw new ArgumentException("The character must belong to the authenticated account.");
                }

                if (!await LockJoinContextAsync(connection, transaction, sessionId, instanceId, serverLeaseId, token))
                {
                    throw new ArgumentException("The session and live lease must match the assigned instance.");
                }

                bool initialJoinAllowed = await connection.QuerySingleAsync<bool>(Command(
                    """
                    SELECT state IN ('Reserved','Loading','Starting') FROM game_sessions WHERE session_id = @SessionId;
                    """, new { SessionId = sessionId }, transaction, token));
                if (!initialJoinAllowed)
                {
                    throw new ArgumentException("Initial join tokens cannot add participants after the session starts.");
                }

                Guid? participant = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                    """
                    SELECT participant_id FROM session_participants
                    WHERE session_id = @SessionId AND participant_id = @ParticipantId
                        AND account_id = @AccountId AND character_id = @CharacterId AND state = 'Reserved'
                    FOR SHARE;
                    """, new { SessionId = sessionId, ParticipantId = participantId, identity.AccountId,
                        CharacterId = characterId }, transaction, token));
                if (!participant.HasValue)
                {
                    throw new ArgumentException("The reserved participant must match the account and character.");
                }
                Guid id = Guid.NewGuid();
                string raw = _hasher.CreateToken();
                DateTime now = await ReadTimeAsync(connection, transaction, token);
                DateTime expires = now.AddSeconds(JOIN_SECONDS);
                await connection.ExecuteAsync(Command(
                    """
                    UPDATE auth_join_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                        WHERE session_id = @SessionId AND participant_id = @ParticipantId AND consumed_at IS NULL;
                    INSERT INTO auth_join_tokens(token_id,account_id,family_id,character_id,participant_id,session_id,
                        instance_id,server_lease_id,token_hash,expires_at)
                    VALUES (@Id,@AccountId,@FamilyId,@CharacterId,@ParticipantId,@SessionId,@InstanceId,@LeaseId,
                        @Hash,@Expires);
                    """, new { Id = id, identity.AccountId, identity.FamilyId, CharacterId = characterId,
                        ParticipantId = participantId, SessionId = sessionId, InstanceId = instanceId,
                        LeaseId = serverLeaseId, Hash = _hasher.Hash(AuthTokenHasher.JOIN_DOMAIN, raw), Expires = expires },
                    transaction, token));
                return new IssuedAuthToken(raw, id, UnixMilliseconds(expires), UnixMilliseconds(now));
            }, cancellationToken);
        }

        /// <summary> 로그아웃이나 운영 복구는 같은 잠금 순서로 전체 로그인 묶음을 폐기한다 </summary>
        public Task<bool> RevokeFamilyAsync(Guid accountId, Guid familyId,
            CancellationToken cancellationToken = default)
        {
            ValidateId(accountId, nameof(accountId));
            ValidateId(familyId, nameof(familyId));
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                await LockAccountAsync(connection, transaction, accountId, token);
                FamilyRow? family = await LockFamilyAsync(connection, transaction, accountId, familyId, token);
                if (family == null)
                {
                    return false;
                }

                await RevokeFamilyRowsAsync(connection, transaction, familyId, token);
                return true;
            }, cancellationToken);
        }

        private static async Task<bool> LockActiveIdentityAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, PlayerAuthIdentity identity, CancellationToken token)
        {
            FamilyRow? family = await LockFamilyAsync(connection, transaction, identity.AccountId, identity.FamilyId, token);
            if ((family == null) || family.RevokedAt.HasValue)
            {
                return false;
            }

            return await connection.QuerySingleAsync<bool>(Command(
                """
                SELECT EXISTS (SELECT 1 FROM auth_refresh_tokens WHERE account_id = @AccountId
                    AND family_id = @FamilyId AND token_id = @ParentId
                    AND revoked_at IS NULL AND consumed_at IS NULL AND expires_at > clock_timestamp());
                """, new { identity.AccountId, identity.FamilyId, ParentId = identity.RefreshTokenId }, transaction, token));
        }

        /// <summary> 만료 뒤 보존 기간이 지난 입장 토큰(교환, 폐기, 미교환 모두)을 오래된 것부터 지운다. 유지 작업이 주기마다 부른다.
        /// 다른 작업자가 잡은 행은 건너뛴다. 지운 개수를 돌려준다 </summary>
        public async Task<int> PurgeExpiredJoinTokensAsync(int maxRows, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
            return await InTransactionAsync((connection, transaction, token) => connection.ExecuteAsync(Command(
                """
                DELETE FROM auth_join_tokens
                WHERE token_id IN (
                    SELECT token_id FROM auth_join_tokens
                    WHERE expires_at < clock_timestamp() - @Retention
                    ORDER BY expires_at, token_id
                    LIMIT @Limit
                    FOR UPDATE SKIP LOCKED);
                """, new { Retention = ServerPoolTimings.JoinTokenRetention, Limit = maxRows }, transaction, token)),
                cancellationToken);
        }

        private static async Task<bool> IsOtherBootAliveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid instanceId, CancellationToken token)
        {
            DynamicParameters parameters = new DynamicParameters(new { InstanceId = instanceId });
            PostgresServerPoolRepository.AddLiveBootParameters(parameters);
            return await connection.QuerySingleAsync<bool>(Command(
                "SELECT EXISTS (SELECT 1 FROM server_instances AS instance WHERE instance.instance_id = @InstanceId AND "
                + PostgresServerPoolRepository.LiveBootCondition("instance") + ");", parameters, transaction, token));
        }

        private static void ValidateId(Guid id, string name)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("An identifier must not be empty.", name);
            }
        }

        private static void ValidatePlayerIdentity(PlayerAuthIdentity identity)
        {
            ValidateId(identity.AccountId, nameof(identity.AccountId));
            ValidateId(identity.FamilyId, nameof(identity.FamilyId));
            ValidateId(identity.RefreshTokenId, nameof(identity.RefreshTokenId));
        }

        private static void ValidateDuration(TimeSpan duration, TimeSpan maximum)
        {
            if ((duration <= TimeSpan.Zero) || (duration > maximum))
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class LeaseInstanceRow
        {
            public int Role { get; set; }
            public string State { get; set; } = string.Empty;
            public Guid? BootId { get; set; }
        }
    }
}
