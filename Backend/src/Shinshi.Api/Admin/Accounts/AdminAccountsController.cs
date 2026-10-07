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
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Admin
{
    /// <summary> 계정 검색, 상세 조회(Viewer, 상세는 감사)와 강제 종료(Operator). 검색 이름과 초대 코드 원문은
    /// 로그, 감사, 응답에 남기지 않는다 </summary>
    public sealed class AdminAccountsController : AdminControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string KICK_OPERATION_KIND = AdminAccountAuditActions.ACCOUNT_KICK;

        // 강제 종료는 계정 revision을 바꾸지 않는다
        private const long NO_REVISION = 0;

        private readonly PostgresAdminAccountRepository _accounts;
        private readonly AuthTokenHasher _hasher;
        private readonly IAdminAuditWriter _auditWriter;
        private readonly AdminOperationExecutor _operations;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountsController(PostgresAdminAccountRepository accounts, AuthTokenHasher hasher,
            IAdminAuditWriter auditWriter, AdminOperationExecutor operations)
        {
            _accounts    = accounts;
            _hasher      = hasher;
            _auditWriter = auditWriter;
            _operations  = operations;
        }

        /// <summary> 계정 ID나 캐릭터 ID(UUID)로만 찾는다. 요청 로그에 조회 문자열이 남으므로 초대 코드 원문은 받지 않고 400이다.
        /// 초대 코드 검색은 POST ACCOUNTS_SEARCH를 쓴다 </summary>
        [HttpGet(AdminApiRoutes.ACCOUNTS)]
        public async Task<ActionResult<AdminAccountSearchResponse>> Search(
            [FromQuery(Name = AdminApiRoutes.QUERY_PARAMETER)] string? query, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(query?.Trim() ?? string.Empty, out Guid id))
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            IReadOnlyList<AdminAccountSummary> found = await _accounts.SearchByIdAsync(id, cancellationToken);
            return Ok(new AdminAccountSearchResponse(found.ToArray()));
        }

        /// <summary> query는 UUID 또는 초대 코드, characterName은 생성과 같은 정규화 키로 정확 검색한다.
        /// 이름 검색은 기본으로 삭제된 캐릭터를 제외하고 includeDeletedCharacters로 포함할 수 있다 </summary>
        [HttpPost(AdminApiRoutes.ACCOUNTS_SEARCH)]
        public async Task<ActionResult<AdminAccountSearchResponse>> SearchByBody(
            [FromBody] AdminAccountSearchRequest? request, CancellationToken cancellationToken)
        {
            if (request?.CharacterName != null)
            {
                if ((request.Query != null)
                    || (!CharacterNameRules.TryNormalize(request.CharacterName, out _, out string nameKey)))
                {
                    return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
                }

                IReadOnlyList<AdminAccountSummary> byName = await _accounts.SearchByCharacterNameKeyAsync(nameKey,
                    request.IncludeDeletedCharacters, cancellationToken);
                return Ok(new AdminAccountSearchResponse(byName.ToArray(), request.IncludeDeletedCharacters));
            }

            string trimmed = request?.Query?.Trim() ?? string.Empty;
            if ((trimmed.Length == 0) || (request?.IncludeDeletedCharacters == true))
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }

            IReadOnlyList<AdminAccountSummary> found;
            if (Guid.TryParse(trimmed, out Guid id))
            {
                found = await _accounts.SearchByIdAsync(id, cancellationToken);
            }
            else if (_hasher.IsAuthentic(trimmed))
            {
                found = await _accounts.SearchByInviteHashAsync(_hasher.Hash(AuthTokenHasher.INVITE_DOMAIN, trimmed),
                    cancellationToken);
            }
            else
            {
                found = Array.Empty<AdminAccountSummary>();
            }

            return Ok(new AdminAccountSearchResponse(found.ToArray()));
        }

        /// <summary> 민감 조회라 reason 조회 문자열이 필수이고, 데이터를 읽기 전에 감사 행을 먼저 커밋한다(감사 없이 내보내지 않는다) </summary>
        [HttpGet(AdminApiRoutes.ACCOUNT)]
        public async Task<ActionResult<AdminAccountDetailResponse>> Detail(Guid accountId,
            [FromQuery(Name = AdminAccountQuery.REASON_PARAMETER)] string? reason, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(reason) || (reason.Length > AdminContractCodes.MAX_REASON_LENGTH)
                || (accountId == Guid.Empty))
            {
                return Reject(StatusCodes.Status400BadRequest, AdminContractCodes.RejectReason.INVALID_REQUEST);
            }
            if (!await _accounts.ExistsAsync(accountId, cancellationToken))
            {
                return Reject(StatusCodes.Status404NotFound, AdminContractCodes.RejectReason.NOT_FOUND);
            }

            await _auditWriter.AppendStandaloneAsync(AdminAuditEntry.ForAdmin(CurrentAdmin,
                AdminAccountAuditActions.ACCOUNT_VIEW, AdminAccountAuditActions.TARGET_ACCOUNT,
                accountId.ToString("D"), reason, requestIpHash: RequestIpHash), cancellationToken);
            AdminAccountDetailResponse? detail = await _accounts.ReadDetailAsync(accountId, cancellationToken);
            if (detail == null)
            {
                return Reject(StatusCodes.Status404NotFound, AdminContractCodes.RejectReason.NOT_FOUND);
            }

            return Ok(detail);
        }

        /// <summary> 로그인 묶음과 모든 하위 토큰, revokeInvites(기본 true)면 미폐기 초대까지 폐기한다. 현재 접속은 유지한다:
        /// 접속 중인 게임 서버의 계정 임대는 남으므로 지금 접속은 이어지고, 폐기된 토큰의 갱신, 재접속, 새 입장 토큰 발급부터
        /// 거절된다. 비밀번호 로그인은 막지 않으므로 같은 아이디와 비밀번호로 새 토큰을 받을 수 있다(로그인 차단은 제재 P1).
        /// 확정 응답은 AdminAccountKickResponse, 거절은 409 OperationResult다 </summary>
        [RequireAdminRole(EAdminRole.Operator)]
        [HttpPost(AdminApiRoutes.ACCOUNT_KICK)]
        public async Task<ActionResult> Kick(Guid accountId, [FromBody] AdminAccountKickRequest? request,
            CancellationToken cancellationToken)
        {
            string target = accountId.ToString("D");
            bool revokeInvites = request?.RevokesInvites ?? true;
            OperationResult result = await _operations.ExecuteAsync(HttpContext, request, KICK_OPERATION_KIND, target,
                async (context, token) =>
                {
                    AdminKickOutcome outcome = await _accounts.KickAsync(context, accountId, revokeInvites, token);
                    return outcome.IsFound
                        ? OperationDecision.Committed(NO_REVISION, target)
                        : OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND);
                }, cancellationToken);
            if (result.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return ApiResponseFactory.FromOperation(result);
            }

            return Ok(new AdminAccountKickResponse(result, target, revokeInvites, true));
        }
    }
}
