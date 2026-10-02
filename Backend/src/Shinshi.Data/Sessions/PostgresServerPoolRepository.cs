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

namespace Shinshi.Data.Sessions
{
    /// <summary> 서버 풀 등록 결과. 장애 취소가 Offline으로 확정한 프로세스는 heartbeat로 되살리지 않고 새 bootId로 등록한다.
    /// OtherBootAlive는 같은 인스턴스의 다른 프로세스가 아직 살아 있어 새 boot를 받지 않았다는 일시 거절이다 </summary>
    public enum EServerPoolStatus
    {
        Accepted         = 0,
        InstanceNotFound = 1,
        RoleMismatch     = 2,
        BootMismatch     = 3,
        HeartbeatExpired = 4,
        Offline          = 5,
        OtherBootAlive   = 6
    }

    /// <summary> 서버 풀 변경 결과와 DB 기준 현재 상태 </summary>
    public sealed class ServerPoolOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public EServerPoolStatus Status     { get; }
        public string            State      { get; }
        public DateTimeOffset    ServerTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServerPoolOutcome(EServerPoolStatus status, string state, DateTimeOffset serverTime)
        {
            Status     = status;
            State      = state;
            ServerTime = serverTime.ToUniversalTime();
        }
    }

    /// <summary> 게임 서버 프로세스의 등록 정보 </summary>
    public sealed class ServerRegistration
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   InstanceId     { get; }
        public Guid   BootId         { get; }
        public int    Role           { get; }
        public string Address        { get; }
        public int    Port           { get; }
        public string BuildId        { get; }
        public string ContentVersion { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServerRegistration(Guid instanceId, Guid bootId, int role, string address, int port, string buildId,
            string contentVersion)
        {
            InstanceId     = instanceId;
            BootId         = bootId;
            Role           = role;
            Address        = address;
            Port           = port;
            BuildId        = buildId;
            ContentVersion = contentVersion;
        }

        /// <summary> 저장 전 형식 검증. 실패하면 요청 형식 오류로 거절한다 </summary>
        public bool IsValid()
        {
            const int MIN_PORT = 1;
            const int MAX_PORT = 65535;
            return (InstanceId != Guid.Empty) && (BootId != Guid.Empty)
                && ((Role == ContractCodes.ServerRole.TOWN) || (Role == ContractCodes.ServerRole.DUNGEON))
                && !string.IsNullOrWhiteSpace(Address) && (Port >= MIN_PORT) && (Port <= MAX_PORT)
                && !string.IsNullOrWhiteSpace(BuildId) && !string.IsNullOrWhiteSpace(ContentVersion);
        }
    }

    /// <summary> 배정 가능한 유휴 던전 하나 </summary>
    public sealed class AllocatedDungeon
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   InstanceId    { get; set; }
        public Guid   BootId        { get; set; }
        public Guid   ServerLeaseId { get; set; }
        public string Address       { get; set; } = string.Empty;
        public int    Port          { get; set; }
    }

    /// <summary> server_instances 등록, heartbeat, 드레인과 유휴 던전 배정. 생존 판정은 DB 시각으로 한다 </summary>
    public sealed class PostgresServerPoolRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // heartbeat, 드레인, 같은 boot 재등록은 시간 조건 없이 Offline만 거절한다. 네트워크가 15초 넘게 끊겼던 프로세스도
        // 같은 bootId로 돌아올 수 있어야 하고, 되돌릴 수 없는 사망은 장애 취소 트랜잭션이 Offline을 쓰는 순간뿐이다
        private const string OWNED_CONDITION = "state <> 'Offline'";

        // 등록과 임대 발급이 함께 쓰는 '다른 boot가 살아 있음' 조건의 테이블 별칭 자리
        private const string ALIAS_TOKEN = "{alias}";

        // 같은 인스턴스에 boot가 둘 살아 있으면 두 프로세스가 같은 출정과 접속을 받는다. Offline이 아니고 heartbeat가 15초 안이면
        // 살아 있다고 본다. 단 출정을 마치고 Draining이 된 던전 boot는 열린 출정이 없으면 새 boot가 바로 넘겨받는다.
        // 마을의 Draining은 운영 드레인이라 접속자가 남아 있을 수 있어 예외로 두지 않는다
        private const string LIVE_BOOT_TEMPLATE =
            """
            ({alias}.boot_id IS NOT NULL AND {alias}.state <> 'Offline'
                AND {alias}.heartbeat_at > clock_timestamp() - @HeartbeatTimeout
                AND NOT ({alias}.role = @LiveBootDungeonRole AND {alias}.state = 'Draining'
                    AND NOT EXISTS (SELECT 1 FROM game_sessions AS open_session
                        WHERE open_session.instance_id = {alias}.instance_id
                            AND open_session.instance_boot_id = {alias}.boot_id
                            AND open_session.state IN ('Reserved', 'Running', 'Settling', 'Auction'))))
            """;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresServerPoolRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 새 bootId면 Idle로 등록한다. 같은 bootId 재전송은 살아 있을 때만 heartbeat로 처리한다 </summary>
        public Task<ServerPoolOutcome> RegisterAsync(ServerRegistration registration,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (!registration.IsValid())
            {
                throw new ArgumentException("Server registration is invalid.", nameof(registration));
            }

            return SessionDb.RetryAsync(token => RegisterOnceAsync(registration, token), cancellationToken);
        }

        /// <summary> 같은 bootId면 늦게 온 heartbeat도 되살린다. Offline으로 확정된 boot는 되살리지 못한다.
        /// 받아들인 heartbeat는 그 인스턴스가 쥔 살아 있는 계정 접속 임대도 함께 연장한다 </summary>
        public Task<ServerPoolOutcome> HeartbeatAsync(Guid instanceId, Guid bootId,
            CancellationToken cancellationToken = default)
        {
            ValidateIds(instanceId, bootId);
            return SessionDb.RetryAsync(async token =>
            {
                ServerPoolOutcome outcome = await ChangeOwnedAsync(instanceId, bootId,
                    "UPDATE server_instances SET heartbeat_at = clock_timestamp() WHERE instance_id = @InstanceId "
                    + "AND boot_id = @BootId AND " + OWNED_CONDITION + " RETURNING state;", token);
                if (outcome.Status == EServerPoolStatus.Accepted)
                {
                    await ExtendAccountLeasesAsync(instanceId, token);
                }

                return outcome;
            }, cancellationToken);
        }

        /// <summary> 효과 경로 호출 자체를 생존 증거로 heartbeat 시각을 갱신한다. 임대 boot가 등록 boot와 같고 Offline이 아닐 때만 바꾼다.
        /// 효과 트랜잭션 밖의 짧은 문장이라 효과의 잠금 순서에 끼지 않는다 </summary>
        public async Task TouchAsync(Guid instanceId, Guid bootId, CancellationToken cancellationToken = default)
        {
            ValidateIds(instanceId, bootId);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() WHERE instance_id = @InstanceId "
                + "AND boot_id = @BootId AND " + OWNED_CONDITION + ";",
                new { InstanceId = instanceId, BootId = bootId }, cancellationToken: cancellationToken));
        }

        /// <summary> 테이블 별칭에 맞춘 '다른 boot가 살아 있음' SQL 조건. 매개변수 @HeartbeatTimeout, @LiveBootDungeonRole이 필요하다 </summary>
        public static string LiveBootCondition(string alias)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(alias);
            return LIVE_BOOT_TEMPLATE.Replace(ALIAS_TOKEN, alias, StringComparison.Ordinal);
        }

        /// <summary> 새 배정을 멈춘다. 진행 중인 출정은 끝까지 진행하고 종료 뒤에도 Idle로 돌아가지 않는다.
        /// 멱등이며 heartbeat와 같은 생존 신호다(heartbeat 시각과 계정 접속 임대를 함께 연장한다) </summary>
        public Task<ServerPoolOutcome> DrainAsync(Guid instanceId, Guid bootId, CancellationToken cancellationToken = default)
        {
            ValidateIds(instanceId, bootId);
            return SessionDb.RetryAsync(async token =>
            {
                ServerPoolOutcome outcome = await ChangeOwnedAsync(instanceId, bootId,
                    "UPDATE server_instances SET state = 'Draining', heartbeat_at = clock_timestamp() "
                    + "WHERE instance_id = @InstanceId AND boot_id = @BootId AND " + OWNED_CONDITION + " RETURNING state;",
                    token);
                if (outcome.Status == EServerPoolStatus.Accepted)
                {
                    await ExtendAccountLeasesAsync(instanceId, token);
                }

                return outcome;
            }, cancellationToken);
        }

        /// <summary> 호출 트랜잭션 안에서 살아 있는 유휴 던전 하나를 Reserved로 바꾼다. 다른 배정이 잠근 행은 건너뛴다 </summary>
        public static async Task<AllocatedDungeon?> TryAllocateIdleDungeonAsync(DbConnection connection,
            DbTransaction transaction, string contentVersion, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentException.ThrowIfNullOrWhiteSpace(contentVersion);

            // SKIP LOCKED라 동시 출정끼리 같은 인스턴스를 기다리지 않는다. 기다리지 않으므로 잠금 순서에도 끼지 않는다.
            // 등록한 현재 프로세스(boot)의 살아 있는 서버 임대가 있는 던전만 배정한다. 새 boot가 임대만 받고 등록 전이면
            // 아직 이전 boot의 행이라 배정하지 않는다. 먼저 등록한 프로세스부터 고른다. heartbeat 최신순은 주기마다 순서가 흔들린다
            AllocatedDungeon? candidate = await connection.QuerySingleOrDefaultAsync<AllocatedDungeon>(new CommandDefinition(
                """
                SELECT instance.instance_id AS "InstanceId", instance.boot_id AS "BootId",
                    lease.lease_id AS "ServerLeaseId", instance.address AS "Address", instance.port AS "Port"
                FROM server_instances AS instance
                JOIN auth_server_leases AS lease ON lease.instance_id = instance.instance_id AND lease.role = instance.role
                    AND lease.revoked_at IS NULL AND lease.expires_at > clock_timestamp()
                    AND lease.boot_id = instance.boot_id
                WHERE instance.role = @Role AND instance.state = 'Idle' AND instance.boot_id IS NOT NULL
                    AND instance.content_version = @ContentVersion
                    AND instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout
                ORDER BY instance.registered_at, instance.instance_id
                LIMIT 1
                FOR UPDATE OF instance SKIP LOCKED;
                """, new { Role = ContractCodes.ServerRole.DUNGEON, ContentVersion = contentVersion,
                    HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout },
                transaction, cancellationToken: cancellationToken));
            if (candidate == null)
            {
                return null;
            }

            int changed = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE server_instances SET state = 'Reserved' WHERE instance_id = @InstanceId AND state = 'Idle';",
                new { candidate.InstanceId }, transaction, cancellationToken: cancellationToken));
            return changed == 1 ? candidate : null;
        }

        /// <summary> 출정이 끝난(정산, 예약 해제) 인스턴스를 Draining으로 바꾼다. 출정을 맡았던 boot는 다시 배정하지 않는다.
        /// 같은 프로세스(bootId)이고 Reserved, Active일 때만 바꾼다. Idle은 새 boot의 등록만 만든다 </summary>
        public static Task ReturnToPoolAsync(DbConnection connection, DbTransaction transaction, Guid instanceId,
            Guid? bootId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);

            // 출정 상태가 남은 프로세스를 다음 출정에 다시 쓰지 않는다. 던전은 Draining을 보고 종료한 뒤 새 bootId로 등록한다.
            // Offline은 유지하고, 재등록한 새 프로세스의 상태는 이전 출정 종료가 바꾸지 않는다
            return connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE server_instances SET state = 'Draining'
                WHERE instance_id = @InstanceId AND boot_id = @BootId AND state IN ('Reserved', 'Active');
                """, new { InstanceId = instanceId, BootId = bootId }, transaction, cancellationToken: cancellationToken));
        }

        private async Task<ServerPoolOutcome> RegisterOnceAsync(ServerRegistration registration,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            DynamicParameters parameters = new DynamicParameters(registration);
            AddLiveBootParameters(parameters);

            // 다른 boot가 아직 살아 있으면 덮지 않는다. 두 프로세스가 같은 인스턴스로 출정과 접속을 받게 된다
            StateRow? registered = await connection.QuerySingleOrDefaultAsync<StateRow>(new CommandDefinition(
                """
                INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state,
                    heartbeat_at, boot_id, registered_at)
                VALUES (@InstanceId, @Role, @Address, @Port, @BuildId, @ContentVersion, 'Idle', clock_timestamp(),
                    @BootId, clock_timestamp())
                ON CONFLICT (instance_id) DO UPDATE SET address = EXCLUDED.address, port = EXCLUDED.port,
                    build_id = EXCLUDED.build_id, content_version = EXCLUDED.content_version, state = 'Idle',
                    heartbeat_at = EXCLUDED.heartbeat_at, boot_id = EXCLUDED.boot_id, registered_at = EXCLUDED.registered_at
                    WHERE server_instances.role = EXCLUDED.role
                        AND server_instances.boot_id IS DISTINCT FROM EXCLUDED.boot_id
                        AND NOT
                """ + LiveBootCondition("server_instances") + """

                RETURNING state AS "State", clock_timestamp() AS "ServerTime";
                """, parameters, cancellationToken: cancellationToken));
            if (registered != null)
            {
                return new ServerPoolOutcome(EServerPoolStatus.Accepted, registered.State, ToUtc(registered.ServerTime));
            }

            InstanceRow? existing = await ReadInstanceAsync(connection, registration.InstanceId, cancellationToken);
            if (existing == null)
            {
                throw new InvalidOperationException("Server registration conflicted without an existing row.");
            }
            if (existing.Role != registration.Role)
            {
                return ToOutcome(EServerPoolStatus.RoleMismatch, existing);
            }
            if (existing.BootId != registration.BootId)
            {
                return ToOutcome(EServerPoolStatus.OtherBootAlive, existing);
            }

            // 같은 bootId 재전송: 응답을 잃은 등록의 재시도다. Offline이 아니면 heartbeat와 같다
            return await ChangeOwnedAsync(registration.InstanceId, registration.BootId,
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() WHERE instance_id = @InstanceId "
                + "AND boot_id = @BootId AND " + OWNED_CONDITION + " RETURNING state;", cancellationToken);
        }

        private async Task<ServerPoolOutcome> ChangeOwnedAsync(Guid instanceId, Guid bootId, string sql,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            string? state = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(sql,
                new { InstanceId = instanceId, BootId = bootId, HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout },
                cancellationToken: cancellationToken));
            InstanceRow? current = await ReadInstanceAsync(connection, instanceId, cancellationToken);
            if (current == null)
            {
                return new ServerPoolOutcome(EServerPoolStatus.InstanceNotFound, string.Empty,
                    ToUtc(await ReadServerTimeAsync(connection, cancellationToken)));
            }
            if (state != null)
            {
                return ToOutcome(EServerPoolStatus.Accepted, current);
            }
            if (current.BootId != bootId)
            {
                return ToOutcome(EServerPoolStatus.BootMismatch, current);
            }

            return ToOutcome(current.State == GameSessionStates.INSTANCE_OFFLINE
                ? EServerPoolStatus.Offline : EServerPoolStatus.HeartbeatExpired, current);
        }

        /// <summary> LiveBootCondition이 쓰는 매개변수를 더한다 </summary>
        public static void AddLiveBootParameters(DynamicParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            parameters.Add("HeartbeatTimeout", ServerPoolTimings.HeartbeatTimeout);
            parameters.Add("LiveBootDungeonRole", ContractCodes.ServerRole.DUNGEON);
        }

        // heartbeat 문장이 커밋된 뒤 따로 실행한다. 인스턴스 행을 잡은 채 임대 행을 기다리면 계정 -> 임대 -> 인스턴스 순서의
        // 임대 저장소와 반대 방향이 된다. 임대 행은 계정 순서로 잠가 같은 행을 지우는 정산과도 같은 방향을 지킨다.
        // 이미 만료된 임대는 되살리지 않는다
        private async Task ExtendAccountLeasesAsync(Guid instanceId, CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                WITH target AS (
                    SELECT account_id FROM account_leases
                    WHERE instance_id = @InstanceId AND expires_at > clock_timestamp()
                    ORDER BY account_id
                    FOR UPDATE
                )
                UPDATE account_leases AS lease
                SET expires_at = GREATEST(lease.expires_at, clock_timestamp() + @Duration)
                FROM target
                WHERE lease.account_id = target.account_id AND lease.instance_id = @InstanceId;
                """, new { InstanceId = instanceId, Duration = ServerPoolTimings.AccountLease },
                cancellationToken: cancellationToken));
        }

        private static Task<InstanceRow?> ReadInstanceAsync(NpgsqlConnection connection, Guid instanceId,
            CancellationToken cancellationToken)
        {
            return connection.QuerySingleOrDefaultAsync<InstanceRow>(new CommandDefinition(
                """
                SELECT role AS "Role", state AS "State", boot_id AS "BootId", clock_timestamp() AS "ServerTime"
                FROM server_instances WHERE instance_id = @InstanceId;
                """, new { InstanceId = instanceId }, cancellationToken: cancellationToken));
        }

        private static Task<DateTime> ReadServerTimeAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            return connection.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT clock_timestamp();",
                cancellationToken: cancellationToken));
        }

        private static ServerPoolOutcome ToOutcome(EServerPoolStatus status, InstanceRow row)
        {
            return new ServerPoolOutcome(status, row.State, ToUtc(row.ServerTime));
        }

        private static DateTimeOffset ToUtc(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        private static void ValidateIds(Guid instanceId, Guid bootId)
        {
            if ((instanceId == Guid.Empty) || (bootId == Guid.Empty))
            {
                throw new ArgumentException("Instance and boot IDs are required.");
            }
        }

        private sealed class StateRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public string   State      { get; set; } = string.Empty;
            public DateTime ServerTime { get; set; }
        }

        private sealed class InstanceRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public int      Role       { get; set; }
            public string   State      { get; set; } = string.Empty;
            public Guid?    BootId     { get; set; }
            public DateTime ServerTime { get; set; }
        }
    }
}
