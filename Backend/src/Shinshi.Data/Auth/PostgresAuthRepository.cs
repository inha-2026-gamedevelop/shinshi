// System
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Characters;

namespace Shinshi.Data.Auth
{
    /// <summary> 토큰 원문을 저장하지 않고 소비와 발급, 묶음 폐기를 짧은 DB 트랜잭션으로 확정한다 </summary>
    public sealed partial class PostgresAuthRepository : IAuthRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int ACCESS_MINUTES          = 15;
        private const int REFRESH_HOURS           = 24;
        private const int INVITE_HOURS            = 24;
        private const int JOIN_SECONDS            = 60;
        private const int HEARTBEAT_SECONDS       = 15;
        private const int MAX_TRANSACTION_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS          = 50;

        private readonly NpgsqlDataSource _dataSource;
        private readonly AuthTokenHasher _hasher;
        private readonly PlayerClassPolicy _classPolicy;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> classPolicy를 생략하면 기존 직업 0, 1, 2만 마을 입장이 열린다 </summary>
        public PostgresAuthRepository(NpgsqlDataSource dataSource, AuthTokenOptions options,
            PlayerClassPolicy? classPolicy = null)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(options);
            _dataSource  = dataSource;
            _hasher      = new AuthTokenHasher(options);
            _classPolicy = classPolicy ?? PlayerClassPolicy.Default;
        }

        public bool IsAuthenticToken(string token)
        {
            return _hasher.IsAuthentic(token);
        }

        public Task<AuthExchangeOutcome> ExchangeInviteAsync(string inviteCode,
            CancellationToken cancellationToken = default)
        {
            if (!_hasher.IsAuthentic(inviteCode))
            {
                return Task.FromResult(AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.INVALID_INVITE_CODE));
            }

            byte[] hash = _hasher.Hash(AuthTokenHasher.INVITE_DOMAIN, inviteCode);
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                Guid? accountId = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                    "SELECT account_id FROM auth_invites WHERE code_hash = @Hash;",
                    new { Hash = hash }, transaction, token));
                if (!accountId.HasValue)
                {
                    return AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.INVALID_INVITE_CODE);
                }

                await LockAccountAsync(connection, transaction, accountId.Value, token);
                InviteRow? invite = await connection.QuerySingleOrDefaultAsync<InviteRow>(Command(
                    """
                    UPDATE auth_invites SET used_count = used_count + 1, used_at = clock_timestamp()
                    WHERE code_hash = @Hash AND revoked_at IS NULL
                        AND expires_at > clock_timestamp() AND used_count < max_uses
                    RETURNING invite_id AS "InviteId", account_id AS "AccountId";
                    """, new { Hash = hash }, transaction, token));
                if (invite == null)
                {
                    int reason = await connection.QuerySingleAsync<int>(Command(
                        """
                        SELECT CASE WHEN revoked_at IS NOT NULL THEN @Invalid
                            WHEN used_count >= max_uses THEN @Used
                            WHEN expires_at <= clock_timestamp() THEN @Expired ELSE @Invalid END
                        FROM auth_invites WHERE code_hash = @Hash;
                        """, new { Hash = hash, Invalid = ContractCodes.RejectReason.INVALID_INVITE_CODE,
                            Used = ContractCodes.RejectReason.INVITE_CODE_USED,
                            Expired = ContractCodes.RejectReason.INVITE_CODE_EXPIRED }, transaction, token));
                    return AuthExchangeOutcome.Rejected(reason);
                }

                Guid familyId = Guid.NewGuid();
                await connection.ExecuteAsync(Command(
                    "INSERT INTO auth_token_families(family_id,account_id,origin) VALUES (@FamilyId,@AccountId,'invite');",
                    new { FamilyId = familyId, AccountId = accountId.Value }, transaction, token));
                return await InsertTokenPairAsync(connection, transaction, accountId.Value, familyId, null, token);
            }, cancellationToken);
        }

        public Task<AuthExchangeOutcome> RotateRefreshAsync(string refreshToken,
            CancellationToken cancellationToken = default)
        {
            return RotateRefreshCoreAsync(refreshToken, null, cancellationToken);
        }

        public Task<AuthExchangeOutcome> RotateRefreshAsync(string refreshToken, string requestId,
            CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParseExact(requestId, "N", out Guid parsed) || (parsed == Guid.Empty)
                || (requestId != parsed.ToString("N")))
            {
                return Task.FromResult(AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED));
            }

            return RotateRefreshCoreAsync(refreshToken, requestId, cancellationToken);
        }

        private Task<AuthExchangeOutcome> RotateRefreshCoreAsync(string refreshToken, string? requestId,
            CancellationToken cancellationToken)
        {
            if (!_hasher.IsAuthentic(refreshToken))
            {
                return Task.FromResult(AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED));
            }

            byte[] hash = _hasher.Hash(AuthTokenHasher.REFRESH_DOMAIN, refreshToken);
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                RefreshRow? found = await ReadRefreshAsync(connection, transaction, hash, token);
                if (found == null)
                {
                    return AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                // 다른 회전, 재사용, 로그아웃과 같은 account -> family 순서로 잠근다.
                await LockAccountAsync(connection, transaction, found.AccountId, token);
                FamilyRow? family = await LockFamilyAsync(connection, transaction, found.AccountId, found.FamilyId, token);
                RefreshRow current = (await ReadRefreshAsync(connection, transaction, hash, token))!;
                if ((family == null) || family.RevokedAt.HasValue)
                {
                    return AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                if (current.ConsumedAt.HasValue || current.RevokedAt.HasValue)
                {
                    if ((requestId != null) && (current.RotationRequestHash != null)
                        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(current.RotationRequestHash,
                            _hasher.Hash(AuthTokenHasher.ROTATION_REQUEST_DOMAIN, requestId)))
                    {
                        return await ReplayRotationAsync(connection, transaction, current, refreshToken, requestId, token);
                    }

                    // 재사용이 child 발급보다 먼저/나중에 도착해도 같은 family 잠금 안에서 전체를 폐기한다.
                    await RevokeFamilyRowsAsync(connection, transaction, current.FamilyId, token);
                    return AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                DateTime now = await ReadTimeAsync(connection, transaction, token);
                if (current.ExpiresAt <= now)
                {
                    return AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED);
                }

                int changed = await connection.ExecuteAsync(Command(
                    """
                    UPDATE auth_refresh_tokens SET consumed_at = clock_timestamp(), revoked_at = clock_timestamp(),
                        rotation_request_hash = @RequestHash
                    WHERE token_id = @TokenId AND consumed_at IS NULL AND revoked_at IS NULL
                        AND expires_at > clock_timestamp();
                    """, new { current.TokenId, RequestHash = requestId == null ? null
                        : _hasher.Hash(AuthTokenHasher.ROTATION_REQUEST_DOMAIN, requestId) }, transaction, token));
                if (changed == 0)
                {
                    return AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED);
                }

                // 기존 부모를 직접 폐기하면 그 부모에서 발급한 게임 세션도 함께 폐기한다.
                await connection.ExecuteAsync(Command(
                    """
                    UPDATE auth_game_sessions SET revoked_at = COALESCE(revoked_at, clock_timestamp())
                    WHERE parent_token_id = @TokenId;
                    """, new { current.TokenId }, transaction, token));
                return await InsertTokenPairAsync(connection, transaction, current.AccountId,
                    current.FamilyId, current.TokenId, token,
                    requestId == null ? null : _hasher.CreateRotationToken(AuthTokenHasher.REFRESH_DOMAIN, refreshToken, requestId),
                    requestId == null ? null : _hasher.CreateRotationToken(AuthTokenHasher.ACCESS_DOMAIN, refreshToken, requestId));
            }, cancellationToken);
        }

        public async Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateAccessAsync(string accessToken,
            CancellationToken cancellationToken = default)
        {
            if (!_hasher.IsAuthentic(accessToken))
            {
                return AuthValidationOutcome<PlayerAuthIdentity>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            AccessRow? row = await connection.QuerySingleOrDefaultAsync<AccessRow>(new CommandDefinition(
                """
                SELECT access.account_id AS "AccountId", access.family_id AS "FamilyId",
                    access.refresh_token_id AS "RefreshTokenId",
                    CASE WHEN (access.revoked_at IS NOT NULL OR family.revoked_at IS NOT NULL) THEN @Revoked
                        WHEN access.expires_at <= clock_timestamp() THEN @Expired ELSE @None END AS "ReasonCode"
                FROM auth_access_tokens AS access
                JOIN auth_token_families AS family ON family.family_id = access.family_id
                    AND family.account_id = access.account_id
                WHERE access.token_hash = @Hash;
                """, new { Hash = _hasher.Hash(AuthTokenHasher.ACCESS_DOMAIN, accessToken),
                    Revoked = ContractCodes.RejectReason.TOKEN_REVOKED,
                    Expired = ContractCodes.RejectReason.TOKEN_EXPIRED, None = ContractCodes.RejectReason.NONE },
                cancellationToken: cancellationToken));
            if ((row == null) || (row.ReasonCode != ContractCodes.RejectReason.NONE))
            {
                return AuthValidationOutcome<PlayerAuthIdentity>.Rejected(
                    row?.ReasonCode ?? ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            return AuthValidationOutcome<PlayerAuthIdentity>.Success(
                new PlayerAuthIdentity(row.AccountId, row.FamilyId, row.RefreshTokenId));
        }

        public async Task<AuthValidationOutcome<ServerAuthIdentity>> ValidateServerLeaseAsync(string serverLeaseToken,
            CancellationToken cancellationToken = default)
        {
            if (!_hasher.IsAuthentic(serverLeaseToken))
            {
                return AuthValidationOutcome<ServerAuthIdentity>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            // heartbeat 부재는 인증 거절 사유가 아니다. 네트워크가 잠깐 끊긴 서버가 heartbeat로 복귀하려면 임대가 살아 있어야 한다.
            // 되돌릴 수 없는 사망은 장애 취소가 그 boot를 Offline으로 쓴 순간뿐이며, 생존 판정은 효과 지점에서 따로 한다
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            ServerRow? row = await connection.QuerySingleOrDefaultAsync<ServerRow>(new CommandDefinition(
                """
                SELECT lease.lease_id AS "LeaseId", lease.instance_id AS "InstanceId",
                    lease.service_id AS "ServiceId", lease.role AS "Role", lease.expires_at AS "ExpiresAt",
                    lease.boot_id AS "BootId",
                    CASE WHEN lease.revoked_at IS NOT NULL THEN @Revoked
                        WHEN lease.expires_at <= clock_timestamp() THEN @Expired
                        WHEN instance.state = 'Offline'
                            AND (lease.boot_id IS NULL OR lease.boot_id = instance.boot_id) THEN @Revoked
                        ELSE @None END AS "ReasonCode"
                FROM auth_server_leases AS lease JOIN server_instances AS instance
                    ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                WHERE lease.token_hash = @Hash;
                """, new { Hash = _hasher.Hash(AuthTokenHasher.SERVER_LEASE_DOMAIN, serverLeaseToken),
                    Revoked = ContractCodes.RejectReason.TOKEN_REVOKED,
                    Expired = ContractCodes.RejectReason.TOKEN_EXPIRED, None = ContractCodes.RejectReason.NONE },
                cancellationToken: cancellationToken));
            if ((row == null) || (row.ReasonCode != ContractCodes.RejectReason.NONE))
            {
                return AuthValidationOutcome<ServerAuthIdentity>.Rejected(
                    row?.ReasonCode ?? ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            return AuthValidationOutcome<ServerAuthIdentity>.Success(new ServerAuthIdentity(row.InstanceId,
                row.LeaseId, row.ServiceId, row.Role, UnixMilliseconds(row.ExpiresAt), row.BootId));
        }

        public Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeJoinTokenAsync(string joinToken,
            Guid instanceId, int serverRole, Guid serverLeaseId, CancellationToken cancellationToken = default)
        {
            if (!_hasher.IsAuthentic(joinToken))
            {
                return Task.FromResult(AuthValidationOutcome<JoinAuthIdentity>.Rejected(
                    ContractCodes.RejectReason.TOKEN_REVOKED));
            }
            if ((instanceId == Guid.Empty) || (serverLeaseId == Guid.Empty)
                || ((serverRole != ContractCodes.ServerRole.DUNGEON) && (serverRole != ContractCodes.ServerRole.TOWN)))
            {
                return Task.FromResult(AuthValidationOutcome<JoinAuthIdentity>.Rejected(
                    ContractCodes.RejectReason.SESSION_MISMATCH));
            }

            byte[] hash = _hasher.Hash(AuthTokenHasher.JOIN_DOMAIN, joinToken);
            return InTransactionAsync(async (connection, transaction, token) =>
            {
                JoinRow? found = await connection.QuerySingleOrDefaultAsync<JoinRow>(Command(
                    """
                    SELECT account_id AS "AccountId", family_id AS "FamilyId", character_id AS "CharacterId",
                        participant_id AS "ParticipantId", session_id AS "SessionId", instance_id AS "InstanceId",
                        server_role AS "ServerRole"
                    FROM auth_join_tokens WHERE token_hash = @Hash;
                    """, new { Hash = hash }, transaction, token));
                if (found == null)
                {
                    return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                // 소비하지 않고 거절한다. 다른 서버나 다른 역할로 잘못 보낸 토큰을 원래 서버에서 계속 쓸 수 있어야 한다
                if ((found.InstanceId != instanceId) || (found.ServerRole != serverRole))
                {
                    return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                return serverRole == ContractCodes.ServerRole.TOWN
                    ? await ConsumeTownJoinAsync(connection, transaction, found, hash, serverLeaseId, token)
                    : await ConsumeDungeonJoinAsync(connection, transaction, found, hash, serverLeaseId, token);
            }, cancellationToken);
        }

        private async Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeDungeonJoinAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, JoinRow found, byte[] hash, Guid serverLeaseId, CancellationToken token)
        {
            Guid instanceId = found.InstanceId;
            Guid sessionId = found.SessionId!.Value;
            Guid participantId = found.ParticipantId!.Value;
            if (!await LockJoinSessionAsync(connection, transaction, sessionId, token))
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            await LockAccountAsync(connection, transaction, found.AccountId, token);
            FamilyRow? family = await LockFamilyAsync(connection, transaction, found.AccountId, found.FamilyId, token);
            if ((family == null) || family.RevokedAt.HasValue)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            // 임대 재발급은 이전 임대를 폐기하므로 발급 당시 ID 대신 같은 인스턴스의 현재 살아 있는 임대를 검증한다.
            if (!await LockJoinContextAsync(connection, transaction, sessionId, instanceId, serverLeaseId, token))
            {
                bool closed = await connection.QuerySingleAsync<bool>(Command(
                    """
                    SELECT state NOT IN ('Reserved','Loading','Starting','Running')
                        AND NOT (state = 'Auction' AND auction_ends_at > clock_timestamp())
                    FROM game_sessions WHERE session_id = @SessionId;
                    """, new { SessionId = sessionId }, transaction, token));
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(closed
                    ? ContractCodes.RejectReason.ALREADY_SETTLED : ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            // 직업, 닉네임, 외형은 참가자가 묶인 캐릭터 행에서, 출정 계약은 세션 행에서 읽는다. 게임 서버가 이 값으로 전투 개체와 전투를 만든다
            JoinCharacterRow? character = await connection.QuerySingleOrDefaultAsync<JoinCharacterRow>(Command(
                """
                SELECT character.class_id AS "ClassId", character.name AS "Name", character.appearance::text AS "Appearance",
                    participant.departed_at IS NOT NULL AS "Departed", session.mode AS "Mode", session.map_id AS "MapId",
                    session.battle_id AS "BattleId", session.boss_id AS "BossId", session.difficulty AS "Difficulty",
                    participant.loadout_snapshot::text AS "Loadout", character.total_experience AS "TotalExperience",
                    character.prologue_stage AS "PrologueStage", character.prologue_wolbaek_potions AS "PrologueWolbaekPotions",
                    character.prologue_talisman_checked AS "PrologueTalismanChecked",
                    character.last_experience_session_id AS "ExperienceSessionId",
                    character.last_experience_event_seq AS "ExperienceEventSeq", session.content_version AS "ContentVersion", session.experience_settings::text AS "ExperienceSettings",
                    (SELECT guild.name FROM guild_members AS member JOIN guilds AS guild ON guild.guild_id = member.guild_id
                        WHERE member.character_id = character.character_id AND guild.disbanded_at IS NULL) AS "GuildName"
                FROM session_participants AS participant
                JOIN characters AS character
                    ON character.character_id = participant.character_id AND character.account_id = participant.account_id
                JOIN game_sessions AS session ON session.session_id = participant.session_id
                    WHERE participant.session_id = @SessionId AND participant.participant_id = @ParticipantId
                        AND participant.account_id = @AccountId AND participant.character_id = @CharacterId
                        AND participant.state IN ('Reserved','Active') AND character.deleted_at IS NULL;
                """, new { SessionId = sessionId, ParticipantId = participantId, found.AccountId, found.CharacterId },
                transaction, token));
            if (character == null)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            Growth.GrowthContent experienceContent = Growth.GrowthContent.LoadEmbedded();
            CharacterLevelSettings? reservedExperience = string.IsNullOrEmpty(character.ExperienceSettings) ? null
                : Newtonsoft.Json.JsonConvert.DeserializeObject<CharacterLevelSettings>(character.ExperienceSettings);
            if ((reservedExperience == null) || !reservedExperience.IsValid()
                || (CharacterLevelRules.SettingsHash(reservedExperience) != CharacterLevelRules.SettingsHash(experienceContent.CharacterLevel))
                || (character.ContentVersion != experienceContent.ContentVersion)
                || ((character.Mode == "Normal") && (experienceContent.CharacterLevel.GetGrant(character.MapId,
                    CharacterExperienceSource.NORMAL_ENEMY) == 0)))
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            if (character.Departed)
            {
                // 포기, 접속 만료로 떠난 참가자는 같은 출정에 다시 들어오지 않는다
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.ALREADY_SETTLED);
            }

            bool connectedElsewhere = await connection.QuerySingleAsync<bool>(Command(
                """
                SELECT EXISTS (SELECT 1 FROM account_leases AS lease JOIN game_sessions AS session
                    ON session.session_id = @SessionId
                    WHERE lease.account_id = @AccountId AND lease.expires_at > clock_timestamp()
                        AND lease.instance_id <> @InstanceId
                        AND lease.instance_id IS DISTINCT FROM session.reserved_by_instance_id);
                """, new { SessionId = sessionId, found.AccountId, InstanceId = instanceId }, transaction, token));
            if (connectedElsewhere)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(ContractCodes.RejectReason.ALREADY_CONNECTED);
            }

            JoinRow? consumed = await connection.QuerySingleOrDefaultAsync<JoinRow>(Command(
                """
                UPDATE auth_join_tokens SET consumed_at = clock_timestamp()
                WHERE token_hash = @Hash AND instance_id = @InstanceId AND server_role = @Dungeon
                    AND expires_at > clock_timestamp() AND consumed_at IS NULL AND revoked_at IS NULL
                    AND EXISTS (SELECT 1 FROM auth_server_leases AS lease JOIN server_instances AS instance
                        ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                        WHERE lease.lease_id = @LeaseId AND lease.instance_id = @InstanceId
                            AND lease.role = @Dungeon AND lease.revoked_at IS NULL
                            AND lease.expires_at > clock_timestamp() AND instance.state <> 'Offline'
                            AND instance.heartbeat_at > clock_timestamp() - @Heartbeat)
                    AND EXISTS (SELECT 1 FROM game_sessions AS session WHERE session.session_id = @SessionId
                        AND session.instance_id = @InstanceId AND session.role = @Dungeon
                        AND (session.state IN ('Reserved','Loading','Starting','Running')
                            OR (session.state = 'Auction' AND session.auction_ends_at > clock_timestamp()))
                        AND (session.state <> 'Reserved' OR session.expires_at > clock_timestamp()))
                    AND NOT EXISTS (SELECT 1 FROM session_participants AS participant
                        WHERE participant.session_id = @SessionId AND participant.participant_id = auth_join_tokens.participant_id
                            AND participant.departed_at IS NOT NULL)
                RETURNING account_id AS "AccountId", character_id AS "CharacterId",
                    participant_id AS "ParticipantId", session_id AS "SessionId", instance_id AS "InstanceId";
                """, new { Hash = hash, InstanceId = instanceId, SessionId = sessionId, LeaseId = serverLeaseId,
                    Dungeon = ContractCodes.ServerRole.DUNGEON,
                    Heartbeat = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) }, transaction, token));
            if (consumed == null)
            {
                return AuthValidationOutcome<JoinAuthIdentity>.Rejected(
                    await ReadUnconsumedReasonAsync(connection, transaction, hash, token));
            }

            // 던전 서버는 이 수량 안에서만 물약 사용 이벤트를 보낸다. 넘는 사용은 /events가 POTION_LIMIT로 거절하고 그 거절은
            // 출정 저장 큐를 멈추므로, 서버가 미리 막을 수 있게 확정 사용을 뺀 남은 예약분을 함께 준다
            List<ReservationPotion> potions = (await connection.QueryAsync<ReservedPotionRow>(Command(
                """
                SELECT definition_id AS "DefinitionId", binding_kind AS "BindingKind", quantity - consumed AS "Remaining"
                FROM reservations
                WHERE session_id = @SessionId AND participant_id = @ParticipantId AND asset_kind = 'Item'
                    AND state = 'Reserved' AND quantity > consumed
                ORDER BY definition_id, binding_kind;
                """, new { SessionId = sessionId, ParticipantId = participantId }, transaction, token)))
                .Select(row => new ReservationPotion(row.DefinitionId, row.BindingKind, row.Remaining)).ToList();

            DateTime now = await ReadTimeAsync(connection, transaction, token);
            return AuthValidationOutcome<JoinAuthIdentity>.Success(new JoinAuthIdentity(consumed.AccountId,
                consumed.CharacterId, character.ClassId, consumed.ParticipantId!.Value, consumed.SessionId!.Value,
                consumed.InstanceId, UnixMilliseconds(now), default, character.Name,
                JoinSortieContract.FromRow(character.Mode, character.MapId, character.BattleId, character.BossId,
                    character.Difficulty, character.PrologueStage, character.PrologueWolbaekPotions,
                    character.PrologueTalismanChecked), character.Loadout, potions, character.Appearance,
                new CharacterExperienceSnapshot(character.TotalExperience,
                    consumed.SessionId.Value.ToString("D"), character.ExperienceSessionId == consumed.SessionId
                        ? character.ExperienceEventSeq : 0,
                    CharacterLevelRules.SettingsHash(Growth.GrowthContent.LoadEmbedded().CharacterLevel)),
                character.GuildName));
        }

        // 던전 입장 교환이 돌려주는 남은 물약 예약분 한 줄
        private sealed class ReservedPotionRow
        {
            public string DefinitionId { get; set; } = string.Empty;
            public string BindingKind { get; set; } = string.Empty;
            public long Remaining { get; set; }
        }

        // 입장 교환이 읽는 캐릭터 직업, 닉네임, 외형 원문. 레거시 행이면 이름과 외형은 null이고 그 null이 표시 resolver가 읽는 유효한 값이다.
        // 던전 입장은 이탈 여부와 출정 계약도 함께 읽는다(마을 입장은 기본값)
        private sealed class JoinCharacterRow
        {
            public long TotalExperience { get; set; }
            public Guid? ExperienceSessionId { get; set; }
            public long ExperienceEventSeq { get; set; }
            public string ContentVersion { get; set; } = string.Empty;
            public string? ExperienceSettings { get; set; }
            public int ClassId { get; set; }
            public string? Name { get; set; }
            public string? Appearance { get; set; }
            public string? GuildName { get; set; }
            public bool Departed { get; set; }
            public string Mode { get; set; } = string.Empty;
            public string MapId { get; set; } = string.Empty;
            public string? BattleId { get; set; }
            public string? BossId { get; set; }
            public short? Difficulty { get; set; }
            public string? Loadout { get; set; }

            // 프롤로그 출정의 던전 서버가 저장 단계에서 재개하도록 함께 읽는다(0023). 마을 입장은 쓰지 않는다
            public short PrologueStage { get; set; }
            public short PrologueWolbaekPotions { get; set; }
            public bool PrologueTalismanChecked { get; set; }
        }

        private static Task<int> ReadUnconsumedReasonAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            byte[] hash, CancellationToken token)
        {
            return connection.QuerySingleAsync<int>(Command(
                """
                SELECT CASE WHEN consumed_at IS NOT NULL OR revoked_at IS NOT NULL THEN @Revoked
                    WHEN expires_at <= clock_timestamp() THEN @Expired ELSE @Mismatch END
                FROM auth_join_tokens WHERE token_hash = @Hash;
                """, new { Hash = hash, Revoked = ContractCodes.RejectReason.TOKEN_REVOKED,
                    Expired = ContractCodes.RejectReason.TOKEN_EXPIRED,
                    Mismatch = ContractCodes.RejectReason.SESSION_MISMATCH }, transaction, token));
        }

        private async Task<AuthExchangeOutcome> InsertTokenPairAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, Guid accountId, Guid familyId, Guid? previousId, CancellationToken token,
            string? refreshValue = null, string? accessValue = null)
        {
            Guid refreshId = Guid.NewGuid();
            Guid accessId = Guid.NewGuid();
            string refresh = refreshValue ?? _hasher.CreateToken();
            string access = accessValue ?? _hasher.CreateToken();
            DateTime now = await ReadTimeAsync(connection, transaction, token);
            DateTime refreshExpires = now.AddHours(REFRESH_HOURS);
            DateTime accessExpires = now.AddMinutes(ACCESS_MINUTES);
            await connection.ExecuteAsync(Command(
                """
                INSERT INTO auth_refresh_tokens(token_id,account_id,family_id,token_hash,expires_at)
                VALUES (@RefreshId,@AccountId,@FamilyId,@RefreshHash,@RefreshExpires);
                INSERT INTO auth_access_tokens(token_id,account_id,family_id,refresh_token_id,token_hash,expires_at)
                VALUES (@AccessId,@AccountId,@FamilyId,@RefreshId,@AccessHash,@AccessExpires);
                UPDATE auth_refresh_tokens SET replaced_by_token_id = @RefreshId WHERE token_id = @PreviousId;
                """, new { RefreshId = refreshId, AccessId = accessId, AccountId = accountId, FamilyId = familyId,
                    RefreshHash = _hasher.Hash(AuthTokenHasher.REFRESH_DOMAIN, refresh),
                    AccessHash = _hasher.Hash(AuthTokenHasher.ACCESS_DOMAIN, access), RefreshExpires = refreshExpires,
                    AccessExpires = accessExpires, PreviousId = previousId }, transaction, token));
            return AuthExchangeOutcome.Success(new AuthExchangeResponse(accountId.ToString("D"), access,
                UnixMilliseconds(accessExpires), refresh, UnixMilliseconds(refreshExpires), UnixMilliseconds(now)));
        }

        private async Task<T> InTransactionAsync<T>(
            Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> action, CancellationToken token)
        {
            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(token);
                    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted, token);
                    await PostgresTransactionPolicy.ApplyAsync(connection, transaction, token);
                    T result = await action(connection, transaction, token);
                    await transaction.CommitAsync(token);
                    return result;
                }
                catch (PostgresException exception) when ((attempt < MAX_TRANSACTION_ATTEMPTS)
                    && !token.IsCancellationRequested
                    && ((exception.SqlState == PostgresErrorCodes.SerializationFailure)
                        || (exception.SqlState == PostgresErrorCodes.DeadlockDetected)))
                {
                    await Task.Delay(RETRY_DELAY_MS * attempt, token);
                }
                catch (Exception exception) when (PostgresTransactionPolicy.IsTimeout(exception))
                {
                    token.ThrowIfCancellationRequested();
                    throw new RetryableDatabaseException(exception);
                }
            }
        }

        private static CommandDefinition Command(string sql, object? parameters, NpgsqlTransaction transaction,
            CancellationToken token)
        {
            return new CommandDefinition(sql, parameters, transaction, cancellationToken: token);
        }

        private static async Task LockAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid accountId, CancellationToken token)
        {
            Guid? locked = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                "SELECT account_id FROM accounts WHERE account_id = @AccountId FOR UPDATE;",
                new { AccountId = accountId }, transaction, token));
            if (!locked.HasValue)
            {
                throw new ArgumentException("The account must exist.", nameof(accountId));
            }
        }

        private static Task<FamilyRow?> LockFamilyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid accountId, Guid familyId, CancellationToken token)
        {
            return connection.QuerySingleOrDefaultAsync<FamilyRow>(Command(
                """
                SELECT family_id AS "FamilyId", revoked_at AS "RevokedAt" FROM auth_token_families
                WHERE account_id = @AccountId AND family_id = @FamilyId FOR UPDATE;
                """, new { AccountId = accountId, FamilyId = familyId }, transaction, token));
        }

        private static Task<RefreshRow?> ReadRefreshAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            byte[] hash, CancellationToken token)
        {
            return connection.QuerySingleOrDefaultAsync<RefreshRow>(Command(
                """
                SELECT token_id AS "TokenId", account_id AS "AccountId", family_id AS "FamilyId",
                    expires_at AS "ExpiresAt", revoked_at AS "RevokedAt", consumed_at AS "ConsumedAt",
                    replaced_by_token_id AS "ReplacedByTokenId", rotation_request_hash AS "RotationRequestHash"
                FROM auth_refresh_tokens WHERE token_hash = @Hash;
                """, new { Hash = hash }, transaction, token));
        }

        private static Task<DateTime> ReadTimeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            CancellationToken token)
        {
            return connection.QuerySingleAsync<DateTime>(Command("SELECT clock_timestamp();", null, transaction, token));
        }

        private static long UnixMilliseconds(DateTime time)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        private static async Task RevokeFamilyRowsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid familyId, CancellationToken token)
        {
            await connection.ExecuteAsync(Command(
                """
                UPDATE auth_token_families SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE family_id = @FamilyId;
                UPDATE auth_refresh_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE family_id = @FamilyId;
                UPDATE auth_access_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE family_id = @FamilyId;
                UPDATE auth_game_sessions AS child SET revoked_at = COALESCE(child.revoked_at,clock_timestamp())
                    FROM auth_refresh_tokens AS parent
                    WHERE child.parent_token_id = parent.token_id AND parent.family_id = @FamilyId;
                UPDATE auth_join_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE family_id = @FamilyId;
                """, new { FamilyId = familyId }, transaction, token));
        }

        private static async Task<bool> LockJoinSessionAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, Guid sessionId, CancellationToken token)
        {
            // 시작, 만료 해제와 같은 session -> account 순서로 잠가 서로의 상태 전이 중간을 읽지 않는다.
            Guid? session = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                "SELECT session_id FROM game_sessions WHERE session_id = @SessionId FOR SHARE;",
                new { SessionId = sessionId }, transaction, token));
            return session.HasValue;
        }
        private static async Task<bool> LockJoinContextAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, Guid sessionId, Guid instanceId, Guid leaseId, CancellationToken token)
        {
            Guid? lease = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                """
                SELECT lease.lease_id FROM auth_server_leases AS lease JOIN server_instances AS instance
                    ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                WHERE lease.lease_id = @LeaseId AND lease.instance_id = @InstanceId AND lease.role = @Dungeon
                    AND lease.revoked_at IS NULL AND lease.expires_at > clock_timestamp()
                    AND instance.state <> 'Offline' AND instance.heartbeat_at > clock_timestamp() - @Heartbeat
                FOR SHARE OF instance, lease;
                """, new { LeaseId = leaseId, InstanceId = instanceId, Dungeon = ContractCodes.ServerRole.DUNGEON,
                    Heartbeat = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) }, transaction, token));
            if (!lease.HasValue)
            {
                return false;
            }

            Guid? session = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                """
                SELECT session.session_id FROM game_sessions AS session JOIN server_instances AS instance
                    ON instance.instance_id = session.instance_id AND instance.role = session.role
                WHERE session.session_id = @SessionId AND session.instance_id = @InstanceId AND session.role = @Dungeon
                    AND (session.state IN ('Reserved','Loading','Starting','Running')
                        OR (session.state = 'Auction' AND session.auction_ends_at > clock_timestamp()))
                    AND (session.state <> 'Reserved' OR session.expires_at > clock_timestamp())
                    AND (session.instance_boot_id IS NULL OR session.instance_boot_id = instance.boot_id)
                FOR SHARE OF session;
                """, new { SessionId = sessionId, InstanceId = instanceId, Dungeon = ContractCodes.ServerRole.DUNGEON }, transaction, token));
            return session.HasValue;
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class InviteRow
        {
            public Guid InviteId { get; set; }
            public Guid AccountId { get; set; }
        }

        private sealed class FamilyRow
        {
            public Guid FamilyId { get; set; }
            public DateTime? RevokedAt { get; set; }
        }

        private sealed class RefreshRow
        {
            public Guid? ReplacedByTokenId { get; set; }
            public byte[]? RotationRequestHash { get; set; }
            public Guid TokenId { get; set; }
            public Guid AccountId { get; set; }
            public Guid FamilyId { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime? RevokedAt { get; set; }
            public DateTime? ConsumedAt { get; set; }
        }

        private sealed class AccessRow
        {
            public Guid AccountId { get; set; }
            public Guid FamilyId { get; set; }
            public Guid RefreshTokenId { get; set; }
            public int ReasonCode { get; set; }
        }

        private sealed class ServerRow
        {
            public Guid LeaseId { get; set; }
            public Guid InstanceId { get; set; }
            public Guid ServiceId { get; set; }
            public int Role { get; set; }
            public DateTime ExpiresAt { get; set; }
            public Guid? BootId { get; set; }
            public int ReasonCode { get; set; }
        }

        private sealed class JoinRow
        {
            public Guid AccountId { get; set; }
            public Guid FamilyId { get; set; }
            public Guid CharacterId { get; set; }
            public Guid? ParticipantId { get; set; }
            public Guid? SessionId { get; set; }
            public Guid InstanceId { get; set; }
            public int ServerRole { get; set; }
        }
    }
}

