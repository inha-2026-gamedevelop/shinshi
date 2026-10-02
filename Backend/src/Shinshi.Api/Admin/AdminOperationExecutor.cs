// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 쓰기의 공용 실행 경로. actor는 운영자 ID이고 operationId, reason을 검증한 뒤 IdempotentOperationExecutor로 실행한다.
    /// 효과 콜백은 AdminOperationContext.Execution의 트랜잭션만 쓰고, Committed로 끝내려면 같은 콜백에서 감사 행을 남겨야 한다 </summary>
    public sealed class AdminOperationExecutor
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TARGET_FIELD  = "target";
        private const string REQUEST_FIELD = "request";

        private static readonly JsonSerializer G_ContractSerializer = JsonSerializer.Create(ContractJson.CreateSettings());

        private readonly IdempotentOperationExecutor _executor;
        private readonly IAdminAuditWriter _auditWriter;
        private readonly PostgresAdminOperationClaimRepository _claims;

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperationExecutor(IdempotentOperationExecutor executor, IAdminAuditWriter auditWriter,
            PostgresAdminOperationClaimRepository claims)
        {
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(auditWriter);
            ArgumentNullException.ThrowIfNull(claims);
            _executor    = executor;
            _auditWriter = auditWriter;
            _claims      = claims;
        }

        /// <summary> targetKey에는 본문 밖의 대상(경로의 grantId 등)을 넣는다. 본문과 함께 해시되므로 같은 operationId로
        /// 다른 대상을 부르면 IdempotencyConflict가 된다. 거절 결정은 효과와 감사 행을 함께 롤백하고 결과만 저장한다.
        /// lockActor는 행위자와 대상을 정해진 순서로 직접 잠그는 효과(운영자 비활성화)만 false로 준다 </summary>
        public Task<OperationResult> ExecuteAsync(HttpContext httpContext, IAdminWriteRequest? request,
            string operationKind, string targetKey,
            Func<AdminOperationContext, CancellationToken, Task<OperationDecision>> effect,
            CancellationToken cancellationToken = default, bool lockActor = true)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            return ExecuteAsync(AdminActors.Get(httpContext), AdminActors.GetRequestIpHash(httpContext), request,
                operationKind, targetKey, effect, cancellationToken, lockActor);
        }

        /// <summary> HTTP 요청 밖에서 확인된 운영자로 실행한다. 검증과 감사 규칙은 HTTP 경로와 같다.
        /// 효과 전에 작업 키를 영속 결속하며 다른 해시는 결과 키를 점유하지 않고 IDEMPOTENCY_CONFLICT다.
        /// 행위자가 비활성화되거나 역할이 낮아졌으면 효과 트랜잭션에서 FORBIDDEN_ROLE을 저장한다 </summary>
        public async Task<OperationResult> ExecuteAsync(AdminIdentity admin, byte[]? requestIpHash,
            IAdminWriteRequest? request, string operationKind, string targetKey,
            Func<AdminOperationContext, CancellationToken, Task<OperationDecision>> effect,
            CancellationToken cancellationToken = default, bool lockActor = true)
        {
            ArgumentNullException.ThrowIfNull(effect);
            OperationRequest operation = CreateRequest(admin, request, operationKind, targetKey);
            AdminWriteKey key = AdminWriteKey.Read(request);
            AdminOperationBinding binding = await _claims.BindAsync(operation, targetKey, cancellationToken);
            if (!binding.Matches)
            {
                // 다른 요청의 복구가 아직 operations 행을 만들기 전이어도 그 키에 거절 결과를 저장하지 않는다
                return new OperationResult(key.OperationId.ToString("D"), ContractCodes.OperationStatus.REJECTED,
                    ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, 0, binding.ServerTime.ToUnixTimeMilliseconds(),
                    string.Empty);
            }

            return await _executor.ExecuteAsync(operation, async (execution, token) =>
            {
                int rejection = await AdminOperationGuard.VerifyAsync(execution, admin, operation, lockActor, token);
                if (rejection != ContractCodes.RejectReason.NONE)
                {
                    return OperationDecision.Rejected(rejection);
                }

                AdminOperationContext context = new AdminOperationContext(execution, admin, key.OperationId,
                    operationKind, key.Reason, requestIpHash, _auditWriter);
                OperationDecision decision = await effect(context, token);
                if ((decision != null) && (decision.Status == ContractCodes.OperationStatus.COMMITTED)
                    && (context.AuditCount == 0))
                {
                    // 예외는 효과와 Pending 행을 모두 롤백한다. 감사 없는 어드민 변경이 커밋되지 않게 한다
                    throw new InvalidOperationException("A committed admin operation must write an audit entry.");
                }

                return decision!;
            }, cancellationToken);
        }

        /// <summary> 실행기가 저장하는 것과 같은 작업 키와 요청 해시. 실행 전에 같은 키를 결속해야 하는 수동 복구가 쓴다 </summary>
        public OperationRequest CreateRequest(AdminIdentity admin, IAdminWriteRequest? request, string operationKind,
            string targetKey)
        {
            ArgumentNullException.ThrowIfNull(admin);
            ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
            ArgumentNullException.ThrowIfNull(targetKey);
            if (operationKind.Length > AdminAuditEntry.MAX_ACTION_LENGTH)
            {
                throw new ArgumentException("Operation kind is too long for the audit action.", nameof(operationKind));
            }

            AdminWriteKey key = AdminWriteKey.Read(request);
            JObject body = new JObject
            {
                [TARGET_FIELD]  = targetKey,
                [REQUEST_FIELD] = JToken.FromObject(request!, G_ContractSerializer)
            };
            return new OperationRequest(admin.AdminId, key.OperationId, operationKind, body.ToString(Formatting.None));
        }
    }
}
