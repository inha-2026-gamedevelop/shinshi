// System
using System;
using System.Data.Common;
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

        /// <summary> 프롤로그 입장 작업 트랜잭션 안에서 배정 던전의 60초 일회 입장 토큰을 넣는다. 토큰 형식, 클레임, 교환 경로는
        /// 마을이 받는 던전 입장권과 같다. 원문은 (계정, operationId)에서 파생해 저장하지 않고 재전송 때 다시 만든다.
        /// 호출자가 계정 행을 먼저 잠가야 한다. 성공이면 NONE, 아니면 거절 사유다 </summary>
        public async Task<int> InsertPrologueJoinTokenAsync(DbConnection connection, DbTransaction transaction,
            PlayerAuthIdentity identity, Guid operationId, Guid sessionId, Guid participantId, Guid characterId,
            Guid instanceId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ValidatePlayerIdentity(identity);
            if ((connection is not NpgsqlConnection npgsql) || (transaction is not NpgsqlTransaction npgsqlTransaction))
            {
                throw new InvalidOperationException("Prologue join tokens need a PostgreSQL transaction.");
            }
            if ((operationId == Guid.Empty) || (sessionId == Guid.Empty) || (participantId == Guid.Empty)
                || (characterId == Guid.Empty) || (instanceId == Guid.Empty))
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }

            // 잠금 순서: (호출자의 계정) -> 로그인 묶음 -> 인스턴스, 임대. 마을 입장권 발급과 같은 방향이다
            if (!await LockActiveIdentityAsync(npgsql, npgsqlTransaction, identity, cancellationToken))
            {
                return ContractCodes.RejectReason.TOKEN_REVOKED;
            }

            Guid? dungeonLease = await npgsql.QuerySingleOrDefaultAsync<Guid?>(Command(
                """
                SELECT lease.lease_id FROM auth_server_leases AS lease JOIN server_instances AS instance
                    ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                WHERE lease.instance_id = @InstanceId AND lease.role = @Dungeon
                    AND lease.revoked_at IS NULL AND lease.expires_at > clock_timestamp()
                    AND lease.boot_id IS NOT DISTINCT FROM instance.boot_id
                    AND instance.state <> 'Offline' AND instance.heartbeat_at > clock_timestamp() - @Heartbeat
                ORDER BY lease.expires_at DESC, lease.lease_id DESC LIMIT 1 FOR SHARE OF instance, lease;
                """, new { InstanceId = instanceId, Dungeon = ContractCodes.ServerRole.DUNGEON,
                    Heartbeat = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) }, npgsqlTransaction, cancellationToken));
            if (!dungeonLease.HasValue)
            {
                return ContractCodes.RejectReason.SESSION_MISMATCH;
            }

            IssuedAuthToken? issued = await InsertDungeonJoinTokenAsync(npgsql, npgsqlTransaction,
                new DungeonTokenTarget(identity.AccountId, identity.FamilyId, characterId, participantId, sessionId,
                    instanceId, dungeonLease.Value), TimeSpan.FromSeconds(JOIN_SECONDS), cancellationToken,
                CreatePrologueJoinToken(identity.AccountId, operationId));
            return issued == null ? ContractCodes.RejectReason.TOKEN_EXPIRED : ContractCodes.RejectReason.NONE;
        }

        /// <summary> 확정된 프롤로그 입장 작업의 입장권을 다시 만든다. 처음 응답과 재전송이 같은 경로를 쓴다.
        /// 아직 교환, 폐기, 만료되지 않았으면 같은 원문을 돌려주고, 교환이나 폐기(새 입장 요청이 대체)는 TOKEN_REVOKED,
        /// 만료나 보존 기간 뒤 삭제는 TOKEN_EXPIRED다. 그때 클라이언트는 새 operationId로 다시 요청한다 </summary>
        public async Task<AuthValidationOutcome<JoinTicket>> ReadPrologueTicketAsync(PlayerAuthIdentity identity,
            Guid operationId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ValidatePlayerIdentity(identity);
            if (operationId == Guid.Empty)
            {
                return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            string raw = CreatePrologueJoinToken(identity.AccountId, operationId);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            PrologueTicketRow? row = await connection.QuerySingleOrDefaultAsync<PrologueTicketRow>(new CommandDefinition(
                """
                SELECT ticket.expires_at AS "ExpiresAt",
                    (ticket.consumed_at IS NOT NULL OR ticket.revoked_at IS NOT NULL) AS "Spent",
                    ticket.expires_at <= clock_timestamp() AS "Expired", clock_timestamp() AS "Now",
                    ticket.instance_id AS "InstanceId", instance.address AS "Address", instance.port AS "Port",
                    ticket.character_id AS "CharacterId", ticket.participant_id AS "ParticipantId"
                FROM auth_join_tokens AS ticket
                JOIN server_instances AS instance ON instance.instance_id = ticket.instance_id
                WHERE ticket.token_hash = @Hash AND ticket.account_id = @AccountId AND ticket.server_role = @Dungeon;
                """, new { Hash = _hasher.Hash(AuthTokenHasher.JOIN_DOMAIN, raw), identity.AccountId,
                    Dungeon = ContractCodes.ServerRole.DUNGEON }, cancellationToken: cancellationToken));
            if (row == null)
            {
                return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED);
            }
            if (row.Spent)
            {
                return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
            }
            if (row.Expired)
            {
                return AuthValidationOutcome<JoinTicket>.Rejected(ContractCodes.RejectReason.TOKEN_EXPIRED);
            }

            return AuthValidationOutcome<JoinTicket>.Success(new JoinTicket(raw, UnixMilliseconds(row.ExpiresAt),
                ContractCodes.ServerRole.DUNGEON, row.InstanceId.ToString("D"), row.Address, row.Port,
                row.CharacterId.ToString("D"), row.ParticipantId.ToString("D"), UnixMilliseconds(row.Now)));
        }

        // 계정을 seed에 넣어 다른 계정이 같은 operationId를 써도 원문이 겹치지 않는다
        private string CreatePrologueJoinToken(Guid accountId, Guid operationId)
        {
            return _hasher.CreateDerivedToken(AuthTokenHasher.PROLOGUE_JOIN_DOMAIN,
                accountId.ToString("D") + "\n" + operationId.ToString("D"));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class PrologueTicketRow
        {
            public DateTime ExpiresAt { get; set; }
            public bool Spent { get; set; }
            public bool Expired { get; set; }
            public DateTime Now { get; set; }
            public Guid InstanceId { get; set; }
            public string Address { get; set; } = string.Empty;
            public int Port { get; set; }
            public Guid CharacterId { get; set; }
            public Guid ParticipantId { get; set; }
        }
    }
}
