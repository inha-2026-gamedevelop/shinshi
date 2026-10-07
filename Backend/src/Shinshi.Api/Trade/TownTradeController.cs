// System
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Sessions;
using Shinshi.Data.Trade;

namespace Shinshi.Backend.Trade
{
    /// <summary> 마을 서버 전용 1:1 거래 실행(prepare, commit, resolve). 멱등 actor는 호출한 마을 서버 인스턴스다.
    /// 마을 역할이 아니면 효과 전에 401, 403이다. 기능 키 trade가 꺼져 있으면 prepare와 commit은 저장된 처음 결과만 돌려주고
    /// (차단 거절은 저장하지 않는다) resolve는 읽기와 종결이라 막지 않는다. 응답 본문은 TradeExecutionResponse다 </summary>
    [ApiController]
    public sealed class TownTradeController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // 차단 거절은 아무것도 확정하지 않는다
        private const long NO_REVISION = 0;

        private readonly PostgresTradeRepository _trades;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<TownTradeController> _logger;
        private readonly ServiceFeatureGate? _features;

        /****************************************
        *                Methods
        ****************************************/

        public TownTradeController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresTradeRepository trades, TimeProvider timeProvider, ILogger<TownTradeController> logger,
            ServiceFeatureGate? features = null)
            : base(callers, pool)
        {
            _trades       = trades;
            _timeProvider = timeProvider;
            _logger       = logger;
            _features     = features;
        }

        /// <summary> 200 Prepared 행 생성(결과는 Committed, execution.state는 Prepared), 409 거절(TradeBusy, TradeNotInTown 등).
        /// 본문의 townInstanceId가 호출 인스턴스와 다르거나 형식이 틀리면 400이다 </summary>
        [HttpPost(TradeRouteTemplates.PREPARE)]
        public async Task<IActionResult> Prepare([FromBody] TradePrepareRequest? request, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TradePrepareInput.TryParse(request, out TradePrepareInput? input) || (input!.TownInstanceId != caller.InstanceId))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            return await RunGatedAsync(input.ExecutionId, (replayOnly, token) => _trades.PrepareAsync(caller.InstanceId, input,
                replayOnly, token), cancellationToken);
        }

        /// <summary> 본문의 operationId가 prepare가 저장한 commitOperationId와 다르면 409 INVALID_TARGET이다(저장하지 않는다) </summary>
        [HttpPost(TradeRouteTemplates.COMMIT)]
        public async Task<IActionResult> Commit(string executionId, [FromBody] TradeCommitRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(executionId, out Guid execution) || (request == null)
                || !TryParseId(request.OperationId, out Guid operation))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            return await RunGatedAsync(operation, (replayOnly, token) => _trades.CommitAsync(caller.InstanceId, execution,
                operation, replayOnly, token), cancellationToken);
        }

        /// <summary> 200 현재 실행 상태. 저장된 commit 결과가 있으면 그 결과로 행을 종결하고, 만료됐으면 Abandoned로 끝낸다 </summary>
        [HttpPost(TradeRouteTemplates.RESOLVE)]
        public async Task<IActionResult> Resolve(string executionId, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(executionId, out Guid execution))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            return ToResult(await _trades.ResolveAsync(caller.InstanceId, execution, cancellationToken));
        }

        // 기능 차단 중에는 저장된 처음 결과만 돌려주고 처음 보는 operationId는 저장 없이 FEATURE_DISABLED로 거절한다
        private async Task<IActionResult> RunGatedAsync(Guid operationId, Func<bool, CancellationToken, Task<TradeOutcome>> run,
            CancellationToken cancellationToken)
        {
            bool blocked = (_features != null)
                && (await _features.CheckFeatureAsync(ServiceFeatureKeys.TRADE, cancellationToken)
                    != ContractCodes.RejectReason.NONE);
            TradeOutcome outcome;
            if (blocked)
            {
                try
                {
                    outcome = await run(true, cancellationToken);
                }
                catch (DbException exception)
                {
                    // 예외 메시지에 연결 정보가 섞일 수 있어 형식 이름만 남긴다
                    _logger.LogWarning("Stored trade operation lookup failed while the feature is disabled ({ExceptionType}).",
                        exception.GetType().Name);
                    outcome = new TradeOutcome(ETradeLookup.Ok, null, null);
                }
            }
            else
            {
                outcome = await run(false, cancellationToken);
            }

            if ((outcome.Lookup == ETradeLookup.Ok) && (outcome.Result == null))
            {
                OperationResult refused = new OperationResult(operationId.ToString("D"),
                    ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.FEATURE_DISABLED, NO_REVISION,
                    _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), string.Empty);
                return new ObjectResult(new TradeExecutionResponse(refused, null)) { StatusCode = StatusOf(refused) };
            }

            return ToResult(outcome);
        }

        private IActionResult ToResult(TradeOutcome outcome)
        {
            if (outcome.Lookup == ETradeLookup.NotFound)
            {
                return NotFound();
            }
            if (outcome.Lookup == ETradeLookup.OperationMismatch)
            {
                OperationResult mismatch = new OperationResult(string.Empty, ContractCodes.OperationStatus.REJECTED,
                    ContractCodes.RejectReason.INVALID_TARGET, NO_REVISION, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                    string.Empty);
                return new ObjectResult(new TradeExecutionResponse(mismatch, outcome.Execution))
                {
                    StatusCode = StatusOf(mismatch)
                };
            }

            OperationResult result = outcome.Result!;
            return new ObjectResult(new TradeExecutionResponse(result, outcome.Execution)) { StatusCode = StatusOf(result) };
        }
    }
}
