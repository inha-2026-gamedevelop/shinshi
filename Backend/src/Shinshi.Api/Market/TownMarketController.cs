// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
using Shinshi.Data.Growth;
using Shinshi.Data.Market;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Market
{
    /// <summary> 마을 서버 전용 유저 경매장. 마을 역할이 아니면 효과 전에 401, 403이고, 쓰기는 그 캐릭터 계정의 접속 임대가 호출 마을에
    /// 있어야 한다. 기능 키 market이 꺼져 있으면 등록과 구매는 저장된 처음 결과만 돌려준다(차단 거절은 저장하지 않는다).
    /// 취소와 확인은 차단 중에도 허용한다(보관분 회수). 검색과 조회는 operations를 만들지 않는다 </summary>
    [ApiController]
    public sealed class TownMarketController : GameServerControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        // 차단 거절은 아무것도 확정하지 않는다
        private const long NO_REVISION = 0;

        private const int MAX_ID_LENGTH = 64;
        private const int MIN_SORT      = MarketCodes.Sort.UNIT_PRICE;
        private const int MAX_SORT      = MarketCodes.Sort.QUANTITY;

        private readonly PostgresMarketRepository _market;
        private readonly PostgresMarketQueries _queries;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<TownMarketController> _logger;
        private readonly ServiceFeatureGate? _features;

        /****************************************
        *                Methods
        ****************************************/

        public TownMarketController(IServerCallerAuthenticator callers, PostgresServerPoolRepository pool,
            PostgresMarketRepository market, PostgresMarketQueries queries, TimeProvider timeProvider,
            ILogger<TownMarketController> logger, ServiceFeatureGate? features = null)
            : base(callers, pool)
        {
            _market       = market;
            _queries      = queries;
            _timeProvider = timeProvider;
            _logger       = logger;
            _features     = features;
        }

        /// <summary> 200 MarketSearchResponse. 필터와 정렬 값이 범위를 벗어나면 400이다 </summary>
        [HttpPost(MarketRouteTemplates.SEARCH)]
        public async Task<IActionResult> Search([FromBody] MarketSearchRequest? request, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if ((request == null) || (request.Sort < MIN_SORT) || (request.Sort > MAX_SORT)
                || ((request.Order != MarketCodes.Order.ASCENDING) && (request.Order != MarketCodes.Order.DESCENDING))
                || (request.Page < 0) || (request.Page > PostgresMarketQueries.MAX_PAGE)
                || ((request.DefinitionIds != null) && ((request.DefinitionIds.Length > MarketLimits.MAX_SEARCH_DEFINITIONS)
                    || request.DefinitionIds.Any(id => !IsShortText(id)))))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return Ok(await _queries.SearchAsync(request, cancellationToken));
        }

        /// <summary> 200 MarketListingsResponse </summary>
        [HttpPost(MarketRouteTemplates.LISTINGS_QUERY)]
        public async Task<IActionResult> QueryListings([FromBody] MarketListingsQueryRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if ((request == null) || !IsShortText(request.DefinitionId) || (request.Page < 0)
                || (request.Page > PostgresMarketQueries.MAX_PAGE))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            return Ok(await _queries.ListingsAsync(request, cancellationToken));
        }

        /// <summary> 200 MarketOperationResponse(listingId는 등록 ID). 409는 거절이며 본문의 reasonCode가 사유다 </summary>
        [HttpPost(MarketRouteTemplates.LIST)]
        public Task<IActionResult> List(string characterId, [FromBody] MarketListRequest? request,
            CancellationToken cancellationToken)
        {
            Guid itemId = Guid.Empty;
            bool valid = (request != null) && IsShortText(request.AssetKind) && IsShortText(request.DefinitionId, true)
                && ((request.AssetKind != MarketCodes.ASSET_EQUIPMENT) || TryParseId(request.ItemId, out itemId));
            return RunAsync(ServiceFeatureKeys.MARKET, characterId, request?.OperationId, valid,
                (town, character, operation, replayOnly, token) => _market.ListAsync(town, character,
                    new MarketListCommand(operation, request!.AssetKind, request.DefinitionId ?? string.Empty,
                        request.AssetKind == MarketCodes.ASSET_EQUIPMENT ? itemId : null, request.Quantity,
                        request.UnitPrice), replayOnly, token), cancellationToken);
        }

        /// <summary> 200 MarketOperationResponse(tradeId, gross, fee, net). 409는 거절이며 본문의 reasonCode가 사유다 </summary>
        [HttpPost(MarketRouteTemplates.PURCHASE)]
        public Task<IActionResult> Purchase(string characterId, [FromBody] MarketBuyRequest? request,
            CancellationToken cancellationToken)
        {
            Guid listingId = Guid.Empty;
            bool valid = (request != null) && TryParseId(request.ListingId, out listingId);
            return RunAsync(ServiceFeatureKeys.MARKET, characterId, request?.OperationId, valid,
                (town, character, operation, replayOnly, token) => _market.BuyAsync(town, character, operation, listingId,
                    request!.Quantity, request.ExpectedUnitPrice, replayOnly, token), cancellationToken);
        }

        /// <summary> 취소는 기능 차단 중에도 실행한다(보관한 품목을 돌려받을 수 있어야 한다) </summary>
        [HttpPost(MarketRouteTemplates.CANCEL)]
        public Task<IActionResult> Cancel(string characterId, string listingId, [FromBody] MarketCancelRequest? request,
            CancellationToken cancellationToken)
        {
            Guid listing = Guid.Empty;
            bool valid = (request != null) && TryParseId(listingId, out listing);
            return RunAsync(null, characterId, request?.OperationId, valid,
                (town, character, operation, replayOnly, token) => _market.CancelAsync(town, character, operation, listing,
                    token), cancellationToken);
        }

        /// <summary> 판매자 결과 확인 표시. 경제 효과가 없는 단순 갱신이라 기능 키를 보지 않고 operations도 만들지 않는다 </summary>
        [HttpPost(MarketRouteTemplates.ACK)]
        public async Task<IActionResult> Ack(string characterId, [FromBody] MarketAckRequest? request,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }

            List<Guid> listings = new List<Guid>();
            bool valid = (request != null) && TryParseId(characterId, out _) && TryParseId(request.OperationId, out _)
                && TryParseIds(request.ListingIds, listings);
            if (!valid)
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            OperationResult result = await _market.AckAsync(caller.InstanceId, Guid.Parse(characterId),
                Guid.Parse(request!.OperationId), listings, cancellationToken);
            return new ObjectResult(new MarketOperationResponse(result, string.Empty, string.Empty, 0, 0, 0))
            {
                StatusCode = StatusOf(result)
            };
        }

        /// <summary> 200 MarketMineResponse. 캐릭터가 없으면 404, 계정 접속 임대가 이 마을에 없으면 409 WRONG_LOCATION이다 </summary>
        [HttpGet(MarketRouteTemplates.MINE)]
        public async Task<IActionResult> Mine(string characterId, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(characterId, out Guid character))
            {
                return BadRequest();
            }

            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            (EGrowthAccess access, MarketMineResponse? body) = await _queries.MineAsync(caller.InstanceId, character,
                cancellationToken);
            return ToReadResult(access, body);
        }

        /// <summary> 200 MarketHistoryResponse. page는 0부터다 </summary>
        [HttpGet(MarketRouteTemplates.HISTORY)]
        public async Task<IActionResult> History(string characterId,
            [FromQuery(Name = MarketRoutes.PAGE_PARAMETER)] int page, CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!TryParseId(characterId, out Guid character) || (page < 0) || (page > PostgresMarketQueries.MAX_PAGE))
            {
                return BadRequest();
            }

            Response.Headers.CacheControl = CacheControlValues.NO_STORE;
            (EGrowthAccess access, MarketHistoryResponse? body) = await _queries.HistoryAsync(caller.InstanceId, character,
                page, cancellationToken);
            return ToReadResult(access, body);
        }

        // 호출 서버, 형식, 기능 차단을 차례로 본 뒤 작업을 실행한다. 차단 중에는 저장된 처음 결과만 돌려주고 처음 보는 operationId는
        // 저장 없이 FEATURE_DISABLED로 거절한다. valid가 false면 400이며 작업으로 저장하지 않는다
        private async Task<IActionResult> RunAsync(string? featureKey, string characterId, string? operationId, bool valid,
            Func<Guid, Guid, Guid, bool, CancellationToken, Task<MarketOperationOutcome>> run,
            CancellationToken cancellationToken)
        {
            (ServerCaller? caller, IActionResult? rejection) = await RequireCallerAsync(ContractCodes.ServerRole.TOWN,
                cancellationToken);
            if (caller == null)
            {
                return rejection!;
            }
            if (!valid || !TryParseId(characterId, out Guid character) || !TryParseId(operationId, out Guid operation))
            {
                return BadRequest();
            }

            await TouchAsync(caller, cancellationToken);
            bool blocked = (featureKey != null) && (_features != null)
                && (await _features.CheckFeatureAsync(featureKey, cancellationToken) != ContractCodes.RejectReason.NONE);
            MarketOperationOutcome outcome = blocked
                ? await FindStoredWhileBlockedAsync(run, caller.InstanceId, character, operation, cancellationToken)
                : await run(caller.InstanceId, character, operation, false, cancellationToken);
            if (outcome.Result == null)
            {
                OperationResult refused = new OperationResult(operation.ToString("D"),
                    ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.FEATURE_DISABLED, NO_REVISION,
                    _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), string.Empty);
                return new ObjectResult(new MarketOperationResponse(refused, string.Empty, string.Empty, 0, 0, 0))
                {
                    StatusCode = StatusOf(refused)
                };
            }

            return new ObjectResult(new MarketOperationResponse(outcome.Result, outcome.ListingId, outcome.TradeId,
                outcome.Gross, outcome.Fee, outcome.Net))
            {
                StatusCode = StatusOf(outcome.Result)
            };
        }

        // 차단 판정은 이미 끝났으므로 저장 결과를 못 읽으면 차단 거절로 답한다. 효과는 없고 같은 ID로 다시 보내면 된다
        private async Task<MarketOperationOutcome> FindStoredWhileBlockedAsync(
            Func<Guid, Guid, Guid, bool, CancellationToken, Task<MarketOperationOutcome>> run, Guid town, Guid character,
            Guid operation, CancellationToken cancellationToken)
        {
            try
            {
                return await run(town, character, operation, true, cancellationToken);
            }
            catch (DbException exception)
            {
                // 예외 메시지에 연결 정보가 섞일 수 있어 형식 이름만 남긴다
                _logger.LogWarning("Stored market operation lookup failed while the feature is disabled ({ExceptionType}).",
                    exception.GetType().Name);
                return MarketOperationOutcome.Of(null);
            }
        }

        private IActionResult ToReadResult(EGrowthAccess access, object? body)
        {
            if (access == EGrowthAccess.NotFound)
            {
                return NotFound();
            }
            if (access == EGrowthAccess.WrongLocation)
            {
                return ApiResponseFactory.Reject(StatusCodes.Status409Conflict, ContractCodes.RejectReason.WRONG_LOCATION,
                    string.Empty, _timeProvider);
            }

            return Ok(body);
        }

        private static bool TryParseIds(string[]? values, List<Guid> ids)
        {
            if (values == null)
            {
                return true;
            }
            if (values.Length > PostgresMarketQueries.MAX_MINE_ROWS)
            {
                return false;
            }

            foreach (string value in values)
            {
                if (!TryParseId(value, out Guid id))
                {
                    return false;
                }
                ids.Add(id);
            }

            return true;
        }

        private static bool IsShortText(string? value, bool allowEmpty = false)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return allowEmpty && (value != null);
            }

            return value.Length <= MAX_ID_LENGTH;
        }
    }
}
