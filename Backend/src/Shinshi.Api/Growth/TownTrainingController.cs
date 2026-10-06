// System
using System;
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
using Shinshi.Data.Growth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Growth
{
    /// <summary> 마을 서버의 정기 수련 투자와 재분배. 기능 키 training으로 막는다 </summary>
    [ApiController]
    public sealed class TownTrainingController : TownGrowthControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresTrainingRepository _training;

        /****************************************
        *                Methods
        ****************************************/

        public TownTrainingController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresTrainingRepository training, TimeProvider timeProvider, ILogger<TownTrainingController> logger,
            ServiceFeatureGate? features = null)
            : base(callers, pool, timeProvider, logger, features)
        {
            _training = training;
        }

        // 상한 판정은 저장소가 콘텐츠 값으로 하고, 여기서는 정수 범위만 본다
        [HttpPost(GrowthRouteTemplates.TRAINING)]
        public Task<IActionResult> Train(string characterId, [FromBody] TrainingRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && (request.Stat >= 0) && (request.Stat < GrowthContent.TRAINING_STAT_COUNT)
                && (request.Steps > 0);
            return RunAsync(ServiceFeatureKeys.TRAINING, characterId, request?.OperationId, request?.ExpectedRevision ?? 0,
                valid, (town, character, operation, replayOnly, token) => _training.TrainAsync(town, operation, character,
                    request!.ExpectedRevision, request.Stat, request.Steps, replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.TRAINING_RESPEC)]
        public Task<IActionResult> Respec(string characterId, [FromBody] TrainingRespecRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && (request.Allocation != null)
                && (request.Allocation.Length == GrowthContent.TRAINING_STAT_COUNT);
            return RunAsync(ServiceFeatureKeys.TRAINING, characterId, request?.OperationId, request?.ExpectedRevision ?? 0,
                valid, (town, character, operation, replayOnly, token) => _training.RespecAsync(town, operation, character,
                    request!.ExpectedRevision, request.Allocation!, replayOnly, token),
                cancellationToken);
        }
    }
}
