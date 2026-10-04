// System
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Life;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Life
{
    // 채집 판정(선점, 거리, 시간)은 마을 서버 몫이다. API는 호출 마을 서버가 그 계정의 살아 있는 접속 임대를 가졌는지,
    // 행동력과 재고, 보관 상한만 확인한다. 거절은 409와 OperationResult(조회는 ApiRejection), 형식 오류는 400이다
    /// <summary> 마을 서버 전용 생활 경로: 재고 조회, 채집 완료, 물약 제작 </summary>
    [ApiController]
    public sealed class LifeController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresLifeRepository _life;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<LifeController> _logger;
        private readonly ServiceFeatureGate? _features;

        /****************************************
        *                Methods
        ****************************************/

        // 어드민 모듈 없이 올린 테스트 호스트에서도 만들 수 있게 게이트는 선택 의존성이다
        public LifeController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresLifeRepository life, TimeProvider timeProvider, ILogger<LifeController> logger,
            ServiceFeatureGate? features = null)
            : base(callers, pool)
        {
            _life         = life;
            _timeProvider = timeProvider;
            _logger       = logger;
            _features     = features;
        }

        [HttpPost(LifeRoutes.SNAPSHOT)]
        public async Task<IActionResult> Snapshot([FromBody] LifeSnapshotRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if ((request == null) || !TryParseId(request.AccountId, out Guid accountId)
                || !TryParseId(request.AccountLeaseId, out Guid leaseId))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            LifeSnapshotOutcome outcome = await _life.ReadSnapshotAsync(caller.InstanceId, accountId, leaseId,
                cancellationToken);
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            if (outcome.Response == null)
            {
                return new ObjectResult(new ApiRejection(outcome.ReasonCode, string.Empty,
                    outcome.ServerTime.ToUnixTimeMilliseconds())) { StatusCode = StatusCodes.Status409Conflict };
            }

            return Ok(outcome.Response);
        }

        [HttpPost(LifeRoutes.GATHERS)]
        public async Task<IActionResult> Gather([FromBody] LifeGatherRequest? request, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if ((request == null) || !TryParseId(request.OperationId, out Guid operationId)
                || !TryParseId(request.AccountId, out Guid accountId)
                || !TryParseId(request.AccountLeaseId, out Guid leaseId)
                || !LifeIds.IsValid(request.ZoneId) || !LifeIds.IsValid(request.NodeId))
            {
                return BadRequest();
            }

            LifeGatherInput input = new LifeGatherInput
            {
                TownInstanceId = caller.InstanceId, OperationId = operationId, AccountId = accountId,
                AccountLeaseId = leaseId, ZoneId = request.ZoneId, NodeId = request.NodeId
            };
            await TouchAsync(caller, cancellationToken);
            IActionResult? blocked = await RejectIfDisabledAsync(ServiceFeatureKeys.GATHERING,
                token => _life.FindStoredGatherAsync(input, token), cancellationToken);
            if (blocked != null)
            {
                return blocked;
            }

            return ApiResponseFactory.FromOperation(await _life.GatherAsync(input, cancellationToken));
        }

        [HttpPost(LifeRoutes.POTION_CRAFTS)]
        public async Task<IActionResult> CraftPotion([FromBody] LifePotionCraftRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if ((request == null) || !TryParseId(request.OperationId, out Guid operationId)
                || !TryParseId(request.AccountId, out Guid accountId)
                || !TryParseId(request.AccountLeaseId, out Guid leaseId)
                || !LifeContent.TryGetPotionCost(request.PotionDefinitionId, out _) || (request.Count <= 0))
            {
                return BadRequest();
            }

            LifePotionCraftInput input = new LifePotionCraftInput
            {
                TownInstanceId = caller.InstanceId, OperationId = operationId, AccountId = accountId,
                AccountLeaseId = leaseId, PotionDefinitionId = request.PotionDefinitionId, Count = request.Count
            };
            await TouchAsync(caller, cancellationToken);
            IActionResult? blocked = await RejectIfDisabledAsync(ServiceFeatureKeys.CRAFTING,
                token => _life.FindStoredCraftAsync(input, token), cancellationToken);
            if (blocked != null)
            {
                return blocked;
            }

            return ApiResponseFactory.FromOperation(await _life.CraftPotionAsync(input, cancellationToken));
        }

        // 꺼진 기능이면 새 작업은 저장하지 않고 FEATURE_DISABLED로 거절한다. 같은 operationId 재전송은 처음 결과를 받는다.
        // 차단 거절을 저장하지 않으므로 차단이 풀린 뒤 같은 operationId로 다시 보내면 그때 실행된다
        private async Task<IActionResult?> RejectIfDisabledAsync(string featureKey,
            Func<CancellationToken, Task<OperationResult?>> findStored, CancellationToken cancellationToken)
        {
            if ((_features == null)
                || (await _features.CheckFeatureAsync(featureKey, cancellationToken) == ContractCodes.RejectReason.NONE))
            {
                return null;
            }

            OperationResult? stored = null;
            try
            {
                stored = await findStored(cancellationToken);
            }
            catch (DbException exception)
            {
                // 예외 메시지에 연결 정보가 섞일 수 있어 형식 이름만 남긴다
                _logger.LogWarning("Stored life operation lookup failed while {Feature} is disabled ({ExceptionType}).",
                    featureKey, exception.GetType().Name);
            }

            return stored == null
                ? ApiResponseFactory.Reject(StatusCodes.Status409Conflict, ContractCodes.RejectReason.FEATURE_DISABLED,
                    string.Empty, _timeProvider)
                : ApiResponseFactory.FromOperation(stored);
        }
    }
}
