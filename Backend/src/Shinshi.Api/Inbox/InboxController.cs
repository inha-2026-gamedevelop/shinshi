// System
using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Inbox;

namespace Shinshi.Backend.Inbox
{
    /// <summary> 플레이어 우편함. 공개 표시가 없으므로 BearerTokenMiddleware가 플레이어 접근 토큰만 통과시킨다(어드민, 서버 토큰은 401) </summary>
    [ApiController]
    public sealed class InboxController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresInboxRepository _inbox;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<InboxController> _logger;
        private readonly ServiceFeatureGate? _features;

        /****************************************
        *                Methods
        ****************************************/

        // 어드민 모듈 없이 올린 테스트 호스트에서도 만들 수 있게 게이트는 선택 의존성이다
        public InboxController(PostgresInboxRepository inbox, TimeProvider timeProvider, ILogger<InboxController> logger,
            ServiceFeatureGate? features = null)
        {
            _inbox        = inbox;
            _timeProvider = timeProvider;
            _logger       = logger;
            _features     = features;
        }

        /// <summary> 곧 만료되는 순서로 최대 100개. 계정별 1분 한도를 넘으면 429 RATE_LIMITED와 Retry-After다(InboxRequestGateFilter) </summary>
        [HttpGet(ApiRoutes.INBOX)]
        [TypeFilter(typeof(InboxRequestGateFilter), Arguments = new object[] { EInboxRequestKind.List })]
        public async Task<ActionResult> List(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            Guid? accountId = AuthActors.GetPlayer(HttpContext)?.AccountId;
            if (accountId == null)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED, string.Empty, _timeProvider);
            }

            InboxListing listing = await _inbox.ListAsync(accountId.Value, cancellationToken);
            foreach (Guid corrupt in listing.CorruptInboxIds)
            {
                // 보상 JSON은 운영 데이터라 남기지 않고 우편 ID만 남긴다
                _logger.LogWarning("Inbox entry {InboxId} has corrupt stored rewards and was left out of the list.",
                    corrupt);
            }
            InboxEntry[] entries = listing.Entries.Select(entry => new InboxEntry(entry.InboxId.ToString("D"),
                entry.SourceKind, entry.CreatedAt.ToUnixTimeMilliseconds(), entry.ExpiresAt.ToUnixTimeMilliseconds(),
                entry.Rewards.Select(line => new InboxReward(line.AssetKind, line.AssetId, line.BindingKind,
                    line.Quantity)).ToArray(), entry.RequiresCharacter)).ToArray();
            return Ok(new InboxListResponse(entries, listing.ServerTime.ToUnixTimeMilliseconds()));
        }

        /// <summary> 거절은 409와 OperationResult(INBOX_*, 회수함에 공간이 없으면 INVENTORY_FULL, 남은 줄의 획득 캐릭터가 삭제됐으면
        /// CHARACTER_NOT_FOUND)이고 같은 operationId 재전송은 처음 결과를 받는다.
        /// 회수함 우편은 상한까지 남은 공간만 옮기는 부분 수령이다. 형식 오류는 400이다.
        /// 이 계정의 우편이 아니면 작업 행 없이 409 INBOX_NOT_FOUND다. 계정별 1분 한도를 넘으면 429 RATE_LIMITED와 Retry-After,
        /// 본문이 InboxRequestGateFilter.MAX_BODY_BYTES를 넘으면 413이며 둘 다 본문을 읽기 전에 거절한다 </summary>
        [HttpPost(ApiRoutes.INBOX_CLAIM)]
        [RequestSizeLimit(InboxRequestGateFilter.MAX_BODY_BYTES)]
        [TypeFilter(typeof(InboxRequestGateFilter), Arguments = new object[] { EInboxRequestKind.Claim })]
        public async Task<ActionResult> Claim([FromBody] InboxClaimRequest? request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            Guid? accountId = AuthActors.GetPlayer(HttpContext)?.AccountId;
            if (accountId == null)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED, string.Empty, _timeProvider);
            }
            if ((request == null) || !Guid.TryParse(request.OperationId, out Guid operationId)
                || (operationId == Guid.Empty) || !Guid.TryParse(request.InboxId, out Guid inboxId))
            {
                return BadRequest();
            }

            Guid? characterId = null;
            if (!string.IsNullOrEmpty(request.CharacterId))
            {
                if (!Guid.TryParse(request.CharacterId, out Guid parsed) || (parsed == Guid.Empty))
                {
                    return BadRequest();
                }
                characterId = parsed;
            }

            // 차단 중에도 같은 operationId 재전송은 저장된 처음 결과를 받는다. 차단 거절은 저장하지 않으므로
            // 한 번도 실행되지 않은 수령은 차단이 풀린 뒤 같은 operationId로 다시 보내면 된다
            if ((_features != null)
                && (await _features.CheckFeatureAsync(ServiceFeatureKeys.INBOX_CLAIM, cancellationToken)
                    != ContractCodes.RejectReason.NONE))
            {
                OperationResult? stored = await FindStoredWhileBlockedAsync(accountId.Value, operationId, inboxId,
                    characterId, cancellationToken);
                return stored == null
                    ? ApiResponseFactory.Reject(StatusCodes.Status409Conflict,
                        ContractCodes.RejectReason.FEATURE_DISABLED, string.Empty, _timeProvider)
                    : ApiResponseFactory.FromOperation(stored);
            }

            OperationResult result = await _inbox.ClaimAsync(accountId.Value, operationId, inboxId, characterId,
                cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        // 차단 판정은 이미 끝났으므로 저장 결과를 못 읽으면 차단 거절로 답한다. 효과는 없고 같은 ID로 다시 보내면 된다
        private async Task<OperationResult?> FindStoredWhileBlockedAsync(Guid accountId, Guid operationId, Guid inboxId,
            Guid? characterId, CancellationToken cancellationToken)
        {
            try
            {
                return await _inbox.FindStoredClaimAsync(accountId, operationId, inboxId, characterId,
                    cancellationToken);
            }
            catch (DbException exception)
            {
                // 예외 메시지에 연결 정보가 섞일 수 있어 형식 이름만 남긴다
                _logger.LogWarning("Stored inbox claim lookup failed while claims are disabled ({ExceptionType}).",
                    exception.GetType().Name);
                return null;
            }
        }
    }
}
