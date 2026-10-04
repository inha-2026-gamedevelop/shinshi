// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Growth;

namespace Shinshi.Backend.Growth
{
    /// <summary> 플레이어 계정의 영구 진행 조회. 공개 표시가 없으므로 BearerTokenMiddleware가 플레이어 토큰만 통과시킨다 </summary>
    [ApiController]
    public sealed class AccountProgressController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresAccountProgressRepository _progress;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public AccountProgressController(PostgresAccountProgressRepository progress, TimeProvider timeProvider)
        {
            _progress     = progress;
            _timeProvider = timeProvider;
        }

        /// <summary> 200 AccountProgressResponse. 계정 ID는 요청에서 받지 않고 인증된 주체에서 정한다 </summary>
        [HttpGet(GrowthRoutes.ACCOUNT_PROGRESS)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            PlayerAuthIdentity? player = AuthActors.GetPlayer(HttpContext);
            if (player == null)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status401Unauthorized,
                    ContractCodes.RejectReason.TOKEN_REVOKED, string.Empty, _timeProvider);
            }

            return Ok(await _progress.ReadAsync(player.AccountId, cancellationToken));
        }
    }
}
