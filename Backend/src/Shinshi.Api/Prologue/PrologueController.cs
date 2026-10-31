// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Prologue;

namespace Shinshi.Backend.Prologue
{
    /// <summary> 플레이어의 프롤로그 상태 조회, 입장권, 건너뛰기와 성장 메뉴 안내 완료. 공개 표시가 없으므로 플레이어 토큰만 통과한다.
    /// 계정은 요청이 아니라 인증된 주체에서 정한다 </summary>
    [ApiController]
    public sealed class PrologueController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresPrologueRepository _prologue;
        private readonly PostgresMenuGuideRepository _menuGuides;
        private readonly ServiceFeatureGate _features;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueController(PostgresPrologueRepository prologue, PostgresMenuGuideRepository menuGuides,
            ServiceFeatureGate features, TimeProvider timeProvider)
        {
            _prologue     = prologue;
            _menuGuides   = menuGuides;
            _features     = features;
            _timeProvider = timeProvider;
        }

        /// <summary> 200 PrologueStateResponse. 이 계정의 살아 있는 캐릭터가 아니면 404 CHARACTER_NOT_FOUND다 </summary>
        [HttpGet(PrologueRouteTemplates.STATE)]
        public async Task<IActionResult> GetState(string characterId, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }
            if (!TryParseId(characterId, out Guid character))
            {
                return Reject(StatusCodes.Status404NotFound, ContractCodes.RejectReason.CHARACTER_NOT_FOUND);
            }

            PrologueStateResponse? state = await _prologue.ReadStateAsync(player.AccountId, character, cancellationToken);
            return state == null
                ? Reject(StatusCodes.Status404NotFound, ContractCodes.RejectReason.CHARACTER_NOT_FOUND)
                : Ok(state);
        }

        /// <summary> 200 JoinTicket(던전 role). 게임 하위 세션 토큰만 받는다(마을 입장권과 같은 이유). 점검 503 MAINTENANCE,
        /// 출정 차단 409 FEATURE_DISABLED, 토큰 계열 401, 그 밖의 거절은 409 ApiRejection이다 </summary>
        [HttpPost(PrologueRouteTemplates.ENTRY)]
        public async Task<IActionResult> Enter([FromBody] PrologueEntryRequest? request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }
            if (AuthActors.GetPlayerTokenKind(HttpContext) != EPlayerTokenKind.GameSession)
            {
                return Reject(StatusCodes.Status403Forbidden, ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            if ((request == null) || !TryParseId(request.OperationId, out Guid operationId)
                || !TryParseId(request.CharacterId, out Guid characterId))
            {
                return BadRequest();
            }

            // 점검과 기능 차단은 새 입장만 막는다. 같은 operationId 재전송은 저장된 처음 결과를 받는다(출정 예약과 같은 규칙)
            int blocked = await _features.CheckNewSortieAsync(GameSessionCodes.Mode.PROLOGUE, cancellationToken);
            PrologueEntryOutcome? outcome = blocked == ContractCodes.RejectReason.NONE
                ? await _prologue.EnterAsync(player, characterId, operationId, cancellationToken)
                : await _prologue.FindStoredEntryAsync(player, characterId, operationId, cancellationToken);
            if (outcome == null)
            {
                return Reject(blocked == ContractCodes.RejectReason.MAINTENANCE
                    ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict, blocked);
            }
            if (outcome.Ticket != null)
            {
                return Ok(outcome.Ticket);
            }

            int status = outcome.ReasonCode switch
            {
                ContractCodes.RejectReason.TOKEN_EXPIRED or ContractCodes.RejectReason.TOKEN_REVOKED
                    => StatusCodes.Status401Unauthorized,
                ContractCodes.RejectReason.SESSION_MISMATCH => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status409Conflict
            };
            return Reject(status, outcome.ReasonCode);
        }

        /// <summary> 200 확정(영수증 ID = 캐릭터 ID), 409 거절(PROLOGUE_REQUIRED, PROLOGUE_ALREADY_DONE, CHARACTER_NOT_FOUND,
        /// CHARACTER_DELETE_PENDING, ALREADY_CONNECTED, RESERVATION_CONFLICT, IDEMPOTENCY_CONFLICT). 본문은 OperationResult다 </summary>
        [HttpPost(PrologueRouteTemplates.SKIP)]
        public async Task<IActionResult> Skip([FromBody] PrologueSkipRequest? request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }
            if ((request == null) || !TryParseId(request.OperationId, out Guid operationId)
                || !TryParseId(request.CharacterId, out Guid characterId))
            {
                return BadRequest();
            }

            OperationResult result = await _prologue.SkipAsync(player.AccountId, characterId, operationId,
                cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        /// <summary> 200 MenuGuideListResponse(정렬한 메뉴 ID) </summary>
        [HttpGet(PrologueRouteTemplates.MENU_GUIDES)]
        public async Task<IActionResult> ListMenuGuides(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            MenuGuideOutcome outcome = await _menuGuides.ListAsync(player.AccountId, cancellationToken);
            return Ok(new MenuGuideListResponse(outcome.MenuIds.ToArray(), outcome.ServerTimeUnixMs));
        }

        /// <summary> 본문 없음. 200 MenuGuideListResponse, 400 MENU_GUIDE_INVALID, 409 MENU_GUIDE_LIMIT. 같은 메뉴 재전송도 200이다 </summary>
        [HttpPost(PrologueRouteTemplates.MENU_GUIDE_COMPLETE)]
        public async Task<IActionResult> CompleteMenuGuide(string menuId, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            MenuGuideOutcome outcome = await _menuGuides.CompleteAsync(player.AccountId, menuId, cancellationToken);
            if (outcome.ReasonCode == ContractCodes.RejectReason.NONE)
            {
                return Ok(new MenuGuideListResponse(outcome.MenuIds.ToArray(), outcome.ServerTimeUnixMs));
            }

            return Reject(outcome.ReasonCode == ContractCodes.RejectReason.MENU_GUIDE_INVALID
                ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict, outcome.ReasonCode);
        }

        private IActionResult Reject(int statusCode, int reasonCode)
        {
            return ApiResponseFactory.Reject(statusCode, reasonCode, string.Empty, _timeProvider);
        }

        private static bool TryParseId(string? value, out Guid id)
        {
            return Guid.TryParse(value, out id) && (id != Guid.Empty);
        }
    }
}
