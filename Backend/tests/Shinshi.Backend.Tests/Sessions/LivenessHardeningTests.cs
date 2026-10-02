// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Auth;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 3차 공격자 관점 검토의 생존 판정, 복구 보류, 배정, 다른 boot, 복구 격리, 정산 상한 결함을 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class LivenessHardeningTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS          = 3;
        private const long POTIONS          = 4;
        private const long POTIONS_RESERVED = 1;
        private const int  TEST_PORT        = 7791;
        private const int  LEASE_MINUTES    = 15;
        private const int  SHORT_GAP        = 30;
        private const int  STALE_GAP        = 20;
        private const int  CANCEL_GAP       = ServerPoolTimings.SERVICE_CANCEL_HEARTBEAT_SECONDS + 30;
        private const int  OUTAGE_SECONDS   = 40;
        private const int  RECOVERY_BATCH   = 8;
        private const long DAILY_MARGIN     = 15;
        private const long OVER_MARGIN      = DAILY_MARGIN + 5;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기동 유예가 끝난 뒤 DB가 40초 끊겼다 돌아와도 같은 유예 시간 동안 소실 복구를 하지 않고, 그 뒤에 장애 취소한다 </summary>
        [PostgresFact]
        public async Task Maintenance_DatabaseOutage_DefersRecoveryForGraceAfterReconnect()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            await StaleAsync(environment, session.InstanceId, CANCEL_GAP);
            ManualTimeProvider clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            FlakyMaintenanceWork work = new FlakyMaintenanceWork(LeaseLivenessTests.CreateWork(environment));
            GameSessionMaintenanceService service = new GameSessionMaintenanceService(work,
                NullLogger<GameSessionMaintenanceService>.Instance, clock);

            clock.Advance(ServerPoolTimings.StartupRecoveryGrace + TimeSpan.FromSeconds(1));
            work.Unreachable = true;
            for (int elapsed = 0; elapsed <= OUTAGE_SECONDS; elapsed += ServerPoolTimings.HEARTBEAT_INTERVAL_SECONDS)
            {
                await service.RunOnceAsync(CancellationToken.None);
                clock.Advance(TimeSpan.FromSeconds(ServerPoolTimings.HEARTBEAT_INTERVAL_SECONDS));
            }
            work.Unreachable = false;
            await service.RunOnceAsync(CancellationToken.None);
            string afterReconnect = await StateAsync(environment, session.SessionId);
            clock.Advance(ServerPoolTimings.StartupRecoveryGrace - TimeSpan.FromSeconds(ServerPoolTimings.HEARTBEAT_INTERVAL_SECONDS
                + 1));
            await service.RunOnceAsync(CancellationToken.None);
            string beforeGraceEnds = await StateAsync(environment, session.SessionId);
            clock.Advance(TimeSpan.FromSeconds(ServerPoolTimings.HEARTBEAT_INTERVAL_SECONDS + 1));
            await service.RunOnceAsync(CancellationToken.None);

            Assert.True(work.FailedCalls > 0);
            Assert.Equal(GameSessionStates.RUNNING, afterReconnect);
            Assert.Equal(GameSessionStates.RUNNING, beforeGraceEnds);
            Assert.Equal(GameSessionStates.CLOSED, await StateAsync(environment, session.SessionId));
        }

        /// <summary> 진행 중 출정은 heartbeat 공백이 15초를 넘어도 60초 전에는 장애 취소하지 않는다 </summary>
        [PostgresFact]
        public async Task Recovery_RunningGapBelowCancelThreshold_IsNotCancelled()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);

            await StaleAsync(environment, session.InstanceId, SHORT_GAP);
            SessionRecoveryResult early = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            OperationResult? earlyCancel = await environment.Settlement.ServiceCancelAsync(session.SessionId);
            long storedEarly = await environment.ScalarAsync(
                "SELECT count(*) FROM operations WHERE operation_kind = @Kind;",
                new { Kind = PostgresSettlementRepository.SERVICE_CANCEL_KIND });
            await StaleAsync(environment, session.InstanceId, CANCEL_GAP);
            SessionRecoveryResult late = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);

            Assert.Equal(0, early.Cancelled);
            Assert.Equal(ContractCodes.RejectReason.WRONG_PHASE, earlyCancel!.ReasonCode);
            Assert.Equal(0L, storedEarly);
            Assert.Equal(1, late.Cancelled);
        }

        /// <summary> 풀 전체(마을 포함)의 heartbeat가 동시에 끊기면 API 쪽 단절로 보고 장애 취소하지 않는다 </summary>
        [PostgresFact]
        public async Task Recovery_WholePoolSilent_DoesNotCancelUntilAnyServerReturns()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - make_interval(secs => @Gap);",
                new { Gap = CANCEL_GAP });

            SessionRecoveryResult silent = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            ServerPoolOutcome townBeat = await environment.Pool.HeartbeatAsync(environment.TownId,
                await BootOfAsync(environment, environment.TownId));
            SessionRecoveryResult recovered = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);

            Assert.Equal(0, silent.Cancelled);
            Assert.Empty(silent.Failures);
            Assert.Equal(EServerPoolStatus.Accepted, townBeat.Status);
            Assert.Equal(1, recovered.Cancelled);
            Assert.Equal(GameSessionStates.CLOSED, await StateAsync(environment, session.SessionId));
        }

        /// <summary> 배정 boot가 그대로인데 heartbeat만 늦으면 /events와 /start는 거절을 저장하지 않고 일시 예외로 끝나며,
        /// heartbeat가 돌아오면 같은 operationId로 확정된다 </summary>
        [PostgresFact]
        public async Task Effects_StaleHeartbeatSameBoot_ThrowTransientWithoutStoring()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView reserved = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid boot = await BootOfAsync(environment, reserved.InstanceId);
            Guid[] ready = reserved.Participants.Select(participant => participant.ParticipantId).ToArray();
            Guid startId = Guid.NewGuid();
            await StaleAsync(environment, reserved.InstanceId, STALE_GAP);

            TransientRejectionException startFailure = await Assert.ThrowsAsync<TransientRejectionException>(() =>
                environment.Sessions.StartAsync(reserved.InstanceId, reserved.SessionId, startId, ready));
            long storedStart = await CountOperationAsync(environment, startId);
            await environment.Pool.HeartbeatAsync(reserved.InstanceId, boot);
            OperationResult started = await environment.Sessions.StartAsync(reserved.InstanceId, reserved.SessionId,
                startId, ready);

            Guid eventsId = Guid.NewGuid();
            SessionEventInput[] batch = { SessionEventTests.Loot(1) };
            await StaleAsync(environment, reserved.InstanceId, STALE_GAP);
            TransientRejectionException eventsFailure = await Assert.ThrowsAsync<TransientRejectionException>(() =>
                environment.Sessions.AppendEventsAsync(reserved.InstanceId, reserved.SessionId, eventsId, batch));
            long storedEvents = await CountOperationAsync(environment, eventsId);
            await environment.Pool.TouchAsync(reserved.InstanceId, boot);
            OperationResult appended = await environment.Sessions.AppendEventsAsync(reserved.InstanceId,
                reserved.SessionId, eventsId, batch);

            Assert.Equal(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, startFailure.ReasonCode);
            Assert.Equal(ServerPoolTimings.LIVENESS_RETRY_AFTER_SECONDS, startFailure.RetryAfterSeconds);
            Assert.Equal(0L, storedStart);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, started.Status);
            Assert.Equal(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, eventsFailure.ReasonCode);
            Assert.Equal(0L, storedEvents);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, appended.Status);
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM session_events;"));
        }

        /// <summary> 정산을 마친 던전은 Idle이 아니라 Draining이 되어 같은 boot에 다시 배정하지 않고, 배정은 먼저 등록한 던전부터 고른다 </summary>
        [PostgresFact]
        public async Task Settle_ReturnsDraining_AndAllocationPrefersEarliestRegistered()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(2);
            Guid first = environment.DungeonIds[0];
            Guid second = environment.DungeonIds[1];
            await environment.ExecuteAsync(
                """
                UPDATE server_instances SET registered_at = clock_timestamp() - interval '1 hour' WHERE instance_id = @First;
                UPDATE server_instances SET heartbeat_at = clock_timestamp() + interval '1 second' WHERE instance_id = @Second;
                """, new { First = first, Second = second });
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer other = await environment.CreatePlayerAsync(TICKETS, POTIONS);

            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid settlementId = Guid.NewGuid();
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, settlementId,
                    new[] { SessionEventTests.Grant(session.Participants[0].ParticipantId, 1) })
            });
            OperationResult settled = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);
            OperationResult finished = await environment.FinishAuctionAsync(session);
            ReservedSessionView next = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, new[] { other },
                POTIONS_RESERVED);

            Assert.Equal(first, session.InstanceId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, finished.Status);
            Assert.Equal(GameSessionStates.INSTANCE_DRAINING, await environment.TextAsync(
                "SELECT state FROM server_instances WHERE instance_id = @Id;", new { Id = first }));
            Assert.Equal(second, next.InstanceId);
        }

        /// <summary> 같은 인스턴스의 다른 boot가 살아 있으면 새 boot 임대는 일시 거절, 끊긴 뒤에는 발급되고,
        /// 대체된 이전 boot는 다시 임대를 받지 못하고 SESSION_TERMINAL로 끝난다. 출정을 마친 Draining 던전은 바로 넘겨받는다 </summary>
        [PostgresFact]
        public async Task IssueServerLease_OtherBootAlive_TransientThenSupersededTerminal()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(0);
            PostgresAuthRepository auth = new PostgresAuthRepository(environment.DataSource, AuthTokenHasherTests.CreateOptions());
            Guid dungeonId = Guid.NewGuid();
            Guid firstBoot = Guid.NewGuid();
            Guid secondBoot = Guid.NewGuid();
            Assert.True((await LeaseAsync(auth, dungeonId, firstBoot)).IsSuccess);
            Assert.Equal(EServerPoolStatus.Accepted, (await RegisterAsync(environment, dungeonId, firstBoot)).Status);

            AuthValidationOutcome<IssuedAuthToken> whileAlive = await LeaseAsync(auth, dungeonId, secondBoot);
            await StaleAsync(environment, dungeonId, STALE_GAP);
            AuthValidationOutcome<IssuedAuthToken> afterGap = await LeaseAsync(auth, dungeonId, secondBoot);
            ServerPoolOutcome registered = await RegisterAsync(environment, dungeonId, secondBoot);
            AuthValidationOutcome<IssuedAuthToken> superseded = await LeaseAsync(auth, dungeonId, firstBoot);
            await environment.ExecuteAsync("UPDATE server_instances SET state = 'Draining' WHERE instance_id = @Id;",
                new { Id = dungeonId });
            AuthValidationOutcome<IssuedAuthToken> drainedTakeover = await LeaseAsync(auth, dungeonId, Guid.NewGuid());

            Assert.Equal(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, whileAlive.ReasonCode);
            Assert.True(afterGap.IsSuccess);
            Assert.Equal(EServerPoolStatus.Accepted, registered.Status);
            Assert.Equal(ContractCodes.RejectReason.SESSION_TERMINAL, superseded.ReasonCode);
            Assert.True(drainedTakeover.IsSuccess);
        }

        /// <summary> 잠금 제한 초과처럼 재시도 가능한 예외는 격리 실패 횟수에 넣지 않고, 잠금이 풀리면 같은 세션을 복구한다 </summary>
        [PostgresFact]
        public async Task Recovery_LockTimeout_IsNotCountedAndRecoversLater()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            await StaleAsync(environment, session.InstanceId, CANCEL_GAP);

            SessionRecoveryResult blocked;
            await using (NpgsqlConnection holder = await environment.DataSource.OpenConnectionAsync())
            await using (NpgsqlTransaction hold = await holder.BeginTransactionAsync())
            {
                await holder.ExecuteAsync("SELECT session_id FROM game_sessions WHERE session_id = @Id FOR UPDATE;",
                    new { Id = session.SessionId }, hold);
                blocked = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
                await hold.RollbackAsync();
            }
            SessionRecoveryResult recovered = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);

            SessionRecoveryFailure failure = Assert.Single(blocked.Failures);
            Assert.True(failure.Retryable);
            Assert.False(failure.Quarantined);
            Assert.Equal(0, blocked.Cancelled);
            Assert.Equal(1, recovered.Cancelled);
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT recovery_failures FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
        }

        /// <summary> 업무 원인의 복구 실패는 횟수를 올리고 지수 백오프로 다음 시도를 미룬다 </summary>
        [PostgresFact]
        public async Task Recovery_CountedFailure_BacksOffNextAttempt()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid settlementId = Guid.NewGuid();
            string payload = "{\"endReason\":0,\"settlementOperationId\":\"" + settlementId.ToString("D")
                + "\",\"grants\":[{\"participantId\":\"" + Guid.NewGuid().ToString("D") + "\",\"essence\":1,\"items\":[]}]}";
            await environment.ExecuteAsync(
                """
                INSERT INTO session_events (session_id, event_seq, event_id, event_kind, payload, operation_id)
                VALUES (@SessionId, 1, gen_random_uuid(), @Ended, CAST(@Payload AS jsonb), gen_random_uuid());
                UPDATE game_sessions SET state = 'Settling', last_event_seq = 1, ended_event_seq = 1,
                    settlement_operation_id = @SettlementId, end_reason = 0,
                    settling_at = clock_timestamp() - interval '2 minutes'
                WHERE session_id = @SessionId;
                """, new { SessionId = session.SessionId, Ended = GameSessionCodes.EventKind.SESSION_ENDED,
                    Payload = payload, SettlementId = settlementId });

            SessionRecoveryResult first = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            SessionRecoveryResult immediate = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);

            Assert.Equal(1, Assert.Single(first.Failures).Failures);
            Assert.Empty(immediate.Failures);
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM game_sessions WHERE session_id = @Id AND next_recovery_at > clock_timestamp();",
                new { Id = session.SessionId }));
        }

        /// <summary> 던전으로 이관된 계정 임대도 던전 heartbeat가 연장하고, 정산이 끝나면 그 던전의 참가자 임대를 같은 트랜잭션에서 푼다.
        /// 만료된 임대는 heartbeat로 되살리지 않는다 </summary>
        [PostgresFact]
        public async Task DungeonLease_HeartbeatExtends_AndSettlementReleases()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer expired = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS,
                new[] { player, expired }, POTIONS_RESERVED);
            Guid boot = await BootOfAsync(environment, session.InstanceId);
            foreach (ReservedParticipantView participant in session.Participants)
            {
                LeaseTransferOutcome moved = await environment.Reservations.TransferParticipantLeaseAsync(session.InstanceId,
                    session.SessionId, participant.ParticipantId, Guid.NewGuid());
                Assert.Equal(ContractCodes.RejectReason.NONE, moved.ReasonCode);
            }
            await environment.ExecuteAsync(
                """
                UPDATE account_leases SET expires_at = clock_timestamp() + interval '1 second' WHERE account_id = @Alive;
                UPDATE account_leases SET expires_at = clock_timestamp() - interval '1 second' WHERE account_id = @Expired;
                """, new { Alive = player.AccountId, Expired = expired.AccountId });

            ServerPoolOutcome beat = await environment.Pool.HeartbeatAsync(session.InstanceId, boot);
            long extended = await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE account_id = @Id AND expires_at > clock_timestamp() + interval '10 seconds';",
                new { Id = player.AccountId });
            long revived = await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE account_id = @Id AND expires_at > clock_timestamp();",
                new { Id = expired.AccountId });
            OperationResult started = await environment.Sessions.StartAsync(session.InstanceId, session.SessionId,
                Guid.NewGuid(), session.Participants.Select(row => row.ParticipantId).ToArray());
            Guid settlementId = Guid.NewGuid();
            await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, settlementId, session.Participants
                    .Select(row => SessionEventTests.Grant(row.ParticipantId, 1)))
            });
            OperationResult settled = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);

            // 경매 단계 동안은 임대를 유지해 같은 계정의 마을 접속을 막고, finish가 푼다
            long heldDuringAuction = await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE instance_id = @Id;", new { Id = session.InstanceId });
            OperationResult finished = await environment.FinishAuctionAsync(session);

            Assert.Equal(EServerPoolStatus.Accepted, beat.Status);
            Assert.True(heldDuringAuction > 0);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, finished.Status);
            Assert.Equal(1L, extended);
            Assert.Equal(0L, revived);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, started.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE instance_id = @Id;", new { Id = session.InstanceId }));
        }

        /// <summary> 정산 아이템 귀속은 허용 목록(계정 귀속)만 받는다 </summary>
        [Fact]
        public void Validator_SettlementBindingOutsideAllowList_Rejected()
        {
            Guid participant = Guid.NewGuid();

            foreach (string binding in new[] { "account", "Character", "Tradable", string.Empty })
            {
                Assert.False(SessionEventValidator.IsValid(new[] { EndedWith(participant, binding) }));
            }
            Assert.True(SessionEventValidator.IsValid(new[]
            {
                EndedWith(participant, SettlementSanityLimits.ACCOUNT_BINDING_KIND)
            }));
        }

        /// <summary> 같은 인스턴스의 24시간 종료 정산안 정기 합계가 상한을 넘는 종료 배치는 효과 없이 거절한다 </summary>
        [PostgresFact]
        public async Task Append_InstanceDailyEssenceCap_RejectsEndedBatch()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView earlier = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid earlierSettlement = Guid.NewGuid();
            await SessionEventTests.AppendAsync(environment, earlier, new[]
            {
                SessionEventTests.Ended(1, earlierSettlement,
                    new[] { SessionEventTests.Grant(earlier.Participants[0].ParticipantId, 1) })
            });
            await environment.Settlement.SettleAsync(earlier.InstanceId, earlier.SessionId, earlierSettlement);
            await environment.FinishAuctionAsync(earlier);
            await environment.ExecuteAsync("UPDATE game_sessions SET ended_essence = @Essence WHERE session_id = @Id;",
                new { Id = earlier.SessionId, Essence = SettlementSanityLimits.MAX_INSTANCE_DAILY_ESSENCE - DAILY_MARGIN });
            await environment.RebootDungeonAsync(earlier.InstanceId);

            // 정산이 캐릭터 revision을 올렸으므로 다음 출정은 새 참가자로 한다
            TestPlayer next = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { next },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;

            OperationResult over = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, Guid.NewGuid(), new[] { SessionEventTests.Grant(participant, OVER_MARGIN) })
            });
            OperationResult within = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, Guid.NewGuid(), new[] { SessionEventTests.Grant(participant, DAILY_MARGIN) })
            });

            Assert.Equal(earlier.InstanceId, session.InstanceId);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, over.Status);
            Assert.Equal(SettlementSanityLimits.REJECT_REASON, over.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, within.Status);
            Assert.Equal(DAILY_MARGIN, await environment.ScalarAsync(
                "SELECT ended_essence FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
        }

        private static SessionEventInput EndedWith(Guid participant, string binding)
        {
            return SessionEventTests.Ended(1, Guid.NewGuid(), new[]
            {
                SessionEventTests.Grant(participant, 1, new SettlementItemInput
                {
                    DefinitionId = "item_boss_material_common", BindingKind = binding, Quantity = 1
                })
            });
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

        private static Task StaleAsync(SessionTestEnvironment environment, Guid instanceId, int gapSeconds)
        {
            return environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - make_interval(secs => @Gap) WHERE instance_id = @Id;",
                new { Id = instanceId, Gap = gapSeconds });
        }

        private static Task<string> StateAsync(SessionTestEnvironment environment, Guid sessionId)
        {
            return environment.TextAsync("SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = sessionId });
        }

        private static async Task<Guid> BootOfAsync(SessionTestEnvironment environment, Guid instanceId)
        {
            return Guid.Parse(await environment.TextAsync(
                "SELECT boot_id::text FROM server_instances WHERE instance_id = @Id;", new { Id = instanceId }));
        }

        private static Task<long> CountOperationAsync(SessionTestEnvironment environment, Guid operationId)
        {
            return environment.ScalarAsync("SELECT count(*) FROM operations WHERE operation_id = @Id;",
                new { Id = operationId });
        }

        /// <summary> 실제 작업을 감싸 DB 단절을 흉내 낸다. 단절 중에는 연결 실패와 같은 NpgsqlException을 던진다 </summary>
        private sealed class FlakyMaintenanceWork : IGameSessionMaintenanceWork
        {
            private readonly IGameSessionMaintenanceWork _inner;

            public bool Unreachable { get; set; }
            public int  FailedCalls { get; private set; }

            public FlakyMaintenanceWork(IGameSessionMaintenanceWork inner)
            {
                _inner = inner;
            }

            public Task<int> ReleaseExpiredAsync(int maxSessions, CancellationToken cancellationToken)
            {
                ThrowIfUnreachable();
                return _inner.ReleaseExpiredAsync(maxSessions, cancellationToken);
            }

            public Task<SessionRecoveryResult> RecoverLostSessionsAsync(int maxSessions, CancellationToken cancellationToken)
            {
                ThrowIfUnreachable();
                return _inner.RecoverLostSessionsAsync(maxSessions, cancellationToken);
            }

            public Task<int> PurgeExpiredJoinTokensAsync(int maxRows, CancellationToken cancellationToken)
            {
                ThrowIfUnreachable();
                return _inner.PurgeExpiredJoinTokensAsync(maxRows, cancellationToken);
            }

            private void ThrowIfUnreachable()
            {
                if (!Unreachable)
                {
                    return;
                }

                ++FailedCalls;
                throw new NpgsqlException("Simulated database outage.");
            }
        }
    }
}
