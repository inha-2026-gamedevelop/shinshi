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
    /// <summary> 마을 서버의 장비 제작(crafting), 강화(enhance), 계승(inherit), 착용, 해제, 잠금, 분해.
    /// 착용, 해제, 잠금, 분해는 기능 키가 없어 막지 않는다 </summary>
    [ApiController]
    public sealed class TownEquipmentController : TownGrowthControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresEquipmentRepository _equipment;

        /****************************************
        *                Methods
        ****************************************/

        public TownEquipmentController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresEquipmentRepository equipment, TimeProvider timeProvider, ILogger<TownEquipmentController> logger,
            ServiceFeatureGate? features = null)
            : base(callers, pool, timeProvider, logger, features)
        {
            _equipment = equipment;
        }

        [HttpPost(GrowthRouteTemplates.EQUIPMENT_CRAFT)]
        public Task<IActionResult> Craft(string characterId, [FromBody] EquipmentCraftRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && IsShortText(request.DefinitionId);
            return RunAsync(ServiceFeatureKeys.CRAFTING, characterId, request?.OperationId, request?.ExpectedRevision ?? 0,
                valid, (town, character, operation, replayOnly, token) => _equipment.CraftAsync(town, operation, character,
                    request!.ExpectedRevision, request.DefinitionId, replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.EQUIPMENT_INHERIT)]
        public Task<IActionResult> Inherit(string characterId, [FromBody] EquipmentInheritRequest? request,
            CancellationToken cancellationToken)
        {
            Guid source = Guid.Empty;
            Guid target = Guid.Empty;
            bool valid = (request != null) && TryParseId(request.SourceItemId, out source)
                && TryParseId(request.TargetItemId, out target);
            return RunAsync(ServiceFeatureKeys.INHERIT, characterId, request?.OperationId, request?.ExpectedRevision ?? 0,
                valid, (town, character, operation, replayOnly, token) => _equipment.InheritAsync(town, operation, character,
                    request!.ExpectedRevision, source, target, replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.EQUIPMENT_ENHANCE)]
        public Task<IActionResult> Enhance(string characterId, string itemId, [FromBody] EquipmentEnhanceRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && TryParseId(itemId, out Guid item)
                && EnhancementRules.IsValidRoll(request.RollBasisPoints);
            return RunAsync(ServiceFeatureKeys.ENHANCE, characterId, request?.OperationId, request?.ExpectedRevision ?? 0,
                valid, (town, character, operation, replayOnly, token) => _equipment.EnhanceAsync(town, operation, character,
                    request!.ExpectedRevision, Guid.Parse(itemId), request.RollBasisPoints, replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.EQUIPMENT_EQUIP)]
        public Task<IActionResult> Equip(string characterId, string itemId, [FromBody] EquipmentActionRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && TryParseId(itemId, out _);
            return RunAsync(null, characterId, request?.OperationId, request?.ExpectedRevision ?? 0, valid,
                (town, character, operation, replayOnly, token) => _equipment.EquipAsync(town, operation, character,
                    request!.ExpectedRevision, Guid.Parse(itemId), replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.EQUIPMENT_UNEQUIP)]
        public Task<IActionResult> Unequip(string characterId, string itemId, [FromBody] EquipmentActionRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && TryParseId(itemId, out _);
            return RunAsync(null, characterId, request?.OperationId, request?.ExpectedRevision ?? 0, valid,
                (town, character, operation, replayOnly, token) => _equipment.UnequipAsync(town, operation, character,
                    request!.ExpectedRevision, Guid.Parse(itemId), replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.EQUIPMENT_LOCK)]
        public Task<IActionResult> Lock(string characterId, string itemId, [FromBody] EquipmentLockRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && TryParseId(itemId, out _);
            return RunAsync(null, characterId, request?.OperationId, request?.ExpectedRevision ?? 0, valid,
                (town, character, operation, replayOnly, token) => _equipment.LockAsync(town, operation, character,
                    request!.ExpectedRevision, Guid.Parse(itemId), request.Locked, replayOnly, token),
                cancellationToken);
        }

        [HttpPost(GrowthRouteTemplates.EQUIPMENT_DISMANTLE)]
        public Task<IActionResult> Dismantle(string characterId, string itemId, [FromBody] EquipmentActionRequest? request,
            CancellationToken cancellationToken)
        {
            bool valid = (request != null) && TryParseId(itemId, out _);
            return RunAsync(null, characterId, request?.OperationId, request?.ExpectedRevision ?? 0, valid,
                (town, character, operation, replayOnly, token) => _equipment.DismantleAsync(town, operation, character,
                    request!.ExpectedRevision, Guid.Parse(itemId), replayOnly, token),
                cancellationToken);
        }
    }
}
