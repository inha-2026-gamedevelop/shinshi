// System
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Auctions;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Data.Admin
{
    /// <summary> 복구 대상 세션 목록, 수동 복구 전후 상태 조회, 수동 복구의 운영자 작업 키 결속. 세션 상태는 바꾸지 않는다
    /// (종결은 기존 정산 저장소가 한다) </summary>
    public sealed class PostgresAdminRecoveryRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // game_sessions.state CHECK와 같은 값. 목록의 state 필터는 이 중 하나만 받는다
        public static readonly IReadOnlyList<string> G_SessionStates = Array.AsReadOnly(new[]
        {
            "Reserved", "Loading", "Starting", "Running", "Settling", "Auction", "Returning", "RecoveryPending", "Closed"
        });

        // 배정 boot가 Offline이 아니고 @Timeout 안에 heartbeat를 보냈다. session, instance 별칭이 필요하다
        private const string BOOT_ALIVE_SQL =
            """
            COALESCE(instance.boot_id = session.instance_boot_id AND instance.state <> 'Offline'
                AND instance.heartbeat_at > clock_timestamp() - @{0}, false)
            """;

        private const string SETTLING_STALE_SQL =
            "(session.state = 'Settling' AND session.settling_at <= clock_timestamp() - @SettlingTimeout)";

        // 자동 복구가 장애 취소로 보는 기준(60초)과 같다. 판정 자체는 복구 실행 때 정산 저장소가 다시 한다
        private static readonly string G_InstanceLostSql =
            "(session.state = 'Running' AND NOT " + string.Format(BOOT_ALIVE_SQL, "CancelTimeout") + ")";

        // 경매 단계 자동 복구(유지 작업)가 경매를 닫는 두 조건. 기한 + 유예 경과와 배정 boot 소실이다
        internal const string AUCTION_DEADLINE_SQL =
            "(session.state = 'Auction' AND session.auction_ends_at + @AuctionGrace < clock_timestamp())";
        internal static readonly string G_AuctionLostSql =
            "(session.state = 'Auction' AND " + PostgresPartyAuctionRepository.LOST_CONDITION + ")";

        // 기본 목록: RecoveryPending, 자동 복구가 한 번 이상 실패했거나 백오프 중인 진행/정산 세션,
        // 정산 대기 시간이 지난 Settling, 배정 서버 생존 증거가 없는 Running, 멈춘 Auction
        private static readonly string G_RecoveryCandidateSql =
            "(session.state = 'RecoveryPending'"
            + " OR (session.state IN ('Running', 'Settling')"
            + " AND (session.recovery_failures > 0 OR session.next_recovery_at IS NOT NULL))"
            + " OR " + SETTLING_STALE_SQL + " OR " + G_InstanceLostSql
            + " OR " + AUCTION_DEADLINE_SQL + " OR " + G_AuctionLostSql + ")";

        // 같은 운영자 작업 키의 수동 복구를 직렬화하는 트랜잭션 advisory 잠금의 첫 키. 두 정수 키 공간은 부트스트랩, 마이그레이션이
        // 쓰는 bigint 한 개 키 공간과 겹치지 않는다. 둘째 키는 (운영자, operationId)의 hashtext이며 충돌해도 직렬화가 늘 뿐이다
        private const int CLAIM_LOCK_SPACE = 0x53484143;

        private readonly NpgsqlDataSource _dataSource;
        private readonly PostgresAdminOperationClaimRepository _claims;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminRecoveryRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
            _claims     = new PostgresAdminOperationClaimRepository(dataSource);
        }

        /// <summary> state가 없으면 복구 관심 대상, 있으면 그 상태의 세션을 생성 시각, ID 순으로 읽는다.
        /// livenessDeferred면 서버 소실 판정을 믿을 수 없는 동안이라 instanceLost를 모두 false로 내리고 페이지에 표시한다 </summary>
        public async Task<AdminRecoverySessionPage> ListAsync(string? state, AdminLogCursor? cursor, int limit,
            CancellationToken cancellationToken = default, bool livenessDeferred = false)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }
            if ((state != null) && (!G_SessionStates.Contains(state)))
            {
                throw new ArgumentException("Unknown session state.", nameof(state));
            }

            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("HeartbeatTimeout", ServerPoolTimings.HeartbeatTimeout);
            parameters.Add("CancelTimeout", ServerPoolTimings.ServiceCancelTimeout);
            parameters.Add("SettlingTimeout", ServerPoolTimings.SettlingRecoveryDelay);
            parameters.Add("AuctionGrace", PartyAuctionRules.PhaseRecoveryGrace);
            parameters.Add("Fetch", limit + 1);
            string filter = G_RecoveryCandidateSql;
            if (state != null)
            {
                filter = "session.state = @State";
                parameters.Add("State", state);
            }
            if (cursor != null)
            {
                filter += " AND (session.created_at, session.session_id) > (@CursorAt, @CursorId)";
                parameters.Add("CursorAt", cursor.At);
                parameters.Add("CursorId", cursor.Id);
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            List<AdminRecoveryRow> rows = (await connection.QueryAsync<AdminRecoveryRow>(new CommandDefinition(
                "SELECT " + AdminSessionRow.SELECT_COLUMNS + ","
                + """

                    instance.instance_id AS "ServerInstanceId", instance.boot_id AS "ServerBootId",
                    instance.state AS "ServerState", instance.heartbeat_at AS "ServerHeartbeatAt",
                """
                + " " + string.Format(BOOT_ALIVE_SQL, "HeartbeatTimeout") + " AS \"AssignedBootAlive\", "
                + SETTLING_STALE_SQL + " AS \"SettlingStale\", " + G_InstanceLostSql + " AS \"InstanceLost\", "
                + AUCTION_DEADLINE_SQL + " AS \"AuctionDeadlinePassed\", " + G_AuctionLostSql + " AS \"AuctionInstanceLost\""
                + " FROM game_sessions AS session"
                + " JOIN server_instances AS instance ON instance.instance_id = session.instance_id"
                + " WHERE " + filter
                + " ORDER BY session.created_at, session.session_id LIMIT @Fetch;",
                parameters, cancellationToken: cancellationToken))).ToList();

            bool hasMore = rows.Count > limit;
            List<AdminRecoveryRow> page = hasMore ? rows.GetRange(0, limit) : rows;
            string? nextCursor = hasMore
                ? new AdminLogCursor(page[page.Count - 1].CreatedAt, page[page.Count - 1].SessionId).Encode()
                : null;
            return new AdminRecoverySessionPage(page.Select(row =>
                row.ToRecoveryContract(PostgresSettlementRepository.MAX_RECOVERY_FAILURES, livenessDeferred)).ToArray(),
                nextCursor, livenessDeferred);
        }

        /// <summary> 자체 연결로 세션 상태를 읽는다. 없으면 null이다 </summary>
        public async Task<AdminRecoveryTarget?> ReadTargetAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await ReadTargetAsync(connection, null, sessionId, cancellationToken);
        }

        /// <summary> 어드민 작업 트랜잭션 안에서 복구 뒤 상태를 읽는다 </summary>
        public Task<AdminRecoveryTarget?> ReadTargetAsync(OperationExecutionContext context, Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            return ReadTargetAsync(connection, transaction, sessionId, cancellationToken);
        }

        /// <summary> 운영자 작업 키를 이번 대상과 요청 해시에 묶고(이미 묶였으면 그 결속을 읽고), 맞으면 같은 키의 실행을 끝날 때까지
        /// 직렬화하는 잠금과 행위자 행 FOR SHARE를 쥔 결속을 돌려준다. 결속은 서버 복구보다 먼저 커밋한다. 복구가 확정된 뒤
        /// 어드민 단계가 실패하거나 프로세스가 죽어도 이 operationId로 다른 세션을 복구할 수 없게 하기 위해서다.
        /// 잠금 대기가 제한을 넘으면 재시도 가능한 DB 예외(503)다 </summary>
        public async Task<AdminOperationClaim> ClaimAsync(AdminIdentity admin, OperationRequest operation,
            string targetKey, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(admin);
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(targetKey);
            if (operation.ActorId != admin.AdminId)
            {
                throw new ArgumentException("The operation actor must be the admin.", nameof(operation));
            }

            AdminOperationBinding binding = await _claims.BindAsync(operation, targetKey, cancellationToken);
            if (!binding.Matches)
            {
                return new AdminOperationClaim(null, null, false, false, binding.ServerTime);
            }

            NpgsqlConnection lockConnection = await _dataSource.OpenConnectionAsync(cancellationToken);
            NpgsqlTransaction? lockTransaction = null;
            try
            {
                lockTransaction = await lockConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted,
                    cancellationToken);
                await PostgresTransactionPolicy.ApplyAsync(lockConnection, lockTransaction, cancellationToken);
                string lockKey = admin.AdminId.ToString("D") + "/" + operation.OperationId.ToString("D");
                await lockConnection.ExecuteAsync(new CommandDefinition(
                    "SELECT pg_advisory_xact_lock(@Space, hashtext(@Key));",
                    new { Space = CLAIM_LOCK_SPACE, Key = lockKey }, lockTransaction,
                    cancellationToken: cancellationToken));
                bool actorActive = await AdminOperationGuard.LockActorAsync(lockConnection, lockTransaction, admin,
                    cancellationToken);
                return new AdminOperationClaim(lockConnection, lockTransaction, true, actorActive, binding.ServerTime);
            }
            catch
            {
                // 결속을 돌려주기 전에 실패했으므로 잠금 트랜잭션과 연결을 여기서 정리한다
                try
                {
                    if (lockTransaction != null)
                    {
                        await lockTransaction.DisposeAsync();
                    }
                }
                finally
                {
                    await lockConnection.DisposeAsync();
                }
                throw;
            }
        }

        /// <summary> 같은 actor, operationId의 작업이 이미 저장됐는지. 저장된 어드민 작업은 효과를 다시 실행하지 않는다 </summary>
        public async Task<bool> OperationExistsAsync(Guid actorId, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM operations WHERE actor_id = @ActorId AND operation_id = @OperationId);",
                new { ActorId = actorId, OperationId = operationId }, cancellationToken: cancellationToken));
        }

        private static async Task<AdminRecoveryTarget?> ReadTargetAsync(DbConnection connection,
            DbTransaction? transaction, Guid sessionId, CancellationToken cancellationToken)
        {
            AdminSessionRow? row = await connection.QuerySingleOrDefaultAsync<AdminSessionRow>(new CommandDefinition(
                "SELECT " + AdminSessionRow.SELECT_COLUMNS
                + " FROM game_sessions AS session WHERE session.session_id = @SessionId;",
                new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));
            return row == null ? null : new AdminRecoveryTarget(row);
        }

    }
}
