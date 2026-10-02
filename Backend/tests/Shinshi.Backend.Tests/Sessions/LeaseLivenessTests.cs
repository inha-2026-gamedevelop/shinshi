// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 서버 임대 인증과 생존 판정의 분리, 장애 취소의 Offline 확정, API 기동 유예를 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class LeaseLivenessTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS          = 2;
        private const long POTIONS          = 3;
        private const long POTIONS_RESERVED = 1;
        private const int  TEST_PORT        = 7790;
        private const int  LEASE_MINUTES    = 15;
        private const int  GAP_SECONDS      = 20;

        // 진행 중 출정의 장애 취소 임계(60초)를 넘긴 heartbeat 공백
        private const int  CANCEL_GAP_SECONDS = ServerPoolTimings.SERVICE_CANCEL_HEARTBEAT_SECONDS + GAP_SECONDS;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 첫 임대가 인스턴스 행을 만들고, 20초 heartbeat 공백 동안 임대는 유효하며 배정만 멈췄다가 같은 bootId heartbeat로 복귀한다 </summary>
        [PostgresFact]
        public async Task Heartbeat_TwentySecondGap_SameBootRevivesWithoutNewLease()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(0);
            PostgresAuthRepository auth = new PostgresAuthRepository(environment.DataSource, AuthTokenHasherTests.CreateOptions());
            Guid dungeonId = Guid.NewGuid();
            Guid boot = Guid.NewGuid();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);

            AuthValidationOutcome<IssuedAuthToken> lease = await LeaseAsync(auth, dungeonId, boot);
            string unregistered = await environment.TextAsync(
                "SELECT state FROM server_instances WHERE instance_id = @Id AND boot_id IS NULL;", new { Id = dungeonId });
            Assert.True((await auth.ValidateServerLeaseAsync(lease.Identity!.Token)).IsSuccess);
            Assert.Equal(EServerPoolStatus.Accepted, (await RegisterAsync(environment, dungeonId, boot)).Status);
            await MakeHeartbeatStaleAsync(environment, dungeonId);

            AuthValidationOutcome<ServerAuthIdentity> duringGap = await auth.ValidateServerLeaseAsync(lease.Identity.Token);
            ReservationOutcome noCapacity = await ReserveAsync(environment, player);
            ServerPoolOutcome revived = await environment.Pool.HeartbeatAsync(dungeonId, boot);
            ReservationOutcome reserved = await ReserveAsync(environment, player);

            Assert.Equal(GameSessionStates.INSTANCE_OFFLINE, unregistered);
            Assert.True(duringGap.IsSuccess);
            Assert.Equal(boot, duringGap.Identity!.BootId);
            Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY, noCapacity.Result.ReasonCode);
            Assert.Equal(EServerPoolStatus.Accepted, revived.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reserved.Result.Status);
        }

        /// <summary> 장애 취소가 Offline을 쓴 boot는 heartbeat, 임대 인증, 같은 boot 재임대 모두 거절되고 새 bootId만 다시 등록된다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_WritesOffline_SameBootCannotReturnButNewBootCan()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(0);
            PostgresAuthRepository auth = new PostgresAuthRepository(environment.DataSource, AuthTokenHasherTests.CreateOptions());
            Guid dungeonId = Guid.NewGuid();
            Guid boot = Guid.NewGuid();
            AuthValidationOutcome<IssuedAuthToken> lease = await LeaseAsync(auth, dungeonId, boot);
            await RegisterAsync(environment, dungeonId, boot);
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            await MakeHeartbeatStaleAsync(environment, dungeonId, CANCEL_GAP_SECONDS);

            SessionRecoveryResult recovered = await environment.Settlement.RecoverLostSessionsAsync(1);
            ServerPoolOutcome lateBeat = await environment.Pool.HeartbeatAsync(dungeonId, boot);
            AuthValidationOutcome<ServerAuthIdentity> oldLease = await auth.ValidateServerLeaseAsync(lease.Identity!.Token);
            AuthValidationOutcome<IssuedAuthToken> sameBoot = await LeaseAsync(auth, dungeonId, boot);
            Guid newBoot = Guid.NewGuid();
            AuthValidationOutcome<IssuedAuthToken> fresh = await LeaseAsync(auth, dungeonId, newBoot);
            ServerPoolOutcome registered = await RegisterAsync(environment, dungeonId, newBoot);

            Assert.Equal(1, recovered.Cancelled);
            Assert.Equal(session.InstanceId, dungeonId);
            Assert.Equal(EServerPoolStatus.Offline, lateBeat.Status);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, oldLease.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_TERMINAL, sameBoot.ReasonCode);
            Assert.True(fresh.IsSuccess);
            Assert.True((await auth.ValidateServerLeaseAsync(fresh.Identity!.Token)).IsSuccess);
            Assert.Equal(EServerPoolStatus.Accepted, registered.Status);
            Assert.Equal(GameSessionStates.INSTANCE_IDLE, registered.State);
        }

        /// <summary> 서비스 자격 설정의 역할과 다른 기존 행에는 임대를 발급하지 않는다 </summary>
        [PostgresFact]
        public async Task IssueServerLease_ExistingRowWithOtherRole_RejectsWithoutLease()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(0);
            PostgresAuthRepository auth = new PostgresAuthRepository(environment.DataSource, AuthTokenHasherTests.CreateOptions());

            AuthValidationOutcome<IssuedAuthToken> wrongRole = await auth.IssueServerLeaseAsync(Guid.NewGuid(),
                environment.TownId, ContractCodes.ServerRole.DUNGEON, Guid.NewGuid(), TimeSpan.FromMinutes(LEASE_MINUTES));

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, wrongRole.ReasonCode);
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM auth_server_leases WHERE instance_id = @Id;", new { Id = environment.TownId }));
        }

        /// <summary> API 기동 직후에는 오래된 heartbeat를 소실로 보지 않고, 유예가 지난 뒤에 장애 취소한다 </summary>
        [PostgresFact]
        public async Task Maintenance_StartupGrace_DefersRecoveryUntilGraceElapses()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            await MakeHeartbeatStaleAsync(environment, session.InstanceId, CANCEL_GAP_SECONDS);
            ManualTimeProvider clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            GameSessionMaintenanceService service = new GameSessionMaintenanceService(CreateWork(environment),
                NullLogger<GameSessionMaintenanceService>.Instance, clock);

            await service.RunOnceAsync(CancellationToken.None);
            string duringGrace = await ReadSessionStateAsync(environment, session.SessionId);
            clock.Advance(ServerPoolTimings.StartupRecoveryGrace - TimeSpan.FromSeconds(1));
            await service.RunOnceAsync(CancellationToken.None);
            string beforeEnd = await ReadSessionStateAsync(environment, session.SessionId);
            clock.Advance(TimeSpan.FromSeconds(1));
            await service.RunOnceAsync(CancellationToken.None);
            string afterGrace = await ReadSessionStateAsync(environment, session.SessionId);

            Assert.Equal(GameSessionStates.RUNNING, duringGrace);
            Assert.Equal(GameSessionStates.RUNNING, beforeEnd);
            Assert.Equal(GameSessionStates.CLOSED, afterGrace);
        }

        /// <summary> 같은 newLeaseId 재시도 단축 경로도 만료된 계정 임대를 성공으로 돌려주지 않는다 </summary>
        [PostgresFact]
        public async Task TransferLease_SameLeaseRetryAfterExpiry_ReturnsTokenExpired()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participantId = session.Participants[0].ParticipantId;
            Guid newLeaseId = Guid.NewGuid();

            LeaseTransferOutcome moved = await environment.Reservations.TransferParticipantLeaseAsync(session.InstanceId,
                session.SessionId, participantId, newLeaseId);
            await environment.ExecuteAsync(
                "UPDATE account_leases SET expires_at = clock_timestamp() - interval '1 second' WHERE account_id = @Id;",
                new { Id = player.AccountId });
            LeaseTransferOutcome retried = await environment.Reservations.TransferParticipantLeaseAsync(session.InstanceId,
                session.SessionId, participantId, newLeaseId);

            Assert.Equal(ContractCodes.RejectReason.NONE, moved.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_EXPIRED, retried.ReasonCode);
        }

        /// <summary> 추가 전용 세션 이벤트는 TRUNCATE도 DB에서 거절한다 </summary>
        [PostgresFact]
        public async Task SessionEvents_Truncate_IsRejectedByDatabase()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();

            await Assert.ThrowsAsync<PostgresException>(() => environment.ExecuteAsync("TRUNCATE session_events;"));
        }

        private static Task<AuthValidationOutcome<IssuedAuthToken>> LeaseAsync(PostgresAuthRepository auth,
            Guid instanceId, Guid bootId)
        {
            return auth.IssueServerLeaseAsync(Guid.NewGuid(), instanceId, ContractCodes.ServerRole.DUNGEON, bootId,
                TimeSpan.FromMinutes(LEASE_MINUTES));
        }

        private static Task<ServerPoolOutcome> RegisterAsync(SessionTestEnvironment environment, Guid instanceId,
            Guid bootId)
        {
            return environment.Pool.RegisterAsync(new ServerRegistration(instanceId, bootId,
                ContractCodes.ServerRole.DUNGEON, "127.0.0.1", TEST_PORT, "test-build",
                SessionTestEnvironment.CONTENT_VERSION));
        }

        private static Task<ReservationOutcome> ReserveAsync(SessionTestEnvironment environment, TestPlayer player)
        {
            return environment.Reservations.ReserveAsync(environment.CreateReservation(Guid.NewGuid(),
                GameSessionCodes.Mode.BOSS, new[] { player }, POTIONS_RESERVED));
        }

        private static Task MakeHeartbeatStaleAsync(SessionTestEnvironment environment, Guid instanceId,
            int gapSeconds = GAP_SECONDS)
        {
            return environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - make_interval(secs => @Gap) WHERE instance_id = @Id;",
                new { Id = instanceId, Gap = gapSeconds });
        }

        internal static GameSessionMaintenanceWork CreateWork(SessionTestEnvironment environment)
        {
            return new GameSessionMaintenanceWork(environment.Reservations, environment.Settlement,
                new PostgresAuthRepository(environment.DataSource, AuthTokenHasherTests.CreateOptions()));
        }

        private static Task<string> ReadSessionStateAsync(SessionTestEnvironment environment, Guid sessionId)
        {
            return environment.TextAsync("SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = sessionId });
        }
    }

    /// <summary> 테스트가 직접 앞으로 돌리는 시계 </summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        /****************************************
        *                Fields
        ****************************************/

        private DateTimeOffset _now;

        /****************************************
        *                Methods
        ****************************************/

        public ManualTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan delta)
        {
            _now = _now.Add(delta);
        }
    }
}
