// System
using System;
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
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Admin
{
    /// <summary> 초대 코드 발급과 폐기(Operator). 원문은 발급을 확정한 응답에만 싣고 서버에는 HMAC만 남긴다 </summary>
    [RequireAdminRole(EAdminRole.Operator)]
    public sealed class AdminInvitesController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ISSUE_OPERATION_KIND  = AdminAccountAuditActions.INVITE_ISSUE;
        public const string REVOKE_OPERATION_KIND = AdminAccountAuditActions.INVITE_REVOKE;

        // 초대 발급, 폐기는 계정 revision을 바꾸지 않는다
        private const long NO_REVISION = 0;

        private readonly PostgresAdminInviteRepository _invites;
        private readonly AuthTokenHasher _hasher;
        private readonly AdminOperationExecutor _operations;

        /****************************************
        *                Methods
        ****************************************/

        public AdminInvitesController(PostgresAdminInviteRepository invites, AuthTokenHasher hasher,
            AdminOperationExecutor operations)
        {
            _invites    = invites;
            _hasher     = hasher;
            _operations = operations;
        }

        /// <summary> 같은 operationId 재전송은 같은 inviteId를 주지만 inviteCode는 null이다. 응답을 잃었으면 그 초대를 폐기하고 새로 발급한다 </summary>
        [HttpPost(AdminApiRoutes.INVITES)]
        public async Task<ActionResult> Issue([FromBody] AdminInviteIssueRequest? request,
            CancellationToken cancellationToken)
        {
            AdminWriteKey.Read(request);
            Guid? accountId = null;
            if (request!.AccountId != null)
            {
                if ((!Guid.TryParse(request.AccountId, out Guid parsed)) || (parsed == Guid.Empty))
                {
                    return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
                }
                accountId = parsed;
            }
            if ((request.MaxUses < AdminInviteIssueRequest.MIN_USES) || (request.MaxUses > AdminInviteIssueRequest.MAX_USES)
                || (request.ExpiresInHours < AdminInviteIssueRequest.MIN_HOURS)
                || (request.ExpiresInHours > AdminInviteIssueRequest.MAX_HOURS))
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            // 영수증을 이번 요청이 만든 ID로 정해 결과의 영수증이 같을 때만 이번 호출이 발급을 확정한 것으로 본다
            Guid inviteId = Guid.NewGuid();
            string receiptId = inviteId.ToString("D");
            string inviteCode = _hasher.CreateToken();
            AdminInviteDraft draft = new AdminInviteDraft(inviteId, accountId,
                _hasher.Hash(AuthTokenHasher.INVITE_DOMAIN, inviteCode), request.MaxUses,
                TimeSpan.FromHours(request.ExpiresInHours), request.RevokeExistingSessions ?? true);
            AdminInviteRecord? issued = null;
            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, ISSUE_OPERATION_KIND,
                string.Empty, async (context, token) =>
                {
                    issued = await _invites.IssueAsync(context, draft, token);
                    return issued == null
                        ? OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND)
                        : OperationDecision.Committed(NO_REVISION, receiptId);
                }, cancellationToken);
            if (result.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return ApiResponseFactory.FromOperation(result);
            }

            bool issuedNow = string.Equals(result.ReceiptId, receiptId, StringComparison.Ordinal) && (issued != null);
            AdminInviteRecord? invite = issuedNow ? issued : await _invites.FindAsync(Guid.Parse(result.ReceiptId),
                cancellationToken);
            if (invite == null)
            {
                throw new InvalidOperationException("A committed invite operation must reference an invite row.");
            }

            return Ok(new AdminInviteIssueResponse(result, invite.InviteId.ToString("D"), invite.AccountId.ToString("D"),
                invite.MaxUses, invite.ExpiresAt.ToUnixTimeMilliseconds(), issuedNow ? inviteCode : null));
        }

        /// <summary> 아직 폐기하지 않은 초대만 폐기한다. 이미 그 코드로 만든 로그인은 강제 종료로 따로 끊는다 </summary>
        [HttpPost(AdminApiRoutes.INVITE_REVOKE)]
        public async Task<ActionResult> Revoke(Guid inviteId, [FromBody] AdminInviteRevokeRequest? request,
            CancellationToken cancellationToken)
        {
            string target = inviteId.ToString("D");
            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, REVOKE_OPERATION_KIND, target,
                async (context, token) =>
                {
                    EAdminInviteRevokeStatus status = await _invites.RevokeAsync(context, inviteId, token);
                    return status switch
                    {
                        EAdminInviteRevokeStatus.Revoked        => OperationDecision.Committed(NO_REVISION, target),
                        EAdminInviteRevokeStatus.NotFound       => OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND),
                        EAdminInviteRevokeStatus.AlreadyRevoked => OperationDecision.Rejected(AdminContractCodes.RejectReason.INVALID_STATE),
                        _ => throw new InvalidOperationException("Unknown invite revoke status.")
                    };
                }, cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }
    }
}
