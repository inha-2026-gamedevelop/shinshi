// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Growth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Growth
{
    // 안내를 실제로 끝냈는지는 마을 서버가 판정한다(클라이언트 송신 시점은 U18 프롤로그, 튜토리얼 미구현).
    // API는 호출 서버가 마을 역할인지, 그 계정의 살아 있는 접속 임대가 호출 마을에 있는지만 확인한다
    /// <summary> 마을 서버 전용 계정 진행 기록: 신시 안내 완료 </summary>
    [ApiController]
    public sealed class TownAccountProgressController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresAccountProgressRepository _progress;

        /****************************************
        *                Methods
        ****************************************/

        public TownAccountProgressController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresAccountProgressRepository progress)
            : base(callers, pool)
        {
            _progress = progress;
        }

        /// <summary> 200 확정(영수증 ID = 계정 ID), 409 거절(SESSION_MISMATCH, INVALID_TARGET, IDEMPOTENCY_CONFLICT)이고
        /// 본문은 OperationResult다. 마을 역할이 아니면 효과 전에 401, 403이고 형식 오류는 400이며 저장하지 않는다 </summary>
        [HttpPost(GrowthRouteTemplates.INTRO_COMPLETE)]
        public async Task<IActionResult> CompleteIntro(string accountId, [FromBody] IntroCompleteRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if ((request == null) || !TryParseId(accountId, out Guid account)
                || !TryParseId(request.OperationId, out Guid operationId))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _progress.CompleteIntroAsync(caller.InstanceId, account, operationId,
                cancellationToken);
            return ApiResponseFactory.FromOperation(result);
        }
    }
}
