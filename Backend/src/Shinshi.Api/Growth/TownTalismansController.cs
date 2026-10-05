// System
using System;
using System.Linq;
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
    /// <summary> 마을 서버의 부적 등록, 확정 강화, 조합 교체, 카드 분해. 기능 키 talisman으로 막는다 </summary>
    [ApiController]
    public sealed class TownTalismansController : TownGrowthControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // 한 번에 분해하는 카드 수 상한. 미등록 부적 보관 상한과 같다
        private const int MAX_DISASSEMBLE_QUANTITY = 999;

        private readonly PostgresTalismanRepository _talismans;

        /****************************************
        *                Methods
        ****************************************/

        public TownTalismansController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresTalismanRepository talismans, TimeProvider timeProvider, ILogger<TownTalismansController> logger,
            ServiceFeatureGate? features = null)
            : base(callers, pool, timeProvider, logger, features)
        {
            _talismans = talismans;
        }

        [HttpPost(GrowthRouteTemplates.TALISMAN_REGISTER)]
        public Task<IActionResult> Register(string characterId, [FromBody] TalismanRegisterRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && IsShortText(request.EffectId)
                && EnhancementRules.IsValidRoll(request.RollBasisPoints);
            return RunAsync(ServiceFeatureKeys.TALISMAN, characterId, request?.OperationId, request?.ExpectedRevision ?? 0,
                valid, (town, character, operation, replayOnly, token) => _talismans.RegisterAsync(town, operation, character,
                    request!.ExpectedRevision, request.EffectId, request.RollBasisPoints, replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.TALISMAN_UPGRADE)]
        public Task<IActionResult> Upgrade(string characterId, [FromBody] TalismanUpgradeRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && IsShortText(request.EffectId);
            return RunAsync(ServiceFeatureKeys.TALISMAN, characterId, request?.OperationId, request?.ExpectedRevision ?? 0,
                valid, (town, character, operation, replayOnly, token) => _talismans.UpgradeAsync(town, operation, character,
                    request!.ExpectedRevision, request.EffectId, replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.TALISMAN_LOADOUT)]
        public Task<IActionResult> Loadout(string characterId, [FromBody] TalismanLoadoutRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && (request.Entries != null)
                && (request.Entries.Length <= GrowthCodes.MAX_LOADOUT_ENTRIES)
                && request.Entries.All(entry => (entry != null) && IsShortText(entry.SkillId) && IsShortText(entry.EffectId)
                    && (entry.Tier >= 0) && (entry.Tier < GrowthContent.TIER_COUNT));
            return RunAsync(ServiceFeatureKeys.TALISMAN, characterId, request?.OperationId, request?.ExpectedRevision ?? 0,
                valid, (town, character, operation, replayOnly, token) => _talismans.ReplaceLoadoutAsync(town, operation,
                    character, request!.ExpectedRevision, request.Entries!, replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.TALISMAN_DISASSEMBLE)]
        public Task<IActionResult> Disassemble(string characterId, [FromBody] TalismanDisassembleRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && IsShortText(request.EffectId) && (request.Quantity > 0)
                && (request.Quantity <= MAX_DISASSEMBLE_QUANTITY);
            return RunAsync(ServiceFeatureKeys.TALISMAN, characterId, request?.OperationId, request?.ExpectedRevision ?? 0,
                valid, (town, character, operation, replayOnly, token) => _talismans.DisassembleAsync(town, operation,
                    character, request!.ExpectedRevision, request.EffectId, request.Quantity, replayOnly, token),
                cancellationToken);
        }
    }
}
