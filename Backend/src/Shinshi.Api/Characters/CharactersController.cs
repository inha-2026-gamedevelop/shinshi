// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Newtonsoft.Json;

// Project
using Shinshi.Backend.Accounts;
using Shinshi.Contracts;
using Shinshi.Data.Characters;

namespace Shinshi.Backend.Characters
{
    /// <summary> 캐릭터 생성과 이름 가용성. 공개 표시가 없으므로 플레이어 접근 토큰이나 게임 하위 세션이 필요하다 </summary>
    [ApiController]
    [ServiceFilter(typeof(AccountInvalidModelStateFilter), Order = AccountInvalidModelStateFilter.FILTER_ORDER)]
    public sealed class CharactersController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // ApiRoutes.CHARACTER_DELETE_FORMAT과 같은 경로의 ASP.NET Core 템플릿
        public const string CHARACTER_DELETE_TEMPLATE = "/v1/characters/{characterId}/delete";

        private readonly ICharacterRepository _characters;
        private readonly AccountRequestLimiter _accounts;
        private readonly TimeProvider _timeProvider;
        private readonly PlayerClassPolicy _classPolicy;

        /****************************************
        *                Methods
        ****************************************/

        public CharactersController(ICharacterRepository characters, AccountRequestLimiter accounts,
            TimeProvider timeProvider, PlayerClassPolicy classPolicy)
        {
            _characters   = characters;
            _accounts     = accounts;
            _timeProvider = timeProvider;
            _classPolicy  = classPolicy;
        }

        /// <summary> 인증된 계정의 캐릭터 목록을 반환한다. 계정 ID는 요청에서 받지 않는다 </summary>
        [HttpGet(ApiRoutes.CHARACTERS)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            Guid? accountId = AuthActors.GetPlayer(HttpContext)?.AccountId;
            if (accountId == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }

            var characters = await _characters.ListAsync(accountId.Value, cancellationToken);
            return Ok(new CharacterListResponse(characters.Select(character => ToSummary(character)!).ToArray(),
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()));
        }

        /// <summary> 200과 AvailabilityResponse. 입력 중 안내용이며 확정은 생성 요청의 유일 제약이 한다. IP당 1분 30회 </summary>
        [EnableRateLimiting(AccountSignupLimiter.AVAILABILITY_POLICY)]
        [HttpGet(ApiRoutes.CHARACTER_NAME_AVAILABILITY)]
        public async Task<IActionResult> NameAvailability([FromQuery] string? name, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            if (AuthActors.GetPlayer(HttpContext) == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }
            if (!CharacterNameRules.TryNormalize(name, out string _, out string nameKey))
            {
                return Ok(Availability(ContractCodes.RejectReason.INVALID_CHARACTER_NAME));
            }

            bool taken = await _characters.IsNameTakenAsync(nameKey, cancellationToken);
            return Ok(Availability(taken ? ContractCodes.RejectReason.CHARACTER_NAME_TAKEN
                : ContractCodes.RejectReason.NONE));
        }

        /// <summary> 200 확정, 409 거절(CHARACTER_NAME_TAKEN, NO_CAPACITY, IDEMPOTENCY_CONFLICT)이고 본문은 CharacterCreateResponse다. 열리지 않은 새 직업(3~5)은 409 FEATURE_DISABLED이며 본문은 거절 코드뿐이고 작업으로 저장하지 않는다. 계정별 1분 한도를 넘으면 429다.
        /// 400은 형식 오류(INVALID_CHARACTER_NAME, INVALID_APPEARANCE)이며 작업으로 저장하지 않는다. 같은 operationId 재전송은 처음 결과와 같은 캐릭터다 </summary>
        [HttpPost(ApiRoutes.CHARACTERS)]
        public async Task<IActionResult> Create([FromBody] CharacterCreateRequest? request,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            Guid? accountId = AuthActors.GetPlayer(HttpContext)?.AccountId;
            if (accountId == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }
            if ((request == null) || !Guid.TryParse(request.OperationId, out Guid operationId)
                || (operationId == Guid.Empty))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);
            }
            if (!CharacterNameRules.TryNormalize(request.Name, out string name, out string nameKey))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_CHARACTER_NAME);
            }

            // v1은 과거 요청을 소급 거절하지 않도록 구조 검사만 본다. v2는 등록된 face/hair/outfit과 중립값까지 본다
            if (!PlayerClassPolicy.IsKnown(request.ClassId) || (request.Appearance == null)
                || !request.Appearance.IsWellFormed())
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_APPEARANCE);
            }
            // 외형 규칙은 직업과 무관하다. Contracts 카탈로그가 새 직업(3~5)을 알기 전까지는 기존 직업 ID로 같은 규칙을 적용한다
            int appearanceClassId = request.ClassId > PlayerClassPolicy.LEGACY_MAX_ID
                ? ContractCodes.PlayerClass.WARRIOR : request.ClassId;
            bool appearanceAllowed = (request.Appearance.Version == CharacterAppearance.VERSION_1)
                || CharacterAppearanceCatalog.IsAllowed(appearanceClassId, request.Appearance);
            if (!appearanceAllowed)
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_APPEARANCE);
            }
            // 새 직업(3~5)은 Characters:EnabledClassIds로 열기 전까지 생성하지 못한다. 작업으로 저장하지 않는다
            if (!_classPolicy.IsEnabled(request.ClassId))
            {
                return Reject(StatusCodes.Status409Conflict, ContractCodes.RejectReason.FEATURE_DISABLED);
            }
            if (!_accounts.TryAcquireCharacterWrite(accountId.Value))
            {
                return AuthFailureLimiter.RejectTooMany(HttpContext, _timeProvider);
            }

            string requestJson = JsonConvert.SerializeObject(request, ContractJson.CreateSettings());
            string appearanceJson = JsonConvert.SerializeObject(request.Appearance, ContractJson.CreateSettings());
            CharacterCreateOutcome outcome = await _characters.CreateAsync(accountId.Value, operationId, name, nameKey,
                request.ClassId, appearanceJson, requestJson, cancellationToken);
            int statusCode = outcome.Result.Status == ContractCodes.OperationStatus.COMMITTED
                ? StatusCodes.Status200OK : StatusCodes.Status409Conflict;
            return StatusCode(statusCode, new CharacterCreateResponse(outcome.Result, ToSummary(outcome.Character)));
        }

        /// <summary> 200 확정(영수증 ID = 캐릭터 ID), 409 거절(CHARACTER_NOT_FOUND, ALREADY_CONNECTED, RESERVATION_CONFLICT,
        /// STALE_REVISION, IDEMPOTENCY_CONFLICT)이고 본문은 OperationResult다. 429는 계정별 1분 한도 또는 24시간 삭제 상한(RATE_LIMITED)이다. 400은 형식 오류이며 작업으로 저장하지 않는다 </summary>
        [HttpPost(CHARACTER_DELETE_TEMPLATE)]
        public async Task<IActionResult> Delete(string characterId, [FromBody] CharacterDeleteRequest? request,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            Guid? accountId = AuthActors.GetPlayer(HttpContext)?.AccountId;
            if (accountId == null)
            {
                return Reject(StatusCodes.Status401Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED);
            }
            if ((request == null) || !Guid.TryParse(request.OperationId, out Guid operationId)
                || (operationId == Guid.Empty) || !Guid.TryParse(characterId, out Guid target) || (target == Guid.Empty)
                || (request.ExpectedRevision < 0))
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT);
            }
            if (!_accounts.TryAcquireCharacterWrite(accountId.Value))
            {
                return AuthFailureLimiter.RejectTooMany(HttpContext, _timeProvider);
            }

            // 최근 24시간 삭제 상한 초과(RATE_LIMITED)는 저장된 작업 결과지만 재시도 시점을 알리도록 429로 돌려준다
            OperationResult result = await _characters.DeleteAsync(accountId.Value, operationId, target,
                request.ExpectedRevision, cancellationToken);
            if ((result.Status == ContractCodes.OperationStatus.REJECTED)
                && (result.ReasonCode == ContractCodes.RejectReason.RATE_LIMITED))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, result);
            }
            return ApiResponseFactory.FromOperation(result);
        }

        private static CharacterSummary? ToSummary(CharacterRecord? character)
        {
            if (character == null)
            {
                return null;
            }

            CharacterAppearance? appearance = character.AppearanceJson == null ? null
                : JsonConvert.DeserializeObject<CharacterAppearance>(character.AppearanceJson, ContractJson.CreateSettings());
            return new CharacterSummary(character.CharacterId.ToString("D"), character.Name, character.ClassId,
                character.Revision, appearance);
        }

        private AvailabilityResponse Availability(int reasonCode)
        {
            return new AvailabilityResponse(reasonCode == ContractCodes.RejectReason.NONE, reasonCode,
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        }

        private ObjectResult Reject(int statusCode, int reasonCode)
        {
            return AccountRejections.Reject(HttpContext, statusCode, reasonCode, _timeProvider);
        }
    }
}
