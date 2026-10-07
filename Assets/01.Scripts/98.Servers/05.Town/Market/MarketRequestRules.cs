// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 클라이언트 경매장 요청을 API 호출로 옮기는 순수 계산, 형식이 틀리면 InvalidTarget이나 가격 범위 거절 </summary>
    public static class MarketRequestRules
    {
        // 대상 캐릭터는 서버가 인증된 연결로 정한다. 클라이언트 메시지에는 계정, 캐릭터 자리가 없다.
        // 품목 이름은 서버가 검색하지 않는다. 클라이언트가 정의 ID 목록을 만들어 보내므로 길이와 개수만 막는다.
        // 쓰기의 operationId는 요청 ID다. 같은 요청 ID를 다시 보내면 API가 처음 결과를 돌려준다

        /****************************************
        *                Fields
        ****************************************/

        private const string GUID_FORMAT        = "D";
        private const int    MAX_ID_LENGTH      = 64;
        private const int    MAX_PAGE           = MarketLimits.MAX_PAGE;
        private const int    MAX_CLASS_ID       = 2;
        private const int    MAX_GRADE          = 3;
        private const string QUERY_START        = "?";
        private const string QUERY_ASSIGN       = "=";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 요청을 API 호출로 바꾼다, 보낼 수 없으면 거절 사유 </summary>
        public static ERejectReason TryBuild(in MarketRequestMessage message, string characterId, out BackendRequest request)
        {
            request = null;
            if (string.IsNullOrEmpty(characterId))
            {
                return ERejectReason.SessionMismatch;
            }

            if ((message.IsWrite) && (message.RequestId == Guid.Empty))
            {
                return ERejectReason.InvalidTarget;
            }

            string operationId = message.RequestId.ToString(GUID_FORMAT);
            float  timeout     = Constants.Network.API_TIMEOUT_SECONDS;
            switch (message.Action)
            {
                case EMarketAction.Search:
                    return BuildSearch(in message, timeout, out request);
                case EMarketAction.Listings:
                    if ((!IsShortId(message.DefinitionId)) || (!IsPage(message.Page)))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    request = BackendRequest.Post(MarketRoutes.LISTINGS_QUERY,
                        new MarketListingsQueryRequest(message.DefinitionId, message.Page), typeof(MarketListingsResponse), null, timeout);
                    return ERejectReason.None;
                case EMarketAction.Mine:
                    request = BackendRequest.Get(Format(MarketRoutes.MINE_FORMAT, characterId), typeof(MarketMineResponse), null, timeout);
                    return ERejectReason.None;
                case EMarketAction.History:
                    if (!IsPage(message.Page))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    string historyPath = Format(MarketRoutes.HISTORY_FORMAT, characterId) + QUERY_START + MarketRoutes.PAGE_PARAMETER
                        + QUERY_ASSIGN + message.Page.ToString(CultureInfo.InvariantCulture);
                    request = BackendRequest.Get(historyPath, typeof(MarketHistoryResponse), null, timeout);
                    return ERejectReason.None;
                case EMarketAction.List:
                    return BuildList(in message, characterId, operationId, timeout, out request);
                case EMarketAction.Buy:
                    if (!IsGuid(message.ListingId))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    if ((!MarketTradeRules.IsQuantityInRange(message.Quantity)) || (!MarketTradeRules.IsUnitPriceInRange(message.UnitPrice)))
                    {
                        return ERejectReason.MarketPriceOutOfRange;
                    }

                    request = BackendRequest.Post(Format(MarketRoutes.PURCHASE_FORMAT, characterId),
                        new MarketBuyRequest(operationId, NormalizeGuid(message.ListingId), message.Quantity, message.UnitPrice),
                        typeof(MarketOperationResponse), operationId, timeout);
                    return ERejectReason.None;
                case EMarketAction.Cancel:
                    if (!IsGuid(message.ListingId))
                    {
                        return ERejectReason.InvalidTarget;
                    }

                    string listingId = NormalizeGuid(message.ListingId);
                    request = BackendRequest.Post(string.Format(CultureInfo.InvariantCulture, MarketRoutes.CANCEL_FORMAT, characterId, listingId),
                        new MarketCancelRequest(operationId), typeof(MarketOperationResponse), operationId, timeout);
                    return ERejectReason.None;
                case EMarketAction.Ack:
                    return BuildAck(in message, characterId, operationId, timeout, out request);
                default:
                    return ERejectReason.InvalidTarget;
            }
        }

        private static ERejectReason BuildSearch(in MarketRequestMessage message, float timeout, out BackendRequest request)
        {
            request = null;
            bool validCategory = (message.Category == MarketRequestMessage.NONE)
                || ((message.Category >= MarketCodes.Category.HERB) && (message.Category <= MarketCodes.Category.EQUIPMENT));
            bool validClass = (message.ClassId == MarketRequestMessage.NONE) || ((message.ClassId >= 0) && (message.ClassId <= MAX_CLASS_ID));
            bool validGrade = (message.Grade == MarketRequestMessage.NONE) || ((message.Grade >= 0) && (message.Grade <= MAX_GRADE));
            bool validSort  = (message.Sort >= MarketCodes.Sort.UNIT_PRICE) && (message.Sort <= MarketCodes.Sort.QUANTITY);
            bool validOrder = (message.Order == MarketCodes.Order.ASCENDING) || (message.Order == MarketCodes.Order.DESCENDING);
            if ((!validCategory) || (!validClass) || (!validGrade) || (!validSort) || (!validOrder) || (!IsPage(message.Page)))
            {
                return ERejectReason.InvalidTarget;
            }

            string[] ids = message.DefinitionIds;
            if (ids.Length > MarketLimits.MAX_SEARCH_DEFINITIONS)
            {
                return ERejectReason.InvalidTarget;
            }

            for (int i = 0; i < ids.Length; ++i)
            {
                if (!IsShortId(ids[i]))
                {
                    return ERejectReason.InvalidTarget;
                }
            }

            int? category = (message.Category == MarketRequestMessage.NONE) ? (int?)null : message.Category;
            int? classId  = (message.ClassId == MarketRequestMessage.NONE) ? (int?)null : message.ClassId;
            int? grade    = (message.Grade == MarketRequestMessage.NONE) ? (int?)null : message.Grade;
            string[] definitionIds = (ids.Length == 0) ? null : ids;
            request = BackendRequest.Post(MarketRoutes.SEARCH,
                new MarketSearchRequest(category, classId, grade, definitionIds, message.Sort, message.Order, message.Page),
                typeof(MarketSearchResponse), null, timeout);
            return ERejectReason.None;
        }

        private static ERejectReason BuildList(in MarketRequestMessage message, string characterId, string operationId, float timeout,
            out BackendRequest request)
        {
            request = null;
            string itemId;
            if (message.AssetKind == MarketCodes.ASSET_EQUIPMENT)
            {
                if ((!IsGuid(message.ItemId)) || (message.Quantity != 1))
                {
                    return ERejectReason.InvalidTarget;
                }

                itemId = NormalizeGuid(message.ItemId);
            }
            else if (message.AssetKind == MarketCodes.ASSET_ACCOUNT_ITEM)
            {
                if ((!IsShortId(message.DefinitionId)) || (!string.IsNullOrEmpty(message.ItemId)))
                {
                    return ERejectReason.InvalidTarget;
                }

                if (!MarketTradeRules.IsTradableAccountItem(message.DefinitionId))
                {
                    return ERejectReason.MarketNotTradable;
                }

                itemId = null;
            }
            else
            {
                return ERejectReason.InvalidTarget;
            }

            if ((!MarketTradeRules.IsQuantityInRange(message.Quantity)) || (!MarketTradeRules.IsUnitPriceInRange(message.UnitPrice)))
            {
                return ERejectReason.MarketPriceOutOfRange;
            }

            request = BackendRequest.Post(Format(MarketRoutes.LIST_FORMAT, characterId),
                new MarketListRequest(operationId, message.AssetKind, message.DefinitionId ?? string.Empty, itemId, message.Quantity, message.UnitPrice),
                typeof(MarketOperationResponse), operationId, timeout);
            return ERejectReason.None;
        }

        private static ERejectReason BuildAck(in MarketRequestMessage message, string characterId, string operationId, float timeout,
            out BackendRequest request)
        {
            request = null;
            string[] source = message.ListingIds;
            if ((source.Length == 0) || (source.Length > MarketMessageSerializer.MAX_STRINGS))
            {
                return ERejectReason.InvalidTarget;
            }

            var ids = new string[source.Length];
            for (int i = 0; i < source.Length; ++i)
            {
                if (!IsGuid(source[i]))
                {
                    return ERejectReason.InvalidTarget;
                }

                ids[i] = NormalizeGuid(source[i]);
            }

            request = BackendRequest.Post(Format(MarketRoutes.ACK_FORMAT, characterId), new MarketAckRequest(operationId, ids),
                typeof(MarketOperationResponse), operationId, timeout);
            return ERejectReason.None;
        }

        private static string Format(string format, string characterId)
        {
            return string.Format(CultureInfo.InvariantCulture, format, characterId);
        }

        private static bool IsPage(int page)
        {
            return (page >= 0) && (page <= MAX_PAGE);
        }

        private static bool IsShortId(string value)
        {
            return (!string.IsNullOrEmpty(value)) && (value.Length <= MAX_ID_LENGTH);
        }

        private static bool IsGuid(string value)
        {
            return (!string.IsNullOrEmpty(value)) && (Guid.TryParse(value, out _));
        }

        // 경로와 본문에 들어가는 ID는 하이픈 형식으로 다시 쓴다
        private static string NormalizeGuid(string value)
        {
            return Guid.Parse(value).ToString(GUID_FORMAT);
        }
    }
}
