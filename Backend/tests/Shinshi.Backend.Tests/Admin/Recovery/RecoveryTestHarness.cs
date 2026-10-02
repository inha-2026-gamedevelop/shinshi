// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Sessions;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 실제 원장 쓰기의 정산 저장소와 감사 실행기로 만든 복구 서비스, 부트스트랩한 운영자. 생존 판정 보류는
    /// 테스트가 직접 돌리는 시계로 열고 닫는다(기본은 기동 유예가 지난 열린 상태) </summary>
    internal sealed class RecoveryTestHarness
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string REASON = "stuck after dungeon crash";

        /****************************************
        *              Properties
        ****************************************/

        public AdminIdentity                   Admin      { get; }
        public PostgresSettlementRepository    Settlement { get; }
        public AdminSessionRecoveryService     Service    { get; }
        public PostgresAdminRecoveryRepository Recovery   { get; }
        public ManualTimeProvider              Clock      { get; }
        public RecoveryLivenessGate            Liveness   { get; }

        /****************************************
        *                Methods
        ****************************************/

        private RecoveryTestHarness(AdminIdentity admin, PostgresSettlementRepository settlement,
            AdminSessionRecoveryService service, PostgresAdminRecoveryRepository recovery, ManualTimeProvider clock,
            RecoveryLivenessGate liveness)
        {
            Admin      = admin;
            Settlement = settlement;
            Service    = service;
            Recovery   = recovery;
            Clock      = clock;
            Liveness   = liveness;
        }

        /// <summary> operationAuditWriter가 있으면 어드민 작업 트랜잭션 안의 감사 쓰기만 그 대역으로 바꾼다.
        /// 의도 감사 행(자체 트랜잭션)은 항상 실제 쓰기로 남긴다 </summary>
        public static async Task<RecoveryTestHarness> CreateAsync(SessionTestEnvironment environment,
            bool livenessDeferred = false, IAdminAuditWriter? operationAuditWriter = null)
        {
            AdminIdentity admin = await AdminLogTestSupport.BootstrapOwnerAsync(environment.DataSource);
            return Create(environment, admin, livenessDeferred, operationAuditWriter);
        }

        /// <summary> 같은 운영자와 DB로 감사 쓰기만 바꾼 복구 서비스를 만든다. 2단계 실패 뒤 정상 재전송을 흉내 낼 때 쓴다 </summary>
        public RecoveryTestHarness WithOperationAuditWriter(SessionTestEnvironment environment,
            IAdminAuditWriter? operationAuditWriter)
        {
            return Create(environment, Admin, Liveness.IsDeferred(), operationAuditWriter);
        }

        public Task<AdminSessionRecoveryOutcome> RecoverAsync(Guid sessionId, Guid operationId, string action)
        {
            return Service.RecoverAsync(Admin, null, sessionId,
                new AdminSessionRecoveryRequest(operationId.ToString("D"), REASON, action));
        }

        public void OpenLiveness()
        {
            Clock.Now = Clock.Now + ServerPoolTimings.StartupRecoveryGrace + TimeSpan.FromSeconds(1);
        }

        private static RecoveryTestHarness Create(SessionTestEnvironment environment, AdminIdentity admin,
            bool livenessDeferred, IAdminAuditWriter? operationAuditWriter)
        {
            PostgresSettlementRepository settlement = new PostgresSettlementRepository(environment.DataSource,
                environment.Executor, new LedgerWriter(new PostgresEconomyLedgerWriter()));
            PostgresAdminAuditWriter audit = new PostgresAdminAuditWriter(environment.DataSource);
            AdminOperationExecutor operations = new AdminOperationExecutor(environment.Executor,
                operationAuditWriter ?? audit, new PostgresAdminOperationClaimRepository(environment.DataSource));
            PostgresAdminRecoveryRepository recovery = new PostgresAdminRecoveryRepository(environment.DataSource);
            ManualTimeProvider clock = new ManualTimeProvider();
            RecoveryLivenessGate liveness = new RecoveryLivenessGate(clock);
            RecoveryTestHarness harness = new RecoveryTestHarness(admin, settlement,
                new AdminSessionRecoveryService(recovery, settlement, operations, audit, liveness), recovery, clock,
                liveness);
            if (!livenessDeferred)
            {
                harness.OpenLiveness();
            }

            return harness;
        }
    }

    /// <summary> 어드민 작업 트랜잭션 안의 감사 쓰기를 항상 실패시키는 대역. 복구 작업은 확정됐는데 어드민 작업이 실패하는 2단계 실패를 만든다 </summary>
    internal sealed class FailingOperationAuditWriter : IAdminAuditWriter
    {
        /****************************************
        *                Methods
        ****************************************/

        public Task AppendAsync(OperationExecutionContext context, AdminAuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated audit failure in the admin operation transaction.");
        }

        public Task AppendStandaloneAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Standalone audit is written by the real writer in this test.");
        }
    }
}
