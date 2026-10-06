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

        /// <summary> 마을 서버 임대와 예약 명단을 다시 확인하고 계정의 활성 로그인 묶음에 입장 토큰을 결합한다 </summary>
        public Task<AuthValidationOutcome<IssuedAuthToken>> IssueDungeonJoinTokenAsync(ServerAuthIdentity server,
            DungeonJoinGrant grant, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(grant);
            if ((server.Role != ContractCodes.ServerRole.TOWN) || (server.InstanceId == Guid.Empty)
                || (server.LeaseId == Guid.Empty) || (server.ServiceId == Guid.Empty))
            {
                return Task.FromResult(AuthValidationOutcome<IssuedAuthToken>.Rejected(
                    ContractCodes.RejectReason.TOKEN_REVOKED));
            }
            if ((grant.AccountId == Guid.Empty) || (grant.CharacterId == Guid.Empty)
                || (grant.ParticipantId == Guid.Empty) || (grant.SessionId == Guid.Empty) || (grant.InstanceId == Guid.Empty))
            {
                return Task.FromResult(AuthValidationOutcome<IssuedAuthToken>.Rejected(
                    ContractCodes.RejectReason.SESSION_MISMATCH));
            }

            return InTransactionAsync(async (connection, transaction, token) =>
            {
                if (!await LockJoinSessionAsync(connection, transaction, grant.SessionId, token))
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                Guid? account = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                    "SELECT account_id FROM accounts WHERE account_id = @AccountId FOR UPDATE;",
                    new { grant.AccountId }, transaction, token));
                if (!account.HasValue)
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                FamilyRow? family = await connection.QuerySingleOrDefaultAsync<FamilyRow>(Command(
                    """
                    SELECT family.family_id AS "FamilyId", family.revoked_at AS "RevokedAt"
                    FROM auth_token_families AS family
                    WHERE family.account_id = @AccountId AND family.revoked_at IS NULL
                        AND EXISTS (SELECT 1 FROM auth_refresh_tokens AS refresh
                            WHERE refresh.account_id = family.account_id AND refresh.family_id = family.family_id
                                AND refresh.revoked_at IS NULL AND refresh.consumed_at IS NULL
                                AND refresh.expires_at > clock_timestamp())
                    ORDER BY family.created_at DESC, family.family_id DESC LIMIT 1 FOR UPDATE OF family;
                    """, new { grant.AccountId }, transaction, token));
                if (family == null)
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                Guid? character = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                    """
                    SELECT character_id FROM characters
                    WHERE account_id = @AccountId AND character_id = @CharacterId AND deleted_at IS NULL FOR SHARE;
                    """,
                    new { grant.AccountId, grant.CharacterId }, transaction, token));
                if (!character.HasValue)
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                Guid? townLease = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                    """
                    SELECT lease.lease_id FROM auth_server_leases AS lease JOIN server_instances AS instance
                        ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                    WHERE lease.lease_id = @LeaseId AND lease.instance_id = @TownInstanceId AND lease.service_id = @ServiceId
                        AND lease.role = @Town AND lease.revoked_at IS NULL AND lease.expires_at > clock_timestamp()
                        AND instance.state <> 'Offline' AND instance.heartbeat_at > clock_timestamp() - @Heartbeat
                    FOR SHARE OF instance, lease;
                    """, new { LeaseId = server.LeaseId, TownInstanceId = server.InstanceId, server.ServiceId,
                        Town = ContractCodes.ServerRole.TOWN, Heartbeat = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) },
                    transaction, token));
                if (!townLease.HasValue)
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                Guid? dungeonLease = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                    """
                    SELECT lease.lease_id FROM auth_server_leases AS lease JOIN server_instances AS instance
                        ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                    WHERE lease.instance_id = @InstanceId AND lease.role = @Dungeon
                        AND lease.revoked_at IS NULL AND lease.expires_at > clock_timestamp()
                        AND instance.state <> 'Offline' AND instance.heartbeat_at > clock_timestamp() - @Heartbeat
                        AND instance.address = @Address AND instance.port = @Port
                    ORDER BY lease.expires_at DESC, lease.lease_id DESC LIMIT 1 FOR SHARE OF instance, lease;
                    """, new { grant.InstanceId, grant.Address, grant.Port, Dungeon = ContractCodes.ServerRole.DUNGEON,
                        Heartbeat = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) }, transaction, token));
                if (!dungeonLease.HasValue)
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                if (!await LockJoinContextAsync(connection, transaction, grant.SessionId, grant.InstanceId,
                    dungeonLease.Value, token))
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                if (!await LockIssuableParticipantAsync(connection, transaction, grant.SessionId, grant.ParticipantId,
                    grant.AccountId, grant.CharacterId, server.InstanceId, token))
                {
                    return AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                TimeSpan lifetime = grant.TokenLifetime < TimeSpan.FromSeconds(JOIN_SECONDS)
                    ? grant.TokenLifetime : TimeSpan.FromSeconds(JOIN_SECONDS);
                IssuedAuthToken? issued = await InsertDungeonJoinTokenAsync(connection, transaction,
                    new DungeonTokenTarget(grant.AccountId, family.FamilyId, grant.CharacterId, grant.ParticipantId,
                        grant.SessionId, grant.InstanceId, dungeonLease.Value), lifetime, token);
                return issued == null
                    ? AuthValidationOutcome<IssuedAuthToken>.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED)
                    : AuthValidationOutcome<IssuedAuthToken>.Success(issued);
            }, cancellationToken);
        }

        /// <summary> 진행 중 출정에서 끊긴 참가자 본인에게 복귀용 60초 일회 토큰을 발급한다. 출정 Running, 참가자 Active, 로그인 묶음이 살아 있을 때만 </summary>
        public Task<AuthValidationOutcome<JoinTicket>> IssueRejoinTicketAsync(PlayerAuthIdentity identity,
            Guid sessionId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ValidatePlayerIdentity(identity);
            if (sessionId == Guid.Empty)
            {
                return Task.FromResult(AuthValidationOutcome<JoinTicket>.Rejected(
                    ContractCodes.RejectReason.SESSION_MISMATCH));
            }

            return InTransactionAsync(async (connection, transaction, token) =>
            {
                // 잠금 순서: 세션 -> 계정 -> 로그인 묶음 -> 인스턴스, 임대 -> 참가자. 시작, 만료 해제, 정산과 같은 방향이다
                if (!await LockJoinSessionAsync(connection, transaction, sessionId, token))
                {
                    return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                // 요청자 계정의 참가자 행만 찾는다. 다른 계정의 participantId를 대신 받을 방법이 없다
                RejoinRow? target = await connection.QuerySingleOrDefaultAsync<RejoinRow>(Command(
                    """
                    SELECT participant.participant_id AS "ParticipantId", participant.character_id AS "CharacterId",
                        participant.state AS "ParticipantState", session.state AS "SessionState",
                        participant.departed_at IS NOT NULL AS "Departed",
                        COALESCE(session.auction_ends_at > clock_timestamp(), false) AS "AuctionOpen",
                        session.instance_id AS "InstanceId", instance.address AS "Address", instance.port AS "Port"
                    FROM session_participants AS participant
                    JOIN game_sessions AS session ON session.session_id = participant.session_id
                    JOIN server_instances AS instance ON instance.instance_id = session.instance_id
                    WHERE participant.session_id = @SessionId AND participant.account_id = @AccountId;
                    """, new { SessionId = sessionId, identity.AccountId }, transaction, token));
                if (target == null)
                {
                    return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                // FOR NO KEY UPDATE: 같은 계정의 다른 쓰기(접속 임대, 토큰 발급)와는 직렬화하되 계정을 참조하는
                // 행 삽입의 외래 키 확인(KEY SHARE)은 막지 않는다. 반복 복귀 요청이 계정 참조 삽입을 줄 세우지 않게 한다
                await LockAccountNoKeyAsync(connection, transaction, identity.AccountId, token);
                if (!await LockActiveIdentityAsync(connection, transaction, identity, token))
                {
                    return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                int? phase = RejoinPhaseReason(target);
                if (phase.HasValue)
                {
                    return AuthValidationOutcome<JoinTicket>.Rejected(phase.Value);
                }

                Guid? dungeonLease = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                    """
                    SELECT lease.lease_id FROM auth_server_leases AS lease JOIN server_instances AS instance
                        ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                    WHERE lease.instance_id = @InstanceId AND lease.role = @Dungeon
                        AND lease.revoked_at IS NULL AND lease.expires_at > clock_timestamp()
                        AND instance.state <> 'Offline' AND instance.heartbeat_at > clock_timestamp() - @Heartbeat
                    ORDER BY lease.expires_at DESC, lease.lease_id DESC LIMIT 1 FOR SHARE OF instance, lease;
                    """, new { target.InstanceId, Dungeon = ContractCodes.ServerRole.DUNGEON,
                        Heartbeat = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) }, transaction, token));
                if (!dungeonLease.HasValue
                    || !await LockJoinContextAsync(connection, transaction, sessionId, target.InstanceId,
                        dungeonLease.Value, token)
                    || !await LockIssuableParticipantAsync(connection, transaction, sessionId, target.ParticipantId,
                        identity.AccountId, target.CharacterId, null, token))
                {
                    return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
                }

                IssuedAuthToken? issued = await InsertDungeonJoinTokenAsync(connection, transaction,
                    new DungeonTokenTarget(identity.AccountId, identity.FamilyId, target.CharacterId,
                        target.ParticipantId, sessionId, target.InstanceId, dungeonLease.Value),
                    TimeSpan.FromSeconds(JOIN_SECONDS), token);
                if (issued == null)
                {
                    return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED);
                }

                return AuthValidationOutcome<JoinTicket>.Success(new JoinTicket(issued.Token, issued.ExpiresAtUnixMs,
                    ContractCodes.ServerRole.DUNGEON, target.InstanceId.ToString("D"), target.Address, target.Port,
                    target.CharacterId.ToString("D"), target.ParticipantId.ToString("D"), issued.ServerTimeUnixMs));
            }, cancellationToken);
        }

        private static async Task LockAccountNoKeyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid accountId, CancellationToken token)
        {
            Guid? locked = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                "SELECT account_id FROM accounts WHERE account_id = @AccountId FOR NO KEY UPDATE;",
                new { AccountId = accountId }, transaction, token));
            if (!locked.HasValue)
            {
                throw new ArgumentException("The account must exist.", nameof(accountId));
            }
        }

        private static int? RejoinPhaseReason(RejoinRow target)
        {
            // 포기, 접속 만료로 떠난 참가자는 정산 전이어도 복귀하지 않는다(PARTICIPANT_DEPARTED)
            if ((target.ParticipantState == GameSessionStates.PARTICIPANT_SETTLED)
                || (target.ParticipantState == GameSessionStates.PARTICIPANT_RELEASED) || target.Departed)
            {
                return ContractCodes.RejectReason.ALREADY_SETTLED;
            }
            if (target.SessionState == GameSessionStates.RESERVED)
            {
                // 시작 전 입장은 예약한 마을 서버의 입장권 경로다
                return ContractCodes.RejectReason.WRONG_PHASE;
            }

            // 보스 성공 뒤 경매 단계는 마감(auction_ends_at) 전까지 끊긴 참가자의 복귀를 받는다. 마감 뒤는 종결로 본다
            bool auctionOpen = (target.SessionState == GameSessionStates.AUCTION) && target.AuctionOpen;
            if ((target.SessionState != GameSessionStates.RUNNING) && !auctionOpen)
            {
                return ContractCodes.RejectReason.SESSION_TERMINAL;
            }

            return target.ParticipantState == GameSessionStates.PARTICIPANT_ACTIVE
                ? null : ContractCodes.RejectReason.SESSION_MISMATCH;
        }

        // Reserved 출정은 예약한 마을 서버의 입장권만, Running과 마감 전 Auction 출정은 떠나지 않은 본인 복귀만 발급한다.
        // 마을 임대 조건은 Reserved에만 걸며 townInstanceId가 없으면 Reserved 분기가 성립하지 않는다
        private static async Task<bool> LockIssuableParticipantAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, Guid sessionId, Guid participantId, Guid accountId, Guid characterId,
            Guid? townInstanceId, CancellationToken token)
        {
            Guid? participant = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                """
                SELECT participant.participant_id FROM session_participants AS participant JOIN game_sessions AS session
                    ON session.session_id = participant.session_id
                WHERE participant.session_id = @SessionId AND participant.participant_id = @ParticipantId
                    AND participant.account_id = @AccountId AND participant.character_id = @CharacterId
                    AND participant.state IN ('Reserved','Active') AND session.state IN ('Reserved','Running','Auction')
                    AND participant.departed_at IS NULL
                    AND ((session.state = 'Reserved' AND participant.state = 'Reserved'
                            AND session.reserved_by_instance_id = @TownInstanceId
                            AND session.expires_at > clock_timestamp())
                        OR (session.state = 'Running' AND participant.state = 'Active'
                            AND CAST(@TownInstanceId AS uuid) IS NULL)
                        OR (session.state = 'Auction' AND participant.state = 'Active'
                            AND session.auction_ends_at > clock_timestamp()
                            AND CAST(@TownInstanceId AS uuid) IS NULL))
                FOR SHARE OF participant;
                """, new { SessionId = sessionId, ParticipantId = participantId, AccountId = accountId,
                    CharacterId = characterId, TownInstanceId = townInstanceId }, transaction, token));
            return participant.HasValue;
        }

        // 같은 참가자의 미교환 토큰은 폐기한다. 만료는 요청 수명, Reserved 예약 만료, Auction 마감, 로그인 묶음 만료 중 이른 값이다
        private async Task<IssuedAuthToken?> InsertDungeonJoinTokenAsync(NpgsqlConnection connection,
            NpgsqlTransaction transaction, DungeonTokenTarget target, TimeSpan lifetime, CancellationToken token)
        {
            Guid id = Guid.NewGuid();
            string raw = _hasher.CreateToken();
            DateTime now = await ReadTimeAsync(connection, transaction, token);
            DateTime expires = await connection.QuerySingleAsync<DateTime>(Command(
                """
                SELECT LEAST(@RequestedExpires,
                    CASE WHEN session.state = 'Reserved' THEN session.expires_at
                        WHEN session.state = 'Auction' THEN session.auction_ends_at ELSE 'infinity'::timestamptz END,
                    COALESCE((
                        SELECT MAX(refresh.expires_at) FROM auth_refresh_tokens AS refresh
                        WHERE refresh.account_id = @AccountId AND refresh.family_id = @FamilyId
                            AND refresh.revoked_at IS NULL AND refresh.consumed_at IS NULL
                            AND refresh.expires_at > clock_timestamp()), '-infinity'::timestamptz))
                FROM game_sessions AS session WHERE session.session_id = @SessionId;
                """, new { RequestedExpires = now.Add(lifetime), target.AccountId, target.FamilyId, target.SessionId },
                transaction, token));
            if (expires <= now)
            {
                return null;
            }

            await connection.ExecuteAsync(Command(
                """
                UPDATE auth_join_tokens SET revoked_at = COALESCE(revoked_at,clock_timestamp())
                    WHERE session_id = @SessionId AND participant_id = @ParticipantId AND consumed_at IS NULL;
                INSERT INTO auth_join_tokens(token_id,account_id,family_id,character_id,participant_id,session_id,
                    instance_id,server_lease_id,token_hash,expires_at,server_role)
                VALUES (@Id,@AccountId,@FamilyId,@CharacterId,@ParticipantId,@SessionId,@InstanceId,@LeaseId,
                    @Hash,@Expires,@Dungeon);
                """, new { Id = id, target.AccountId, target.FamilyId, target.CharacterId, target.ParticipantId,
                    target.SessionId, target.InstanceId, target.LeaseId, Dungeon = ContractCodes.ServerRole.DUNGEON,
                    Hash = _hasher.Hash(AuthTokenHasher.JOIN_DOMAIN, raw), Expires = expires }, transaction, token));
            return new IssuedAuthToken(raw, id, UnixMilliseconds(expires), UnixMilliseconds(now));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed record DungeonTokenTarget(Guid AccountId, Guid FamilyId, Guid CharacterId, Guid ParticipantId,
            Guid SessionId, Guid InstanceId, Guid LeaseId);

        private sealed class RejoinRow
        {
            public Guid ParticipantId { get; set; }
            public Guid CharacterId { get; set; }
            public string ParticipantState { get; set; } = string.Empty;
            public string SessionState { get; set; } = string.Empty;
            public bool Departed { get; set; }
            public bool AuctionOpen { get; set; }
            public Guid InstanceId { get; set; }
            public string Address { get; set; } = string.Empty;
            public int Port { get; set; }
        }
    }
}
