// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 게임 서버 전용 경로의 공통 가드. 서버 자격이 없으면 401, 다른 역할이면 403으로 효과 전에 막는다 </summary>
    [RequireServerLease]
    public abstract class GameServerControllerBase : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IServerCallerAuthenticator _callers;
        private readonly PostgresServerPoolRepository _pool;

        /****************************************
        *                Methods
        ****************************************/

        protected GameServerControllerBase(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool)
        {
            ArgumentNullException.ThrowIfNull(callers);
            ArgumentNullException.ThrowIfNull(pool);
            _callers = callers;
            _pool    = pool;
        }

        /// <summary> 효과 경로 호출 자체를 호출 boot의 생존 증거로 남긴다. 효과 트랜잭션 전에 짧은 문장으로 heartbeat 시각을 갱신해
        /// 응답이 늦은 heartbeat 때문에 정상 효과가 생존 판정에 걸리지 않게 한다. boot가 없는 이전 임대는 건너뛴다 </summary>
        protected Task TouchAsync(ServerCaller caller, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return caller.BootId.HasValue
                ? _pool.TouchAsync(caller.InstanceId, caller.BootId.Value, cancellationToken)
                : Task.CompletedTask;
        }

        /// <summary> 요구 역할의 호출 서버를 확인한다. 실패하면 rejection에 응답이 담기고 null을 돌려준다 </summary>
        protected async Task<(ServerCaller? Caller, IActionResult? Rejection)> RequireCallerAsync(int role,
            CancellationToken cancellationToken)
        {
            ServerCaller? caller = await _callers.AuthenticateAsync(HttpContext, cancellationToken);
            if (caller == null)
            {
                return (null, StatusCode(StatusCodes.Status401Unauthorized));
            }
            if (caller.Role != role)
            {
                return (null, StatusCode(StatusCodes.Status403Forbidden));
            }

            return (caller, null);
        }

        /// <summary> 빈 GUID를 형식 오류로 본다 </summary>
        protected static bool TryParseId(string? value, out Guid id)
        {
            return Guid.TryParse(value, out id) && (id != Guid.Empty);
        }

        /// <summary> 공통 작업 응답의 HTTP 상태를 다른 응답 본문에도 같게 쓴다 </summary>
        protected static int StatusOf(OperationResult result)
        {
            return ApiResponseFactory.FromOperation(result).StatusCode ?? StatusCodes.Status200OK;
        }
    }
}
