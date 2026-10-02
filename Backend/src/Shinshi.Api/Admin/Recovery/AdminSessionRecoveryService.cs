// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Admin
{
    /// <summary> 운영자의 정산 재시도, 서비스 장애 취소, 격리 세션 강제 종료. 정산 재시도와 장애 취소의 실제 종결은 자동 복구와 같은
    /// PostgresSettlementRepository 공개 경로가 배정 서버를 actor로 한 자체 작업(자체 트랜잭션)으로 하고, 어드민 작업은 그 결과를
    /// 감사 행과 함께 따로 확정한다. 강제 종료는 운영자 작업 한 트랜잭션에서 종결, 원장, 감사를 함께 쓴다 </summary>
    public sealed class AdminSessionRecoveryService
    {
        // 두 작업이 한 트랜잭션이 될 수 없는 이유: 정산과 장애 취소는 actor(서버), operationId, 요청 본문이 자동 복구와 같아야
        // 늦게 온 /settlement나 다음 복구 주기와 겹쳐도 한 번만 적용된다. 그 작업은 자기 operations 행과 트랜잭션을 연다.
        // 그래서 순서를 "의도 감사 행 커밋 -> 복구 작업 확정 -> 어드민 작업과 감사 행 확정"으로 둔다. 마지막 단계가 실패해도
        // 의도 행이 운영자, 사유, operationId를 남겨 감사 없는 종결이 생기지 않는다.
        // 이미 저장된 어드민 operationId는 복구를 다시 부르지 않는다. 다시 부르면 처음에 거절된 어드민 작업의 재전송이
        // 나중에 실제 종결을 일으키고도 저장된 거절만 돌려줘 감사 없는 변경이 생긴다.
        // 반대로 복구는 확정됐는데 어드민 작업이 실패(503 등)한 뒤 같은 operationId로 다시 오면 세션은 이미 종결돼 있다.
        // 이때 거절하면 감사 없는 종결이 남으므로, 이 action이 만드는 종결 상태면 기존 경로를 다시 불러 저장된 결과를 받고
        // alreadyApplied로 감사한다. alreadyApplied는 세션 상태가 아니라 복구 작업(서버 actor, 고정 operationId)이 이번 호출 전에
        // 이미 저장돼 있었는지로 정한다. 상태로 정하면 이번 호출이 실제로 종결한 직후의 재시도와 구분하지 못한다.
        // 다른 경로로 종결된 세션(정산 재시도에 ServiceCancelled 등)은 INVALID_STATE다.
        // 저장 여부 검사와 서버 복구는 어드민 operations 행이 생기기 전이라, 그 전에 운영자 작업 키를 대상과 요청 해시에
        // 묶어 커밋하고(admin_operation_claims) 같은 키의 실행을 직렬화한다. 그러지 않으면 같은 운영자, 같은 operationId로
        // 서로 다른 세션을 동시에 복구하거나 어드민 단계가 실패한 뒤 다른 세션을 복구해 두 세션이 모두 종결되고
        // 한쪽만 IDEMPOTENCY_CONFLICT를 받는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresAdminRecoveryRepository _recovery;
        private readonly PostgresSettlementRepository _settlement;
        private readonly AdminOperationExecutor _operations;
        private readonly IAdminAuditWriter _auditWriter;
        private readonly RecoveryLivenessGate _liveness;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 소실 판정 보류 중인지. 복구 목록이 instanceLost를 내리는 데 쓴다 </summary>
        public bool IsLivenessDeferred => _liveness.IsDeferred();

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionRecoveryService(PostgresAdminRecoveryRepository recovery,
            PostgresSettlementRepository settlement, AdminOperationExecutor operations, IAdminAuditWriter auditWriter,
            RecoveryLivenessGate liveness)
        {
            ArgumentNullException.ThrowIfNull(recovery);
            ArgumentNullException.ThrowIfNull(settlement);
            ArgumentNullException.ThrowIfNull(operations);
            ArgumentNullException.ThrowIfNull(auditWriter);
            ArgumentNullException.ThrowIfNull(liveness);
            _recovery    = recovery;
            _settlement  = settlement;
            _operations  = operations;
            _auditWriter = auditWriter;
            _liveness    = liveness;
        }

        /// <summary> 세션이 없으면 NOT_FOUND, 상태가 맞지 않거나 기존 경로가 거절하면 INVALID_STATE로 어드민 작업을 거절한다.
        /// 같은 operationId가 다른 세션이나 다른 action에 이미 묶였으면 아무 효과 없이 IDEMPOTENCY_CONFLICT다.
        /// 생존 판정 보류 중의 새 장애 취소, 강제 종료는 저장하지 않는 TransientRejectionException(503)이다 </summary>
        public async Task<AdminSessionRecoveryOutcome> RecoverAsync(AdminIdentity admin, byte[]? requestIpHash,
            Guid sessionId, AdminSessionRecoveryRequest? request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(admin);
            AdminWriteKey key = AdminWriteKey.Read(request);
            string operationKind = ResolveOperationKind(request!.Action);
            string target = sessionId.ToString("D");

            // 결속이 맞는 동안에는 같은 운영자 작업 키의 다른 요청이 끝날 때까지 기다린다. 맞지 않으면 아무 잠금 없이 끝난다
            OperationRequest operation = _operations.CreateRequest(admin, request, operationKind, target);
            await using AdminOperationClaim claim = await _recovery.ClaimAsync(admin, operation, target,
                cancellationToken);
            if (!claim.Matches)
            {
                return new AdminSessionRecoveryOutcome(new OperationResult(key.OperationId.ToString("D"),
                    ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, 0,
                    claim.ServerTime.ToUnixTimeMilliseconds(), string.Empty), null);
            }

            // 저장된 어드민 작업의 재전송은 효과가 없으므로 보류와 무관하게 처음 결과를 돌려준다
            bool stored = await _recovery.OperationExistsAsync(admin.AdminId, key.OperationId, cancellationToken);
            if ((!stored) && (operationKind != AdminRecoveryActions.RETRY_SETTLEMENT) && (_liveness.IsDeferred()))
            {
                throw TransientRejectionException.LivenessUnknown(_liveness.RetryAfterSeconds());
            }
            if (operationKind == AdminRecoveryActions.FORCE_CLOSE)
            {
                OperationResult closed = await _operations.ExecuteAsync(admin, requestIpHash, request, operationKind,
                    target, (context, token) => ForceCloseAsync(context, sessionId, token), cancellationToken);
                return new AdminSessionRecoveryOutcome(closed, admin.AdminId);
            }

            // 행위자가 비활성이거나 역할이 낮아졌으면 서버 복구를 부르지 않는다.
            // 결속이 행위자 행을 잠그고 있어 어드민 작업 실행기도 같은 판정으로 FORBIDDEN_ROLE을 저장한다
            AdminRecoveryTarget? before = await _recovery.ReadTargetAsync(sessionId, cancellationToken);
            OperationResult? recovery = null;
            bool alreadyApplied = false;
            if ((!stored) && (claim.ActorActive) && (before != null) && (IsApplicable(operationKind, before)))
            {
                (Guid recoveryActorId, Guid recoveryOperationId) = RecoveryKey(operationKind, before);
                alreadyApplied = await _recovery.OperationExistsAsync(recoveryActorId, recoveryOperationId,
                    cancellationToken);
                await _auditWriter.AppendStandaloneAsync(AdminAuditEntry.ForAdmin(admin,
                    AdminRecoveryActions.RECOVERY_INTENT, AdminRecoveryActions.TARGET_GAME_SESSION,
                    target, key.Reason, AdminAuditJson.Serialize(before.ToAuditState()),
                    AdminAuditJson.Serialize(new
                    {
                        OperationId = key.OperationId.ToString("D"),
                        Action = request.Action,
                        RecoveryActorId = recoveryActorId.ToString("D"),
                        RecoveryOperationId = recoveryOperationId.ToString("D")
                    }), requestIpHash: requestIpHash), cancellationToken);
                recovery = await RunRecoveryAsync(operationKind, before, cancellationToken);
            }

            OperationResult result = await _operations.ExecuteAsync(admin, requestIpHash, request, operationKind,
                target, (context, token) => DecideAsync(context, sessionId, before, recovery, alreadyApplied, token),
                cancellationToken);
            return new AdminSessionRecoveryOutcome(result, before?.InstanceId);
        }

        private static string ResolveOperationKind(string? action)
        {
            return action switch
            {
                AdminSessionRecoveryCodes.RETRY_SETTLEMENT => AdminRecoveryActions.RETRY_SETTLEMENT,
                AdminSessionRecoveryCodes.SERVICE_CANCEL   => AdminRecoveryActions.SERVICE_CANCEL,
                AdminSessionRecoveryCodes.FORCE_CLOSE      => AdminRecoveryActions.FORCE_CLOSE,
                _ => throw new AdminRequestException("action must be RetrySettlement, ServiceCancel or ForceClose.")
            };
        }

        // 자동 복구와 같은 복구 작업 키. 정산은 종료 이벤트의 정산 operationId, 장애 취소는 세션 ID에서 만든 고정 ID다
        private static (Guid ActorId, Guid OperationId) RecoveryKey(string operationKind, AdminRecoveryTarget target)
        {
            return operationKind == AdminRecoveryActions.RETRY_SETTLEMENT
                ? (target.InstanceId, target.SettlementOperationId!.Value)
                : (target.InstanceId, PostgresSettlementRepository.ServiceCancelOperationId(target.SessionId));
        }

        // RecoveryPending은 기존 장애 취소 경로(Running만 취소)가 받지 않으므로 여기서도 INVALID_STATE로 둔다.
        // 종결 상태는 같은 경로가 만든 것일 때만 받는다. 그 경로는 같은 operationId라 저장된 결과를 다시 줄 뿐 효과가 없다
        private static bool IsApplicable(string operationKind, AdminRecoveryTarget target)
        {
            if (operationKind == AdminRecoveryActions.RETRY_SETTLEMENT)
            {
                return (target.SettlementOperationId.HasValue)
                    && (target.Result != GameSessionStates.RESULT_SERVICE_CANCELLED)
                    && ((target.State == GameSessionStates.SETTLING) || (IsSettled(target)));
            }

            return (target.State == GameSessionStates.RUNNING) || (IsServiceCancelled(target));
        }

        // 정산이 끝난 세션. 보스 성공은 정산 뒤 경매 단계(Auction)를 거쳐 Closed가 된다
        private static bool IsSettled(AdminRecoveryTarget target)
        {
            return (target.SettlementOperationId.HasValue)
                && ((target.State == GameSessionStates.CLOSED) || (target.State == GameSessionStates.AUCTION))
                && (target.Result != GameSessionStates.RESULT_SERVICE_CANCELLED);
        }

        // 장애 취소 경로가 닫은 Running 출정. 강제 종료한 Settling 출정은 종료 이벤트(정산 operationId)가 있어 여기서 빠진다
        private static bool IsServiceCancelled(AdminRecoveryTarget target)
        {
            return (target.State == GameSessionStates.CLOSED)
                && (target.Result == GameSessionStates.RESULT_SERVICE_CANCELLED)
                && (!target.SettlementOperationId.HasValue);
        }

        // 격리된 Settling만 닫는다. 판정과 종결 갱신이 같은 트랜잭션이고 종결 갱신이 같은 조건을 다시 걸어
        // 그 사이 늦게 온 정산이 이기면 아무것도 바꾸지 않고 INVALID_STATE다
        private async Task<OperationDecision> ForceCloseAsync(AdminOperationContext context, Guid sessionId,
            CancellationToken cancellationToken)
        {
            AdminRecoveryTarget? before = await _recovery.ReadTargetAsync(context.Execution, sessionId, cancellationToken);
            if (before == null)
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND);
            }
            if ((before.State != GameSessionStates.SETTLING)
                || (before.RecoveryFailures < PostgresSettlementRepository.MAX_RECOVERY_FAILURES))
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE, before.Revision);
            }

            long? revision = await _settlement.ForceCloseQuarantinedAsync(context.Execution, sessionId,
                EconomyLedgerCodes.ACTOR_ADMIN, context.Admin.AdminId, context.OperationId, cancellationToken);
            if (!revision.HasValue)
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE, before.Revision);
            }

            string operationId = context.OperationId.ToString("D");
            AdminRecoveryTarget? after = await _recovery.ReadTargetAsync(context.Execution, sessionId, cancellationToken);
            await context.AuditAsync(AdminRecoveryActions.TARGET_GAME_SESSION, sessionId.ToString("D"),
                before.ToAuditState(), new
                {
                    Session = after?.ToAuditState(),
                    AlreadyApplied = false,
                    RecoveryActorId = context.Admin.AdminId.ToString("D"),
                    RecoveryOperationId = operationId
                }, cancellationToken);
            return OperationDecision.Committed(revision.Value, operationId);
        }

        private Task<OperationResult?> RunRecoveryAsync(string operationKind, AdminRecoveryTarget target,
            CancellationToken cancellationToken)
        {
            if (operationKind == AdminRecoveryActions.RETRY_SETTLEMENT)
            {
                // 저장된 SessionEnded의 정산 operationId와 배정 서버 actor 그대로다. 자동 복구, 던전 /settlement와 같은 키다
                return SettleAsync(target, cancellationToken);
            }

            return _settlement.ServiceCancelAsync(target.SessionId, cancellationToken);
        }

        private async Task<OperationResult?> SettleAsync(AdminRecoveryTarget target, CancellationToken cancellationToken)
        {
            return await _settlement.SettleAsync(target.InstanceId, target.SessionId, target.SettlementOperationId!.Value,
                cancellationToken);
        }

        private async Task<OperationDecision> DecideAsync(AdminOperationContext context, Guid sessionId,
            AdminRecoveryTarget? before, OperationResult? recovery, bool alreadyApplied,
            CancellationToken cancellationToken)
        {
            if (before == null)
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND);
            }
            if ((recovery == null) || (recovery.Status != ContractCodes.OperationStatus.COMMITTED))
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE, before.Revision);
            }

            // 복구 작업은 이미 커밋됐으므로 이 트랜잭션에서 읽으면 종결 뒤 상태가 보인다
            AdminRecoveryTarget? after = await _recovery.ReadTargetAsync(context.Execution, sessionId, cancellationToken);
            await context.AuditAsync(AdminRecoveryActions.TARGET_GAME_SESSION, sessionId.ToString("D"),
                before.ToAuditState(), new
                {
                    Session = after?.ToAuditState(),
                    AlreadyApplied = alreadyApplied,
                    RecoveryActorId = before.InstanceId.ToString("D"),
                    RecoveryOperationId = recovery.OperationId,
                    RecoveryReceiptId = recovery.ReceiptId
                }, cancellationToken);
            return OperationDecision.Committed(after?.Revision ?? recovery.CommittedRevision, recovery.OperationId);
        }
    }
}
