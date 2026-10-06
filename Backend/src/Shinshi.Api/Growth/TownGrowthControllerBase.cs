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
using Shinshi.Data.Growth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Growth
{
    /// <summary> 마을 서버 전용 성장 쓰기의 공통 흐름. 마을 역할이 아니면 효과 전에 401, 403이고, 기능이 꺼져 있으면
    /// 저장된 처음 결과만 돌려준다(차단 거절 자체는 저장하지 않으므로 풀린 뒤 같은 operationId로 다시 보내면 실행된다) </summary>
    public abstract class TownGrowthControllerBase : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // 차단 거절은 아무것도 확정하지 않는다
        private const long NO_REVISION = 0;

        protected const int MAX_ID_LENGTH = 64;

        private readonly TimeProvider _timeProvider;
        private readonly ServiceFeatureGate? _features;
        private readonly ILogger _logger;

        /****************************************
        *                Methods
        ****************************************/

        protected TownGrowthControllerBase(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            TimeProvider timeProvider, ILogger logger, ServiceFeatureGate? features)
            : base(callers, pool)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(logger);
            _timeProvider = timeProvider;
            _logger       = logger;
            _features     = features;
        }

        /// <summary> 호출 서버, 형식, 기능 차단을 차례로 본 뒤 작업을 실행한다. valid가 false면 400이며 작업으로 저장하지 않는다 </summary>
        protected async Task<IActionResult> RunAsync(string? featureKey, string characterId, string? operationId,
            long expectedRevision, bool valid,
            Func<Guid, Guid, Guid, bool, CancellationToken, Task<GrowthOperationOutcome>> run,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!valid || !TryParseId(characterId, out Guid character) || !TryParseId(operationId, out Guid operation)
                || (expectedRevision < 0))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            bool blocked = (featureKey != null) && (_features != null)
                && (await _features.CheckFeatureAsync(featureKey, cancellationToken) != ContractCodes.RejectReason.NONE);
            GrowthOperationOutcome outcome = blocked
                ? await FindStoredWhileBlockedAsync(run, caller.InstanceId, character, operation, cancellationToken)
                : await run(caller.InstanceId, character, operation, false, cancellationToken);
            if (outcome.Result == null)
            {
                OperationResult refused = new OperationResult(operation.ToString("D"),
                    ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.FEATURE_DISABLED, NO_REVISION,
                    _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), string.Empty);
                return new ObjectResult(new GrowthOperationResponse(refused, GrowthCodes.Outcome.NONE, null, null))
                {
                    StatusCode = StatusOf(refused)
                };
            }

            GrowthOperationResponse body = new GrowthOperationResponse(outcome.Result, outcome.Outcome,
                outcome.ItemId?.ToString("D"), outcome.Growth);
            return new ObjectResult(body) { StatusCode = StatusOf(outcome.Result) };
        }

        protected static bool IsShortText(string? value)
        {
            return !string.IsNullOrWhiteSpace(value) && (value.Length <= MAX_ID_LENGTH);
        }

        // 차단 판정은 이미 끝났으므로 저장 결과를 못 읽으면 차단 거절로 답한다. 효과는 없고 같은 ID로 다시 보내면 된다
        private async Task<GrowthOperationOutcome> FindStoredWhileBlockedAsync(
            Func<Guid, Guid, Guid, bool, CancellationToken, Task<GrowthOperationOutcome>> run, Guid town, Guid character,
            Guid operation, CancellationToken cancellationToken)
        {
            try
            {
                return await run(town, character, operation, true, cancellationToken);
            }
            catch (DbException exception)
            {
                // 예외 메시지에 연결 정보가 섞일 수 있어 형식 이름만 남긴다
                _logger.LogWarning("Stored growth operation lookup failed while the feature is disabled ({ExceptionType}).",
                    exception.GetType().Name);
                return new GrowthOperationOutcome(null, GrowthCodes.Outcome.NONE, null, null);
            }
        }
    }
}
