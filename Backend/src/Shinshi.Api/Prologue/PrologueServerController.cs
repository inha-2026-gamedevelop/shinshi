// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Prologue;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Prologue
{
    /// <summary> 배정 던전 서버의 프롤로그 단계 저장과 완료 기록. 던전 서버 임대 Bearer만 받고 플레이어 토큰으로는 부를 수 없다 </summary>
    [ApiController]
    public sealed class PrologueServerController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresPrologueRepository _prologue;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueServerController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresPrologueRepository prologue)
            : base(callers, pool)
        {
            _prologue = prologue;
        }

        /// <summary> 200 확정(committedRevision = 저장 단계), 409 거절(PROLOGUE_STAGE_INVALID, PROLOGUE_ALREADY_DONE,
        /// SESSION_MISMATCH, WRONG_PHASE, SESSION_TERMINAL, IDEMPOTENCY_CONFLICT) </summary>
        [HttpPost(PrologueRouteTemplates.STAGE)]
        public async Task<IActionResult> SaveStage(string sessionId, [FromBody] PrologueStageRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if ((request == null) || !TryParseId(sessionId, out Guid session)
                || !TryParseId(request.OperationId, out Guid operationId)
                || !TryParseId(request.CharacterId, out Guid characterId))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _prologue.SaveStageAsync(new PrologueStageInput(caller.InstanceId, session,
                operationId, characterId, request.Stage, request.WolbaekPotions, request.TalismanChecked), cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }

        /// <summary> 200 확정(committedRevision = DONE), 409 거절(SESSION_MISMATCH, WRONG_PHASE, SESSION_TERMINAL, IDEMPOTENCY_CONFLICT).
        /// 정산(/settlement) 전에 부른다 </summary>
        [HttpPost(PrologueRouteTemplates.COMPLETE)]
        public async Task<IActionResult> Complete(string sessionId, [FromBody] PrologueCompleteRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.DUNGEON,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if ((request == null) || !TryParseId(sessionId, out Guid session)
                || !TryParseId(request.OperationId, out Guid operationId)
                || !TryParseId(request.CharacterId, out Guid characterId))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _prologue.CompleteAsync(caller.InstanceId, session, characterId, operationId,
                cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }
    }
}
