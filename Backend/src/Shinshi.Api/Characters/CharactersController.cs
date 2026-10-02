// System
using System;
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

        private readonly ICharacterRepository _characters;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public CharactersController(ICharacterRepository characters, TimeProvider timeProvider)
        {
            _characters   = characters;
            _timeProvider = timeProvider;
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

        /// <summary> 200 확정, 409 거절(CHARACTER_NAME_TAKEN, NO_CAPACITY, IDEMPOTENCY_CONFLICT)이고 본문은 CharacterCreateResponse다.
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

            // TODO(민성): 직업별 허용 헤어와 팔레트 카탈로그 검사는 API에 카탈로그가 생기면 붙인다. 지금은 구조와 값 범위만 본다
            if (!IsKnownClass(request.ClassId) || (request.Appearance == null) || !request.Appearance.IsWellFormed())
            {
                return Reject(StatusCodes.Status400BadRequest, ContractCodes.RejectReason.INVALID_APPEARANCE);
            }

            string requestJson = JsonConvert.SerializeObject(request, ContractJson.CreateSettings());
            string appearanceJson = JsonConvert.SerializeObject(request.Appearance, ContractJson.CreateSettings());
            CharacterCreateOutcome outcome = await _characters.CreateAsync(accountId.Value, operationId, name, nameKey,
                request.ClassId, appearanceJson, requestJson, cancellationToken);
            int statusCode = outcome.Result.Status == ContractCodes.OperationStatus.COMMITTED
                ? StatusCodes.Status200OK : StatusCodes.Status409Conflict;
            return StatusCode(statusCode, new CharacterCreateResponse(outcome.Result, ToSummary(outcome.Character)));
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

        private static bool IsKnownClass(int classId)
        {
            return (classId == ContractCodes.PlayerClass.WARRIOR) || (classId == ContractCodes.PlayerClass.SORCERER)
                || (classId == ContractCodes.PlayerClass.SUMMONER);
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
