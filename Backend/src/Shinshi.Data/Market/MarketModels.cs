// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Market
{
    /// <summary> 경매장 쓰기의 멱등 작업 종류와 원장 값 </summary>
    public static class MarketOperationKinds
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string LIST   = "market.list";
        public const string BUY    = "market.buy";
        public const string CANCEL = "market.cancel";
        public const string EXPIRE = "market.expire";

        // 원장 source_type. source_id는 등록 ID(구매는 성사 ID)다
        public const string LEDGER_SOURCE_TYPE = "market";

        public const string ACTOR_PLAYER = "Player";
        public const string ACTOR_SYSTEM = "System";

        // 만료 유지 작업이 쓰는 고정 actor와 operationId 이름. 같은 등록은 항상 같은 operationId라 두 번 반환되지 않는다
        private const string SYSTEM_ACTOR_NAME   = "shinshi:market:system";
        private const string EXPIRE_NAME_PREFIX  = "shinshi:market:expire:";

        /****************************************
        *                Methods
        ****************************************/

        public static Guid SystemActorId()
        {
            return Sessions.SessionDb.CreateNameBasedGuid(SYSTEM_ACTOR_NAME);
        }

        public static Guid ExpireOperationId(Guid listingId)
        {
            return Sessions.SessionDb.CreateNameBasedGuid(EXPIRE_NAME_PREFIX + listingId.ToString("D"));
        }
    }

    /// <summary> 등록 요청 하나. 장비는 ItemId, 수량형은 Quantity를 쓴다 </summary>
    public sealed class MarketListCommand
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   OperationId  { get; }
        public string AssetKind    { get; }
        public string DefinitionId { get; }
        public Guid?  ItemId       { get; }
        public int    Quantity     { get; }
        public long   UnitPrice    { get; }

        /****************************************
        *                Methods
        ****************************************/

        public MarketListCommand(Guid operationId, string assetKind, string definitionId, Guid? itemId, int quantity,
            long unitPrice)
        {
            if (operationId == Guid.Empty)
            {
                throw new ArgumentException("Operation ID is required.", nameof(operationId));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(assetKind);
            ArgumentNullException.ThrowIfNull(definitionId);

            OperationId  = operationId;
            AssetKind    = assetKind;
            DefinitionId = definitionId;
            ItemId       = itemId;
            Quantity     = quantity;
            UnitPrice    = unitPrice;
        }
    }

    /// <summary> 경매장 쓰기의 저장된 결과. Result가 null이면 기능 차단 중 처음 보는 operationId다 </summary>
    public sealed class MarketOperationOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public OperationResult? Result    { get; }

        // 확정되지 않았으면 빈 문자열, 0
        public string           ListingId { get; }
        public string           TradeId   { get; }
        public long             Gross     { get; }
        public long             Fee       { get; }
        public long             Net       { get; }

        /****************************************
        *                Methods
        ****************************************/

        public MarketOperationOutcome(OperationResult? result, string listingId, string tradeId, long gross, long fee,
            long net)
        {
            Result    = result;
            ListingId = listingId;
            TradeId   = tradeId;
            Gross     = gross;
            Fee       = fee;
            Net       = net;
        }

        public static MarketOperationOutcome Of(OperationResult? result)
        {
            return new MarketOperationOutcome(result, string.Empty, string.Empty, 0, 0, 0);
        }
    }
}
