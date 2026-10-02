// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 운영자 계정 관리(Owner 전용). 생성 응답의 비밀번호와 TOTP 비밀은 그 생성을 확정한 응답에만 있다 </summary>
    [RequireAdminRole(EAdminRole.Owner)]
    public sealed class AdminOperatorsController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CREATE_OPERATION_KIND  = AdminAuditActions.OPERATOR_CREATE;
        public const string DISABLE_OPERATION_KIND = AdminAuditActions.OPERATOR_DISABLE;

        // 운영자 변경은 계정 revision 계약이 없어 0으로 확정한다
        private const long NO_REVISION = 0;

        private readonly PostgresAdminUserRepository _users;
        private readonly AdminCredentialFactory _credentials;
        private readonly AdminOperationExecutor _operations;

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperatorsController(PostgresAdminUserRepository users, AdminCredentialFactory credentials,
            AdminOperationExecutor operations)
        {
            _users       = users;
            _credentials = credentials;
            _operations  = operations;
        }

        [HttpGet(AdminApiRoutes.OPERATORS)]
        public async Task<ActionResult<AdminOperatorListResponse>> List(CancellationToken cancellationToken)
        {
            IReadOnlyList<AdminOperatorRecord> records = await _users.ListAsync(cancellationToken);
            return Ok(new AdminOperatorListResponse(records.Select(record => new AdminOperatorSummary(
                record.AdminId.ToString("D"), record.LoginName, (int)record.Role,
                record.CreatedAt.ToUnixTimeMilliseconds(), record.DisabledAt?.ToUnixTimeMilliseconds(),
                record.LockedUntil?.ToUnixTimeMilliseconds())).ToArray()));
        }

        /// <summary> 같은 operationId 재전송은 처음 결과를 주지만 비밀값은 다시 주지 않는다(서버에 원문이 없다) </summary>
        [HttpPost(AdminApiRoutes.OPERATORS)]
        public async Task<ActionResult> Create([FromBody] AdminOperatorCreateRequest? request,
            CancellationToken cancellationToken)
        {
            AdminWriteKey.Read(request);
            if ((!AdminLoginNames.TryNormalize(request!.LoginName, out string loginName))
                || (!Enum.IsDefined((EAdminRole)request.Role)))
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            // 영수증을 이번 요청이 만든 ID로 정해, 결과의 영수증이 같을 때만 이번 호출이 생성을 확정한 것으로 본다.
            // 느린 비밀번호 해시는 트랜잭션 밖에서 미리 계산한다
            EAdminRole role = (EAdminRole)request.Role;
            Guid adminId = Guid.NewGuid();
            string receiptId = adminId.ToString("D");
            AdminIssuedCredentials issued = _credentials.Create(adminId, loginName, role, CurrentAdmin.AdminId);
            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, CREATE_OPERATION_KIND,
                string.Empty, async (context, token) =>
                {
                    bool created = await _users.CreateAsync(context, issued.Operator, token);
                    return created
                        ? OperationDecision.Committed(NO_REVISION, receiptId)
                        : OperationDecision.Rejected(AdminContractCodes.RejectReason.LOGIN_NAME_TAKEN);
                }, cancellationToken);
            if (result.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return ApiResponseFactory.FromOperation(result);
            }

            bool issuedNow = string.Equals(result.ReceiptId, receiptId, StringComparison.Ordinal);
            return Ok(new AdminOperatorCreateResponse(result, result.ReceiptId, loginName, (int)role,
                issuedNow ? issued.Password : null, issuedNow ? issued.TotpSecretBase32 : null,
                issuedNow ? issued.TotpUri : null));
        }

        /// <summary> 자기 자신은 비활성화할 수 없다. 대상의 열린 어드민 세션을 함께 폐기한다 </summary>
        [HttpPost(AdminApiRoutes.OPERATOR_DISABLE)]
        public async Task<ActionResult> Disable(Guid adminId, [FromBody] AdminOperatorDisableRequest? request,
            CancellationToken cancellationToken)
        {
            // 실행기의 행위자 잠금(FOR SHARE)을 먼저 잡으면 서로를 비활성화하는 Owner 둘이 ID 순서 잠금 전에 교착한다.
            // 행위자 재검증은 DisableAsync가 ID 순서 잠금 안에서 한다
            string target = adminId.ToString("D");
            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, DISABLE_OPERATION_KIND,
                target, async (context, token) =>
                {
                    EAdminDisableOutcome outcome = await _users.DisableAsync(context, adminId, token);
                    return ToDecision(outcome, target);
                }, cancellationToken, lockActor: false);
            return ApiResponseFactory.FromOperation(result);
        }

        /// <summary> 비활성화 결과를 작업 결정으로 바꾼다. 역할이 낮아진 행위자는 실행기의 행위자 검사와 같은 FORBIDDEN_ROLE이다 </summary>
        public static OperationDecision ToDecision(EAdminDisableOutcome outcome, string target)
        {
            return outcome switch
            {
                EAdminDisableOutcome.Disabled         => OperationDecision.Committed(NO_REVISION, target),
                EAdminDisableOutcome.NotFound         => OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND),
                EAdminDisableOutcome.AlreadyDisabled  => OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE),
                EAdminDisableOutcome.SelfTarget       => OperationDecision.Rejected(AdminContractCodes.RejectReason.SELF_TARGET),
                EAdminDisableOutcome.ActorDisabled    => OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE),
                EAdminDisableOutcome.LastOwner        => OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE),
                EAdminDisableOutcome.ActorRoleLowered => OperationDecision.Rejected(AdminContractCodes.RejectReason.FORBIDDEN_ROLE),
                _ => throw new InvalidOperationException("Unknown disable outcome.")
            };
        }
    }
}
