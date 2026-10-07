// System
using System;
using System.Collections.Generic;
using System.Linq;

// Third Party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Trade
{
    /// <summary> 1:1 거래 멱등 작업 종류와 원장 값. 실행 작업의 actor는 요청한 마을 서버 인스턴스다 </summary>
    public static class TradeOperationKinds
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PREPARE = "trade.prepare";
        public const string COMMIT  = "trade.commit";

        public const string LEDGER_SOURCE_TYPE = "trade";
        public const string ACTOR_SERVER       = "Server";
    }

    /// <summary> 실행 행 조회와 요청 검증의 결과 구분 </summary>
    public enum ETradeLookup
    {
        Ok,

        // 실행 행이 없거나 다른 마을 인스턴스의 것이다. 존재 여부를 알리지 않는다
        NotFound,

        // commit 요청의 operationId가 prepare가 저장한 commitOperationId와 다르다
        OperationMismatch
    }

    /// <summary> prepare, commit, resolve의 결과. Result가 null이면 기능 차단 중 처음 보는 operationId다 </summary>
    public sealed class TradeOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public ETradeLookup        Lookup    { get; }
        public OperationResult?    Result    { get; }
        public TradeExecutionView? Execution { get; }

        /****************************************
        *                Methods
        ****************************************/

        public TradeOutcome(ETradeLookup lookup, OperationResult? result, TradeExecutionView? execution)
        {
            Lookup    = lookup;
            Result    = result;
            Execution = execution;
        }
    }

    /// <summary> prepare 요청을 검증해 파싱한 값. 양쪽 계정과 캐릭터, 제안이 서로 다른 형식이 아니라는 것만 보장한다 </summary>
    public sealed class TradePrepareInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid           ExecutionId       { get; }
        public Guid           TradeId           { get; }
        public Guid           CommitOperationId { get; }
        public Guid           TownInstanceId    { get; }
        public Guid           AccountA          { get; }
        public Guid           CharacterA        { get; }
        public Guid           AccountB          { get; }
        public Guid           CharacterB        { get; }
        public TradeSideOffer OfferA            { get; }
        public TradeSideOffer OfferB            { get; }

        /****************************************
        *                Methods
        ****************************************/

        private TradePrepareInput(Guid executionId, Guid tradeId, Guid commitOperationId, Guid townInstanceId,
            Guid accountA, Guid characterA, Guid accountB, Guid characterB, TradeSideOffer offerA, TradeSideOffer offerB)
        {
            ExecutionId       = executionId;
            TradeId           = tradeId;
            CommitOperationId = commitOperationId;
            TownInstanceId    = townInstanceId;
            AccountA          = accountA;
            CharacterA        = characterA;
            AccountB          = accountB;
            CharacterB        = characterB;
            OfferA            = offerA;
            OfferB            = offerB;
        }

        /// <summary> 형식이 맞으면 true. operationId는 executionId와 같아야 하고 commitOperationId는 그것과 달라야 한다(저장된 작업 키가 겹치지 않게) </summary>
        public static bool TryParse(TradePrepareRequest? request, out TradePrepareInput? input)
        {
            input = null;
            if ((request == null) || (request.SideA == null) || (request.SideB == null)
                || !TryId(request.ExecutionId, out Guid execution) || !TryId(request.OperationId, out Guid operation)
                || !TryId(request.TradeId, out Guid trade) || !TryId(request.CommitOperationId, out Guid commit)
                || !TryId(request.TownInstanceId, out Guid town) || (operation != execution) || (commit == execution)
                || !TryId(request.SideA.AccountId, out Guid accountA) || !TryId(request.SideA.CharacterId, out Guid characterA)
                || !TryId(request.SideB.AccountId, out Guid accountB) || !TryId(request.SideB.CharacterId, out Guid characterB))
            {
                return false;
            }

            input = new TradePrepareInput(execution, trade, commit, town, accountA, characterA, accountB, characterB,
                request.SideA, request.SideB);
            return true;
        }

        private static bool TryId(string? value, out Guid id)
        {
            return Guid.TryParse(value, out id) && (id != Guid.Empty);
        }
    }

    /// <summary> 실행 행에 동결해 저장하는 한 쪽의 제안(offer_a, offer_b의 JSON) </summary>
    internal sealed class StoredOffer
    {
        /****************************************
        *              Properties
        ****************************************/

        [JsonProperty("items")]    public List<StoredLine> Items    { get; set; } = new List<StoredLine>();
        [JsonProperty("currency")] public long             Currency { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public static StoredOffer From(TradeSideOffer offer)
        {
            return new StoredOffer
            {
                Currency = offer.Currency,
                Items = (offer.Items ?? Array.Empty<TradeItemLine>()).Select(line => new StoredLine
                {
                    AssetKind = line.AssetKind,
                    DefinitionId = line.DefinitionId ?? string.Empty,
                    ItemId = Guid.TryParse(line.ItemId, out Guid itemId) ? itemId : null,
                    Quantity = line.Quantity
                }).ToList()
            };
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None);
        }

        public static StoredOffer Parse(string json)
        {
            return JsonConvert.DeserializeObject<StoredOffer>(json)
                ?? throw new InvalidOperationException("Stored trade offer is corrupt.");
        }
    }

    internal sealed class StoredLine
    {
        /****************************************
        *              Properties
        ****************************************/

        [JsonProperty("assetKind")]    public string AssetKind    { get; set; } = string.Empty;
        [JsonProperty("definitionId")] public string DefinitionId { get; set; } = string.Empty;
        [JsonProperty("itemId")]       public Guid?  ItemId       { get; set; }
        [JsonProperty("quantity")]     public int    Quantity     { get; set; }
    }

    /// <summary> player_trade_executions 한 행 </summary>
    internal sealed class TradeExecutionRow
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string COLUMNS =
            """
            execution_id AS "ExecutionId", trade_id AS "TradeId", commit_operation_id AS "CommitOperationId",
            town_instance_id AS "TownInstanceId", account_a AS "AccountA", character_a AS "CharacterA",
            account_b AS "AccountB", character_b AS "CharacterB", offer_a::text AS "OfferA", offer_b::text AS "OfferB",
            state AS "State", reason_code AS "ReasonCode",
            (extract(epoch FROM prepared_at) * 1000)::bigint AS "PreparedAtUnixMs",
            COALESCE((extract(epoch FROM finished_at) * 1000)::bigint, 0) AS "FinishedAtUnixMs",
            (extract(epoch FROM clock_timestamp()) * 1000)::bigint AS "ServerTimeUnixMs", revision AS "Revision"
            """;

        /****************************************
        *              Properties
        ****************************************/

        public Guid   ExecutionId       { get; set; }
        public Guid   TradeId           { get; set; }
        public Guid   CommitOperationId { get; set; }
        public Guid   TownInstanceId    { get; set; }
        public Guid   AccountA          { get; set; }
        public Guid   CharacterA        { get; set; }
        public Guid   AccountB          { get; set; }
        public Guid   CharacterB        { get; set; }
        public string OfferA            { get; set; } = string.Empty;
        public string OfferB            { get; set; } = string.Empty;
        public string State             { get; set; } = string.Empty;
        public int    ReasonCode        { get; set; }
        public long   PreparedAtUnixMs  { get; set; }
        public long   FinishedAtUnixMs  { get; set; }
        public long   ServerTimeUnixMs  { get; set; }
        public long   Revision          { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public TradeExecutionView ToView()
        {
            return new TradeExecutionView(ExecutionId.ToString("D"), TradeId.ToString("D"), State, ReasonCode,
                PreparedAtUnixMs, FinishedAtUnixMs, ServerTimeUnixMs);
        }
    }

    /// <summary> 제안 형식 검사. 보유량과 소유 같은 DB 상태는 실행(commit)이 잠근 행으로 다시 판정한다 </summary>
    public static class TradeOfferRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 엽전 한 번 이동의 상한. 지갑 합이 64비트를 넘는 입력을 막는다
        public const long MAX_CURRENCY = 1_000_000_000_000_000;

        // 정의 ID 길이 상한. DB definition_id 열 규약과 맞춘다
        private const int MAX_DEFINITION_ID_LENGTH = 64;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 한 쪽 제안의 형식. 통과하면 NONE, 아니면 거절 사유다. 칸 수, 수량, 중복, 엽전 범위, 수량형 거래 가능 품목을 본다 </summary>
        public static int ValidateSide(TradeSideOffer offer)
        {
            if (offer == null)
            {
                return ContractCodes.RejectReason.TRADE_ITEM_CHANGED;
            }

            TradeItemLine[] items = offer.Items ?? Array.Empty<TradeItemLine>();
            if ((items.Length > TradeCodes.TRADE_MAX_ITEMS) || (offer.Currency < 0) || (offer.Currency > MAX_CURRENCY))
            {
                return ContractCodes.RejectReason.TRADE_ITEM_CHANGED;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TradeItemLine line in items)
            {
                if (line == null)
                {
                    return ContractCodes.RejectReason.TRADE_ITEM_CHANGED;
                }

                if (line.AssetKind == MarketCodes.ASSET_EQUIPMENT)
                {
                    if (!IsValidDefinitionId(line.DefinitionId) || !Guid.TryParse(line.ItemId, out Guid itemId)
                        || (itemId == Guid.Empty) || (line.Quantity != 1) || !keys.Add("E:" + itemId.ToString("D")))
                    {
                        return ContractCodes.RejectReason.TRADE_ITEM_CHANGED;
                    }
                    continue;
                }
                if (line.AssetKind != MarketCodes.ASSET_ACCOUNT_ITEM)
                {
                    return ContractCodes.RejectReason.TRADE_NOT_TRADABLE;
                }
                if (!IsValidDefinitionId(line.DefinitionId))
                {
                    return ContractCodes.RejectReason.TRADE_ITEM_CHANGED;
                }
                if (!MarketTradeRules.IsTradableAccountItem(line.DefinitionId))
                {
                    return ContractCodes.RejectReason.TRADE_NOT_TRADABLE;
                }
                if (!MarketTradeRules.IsQuantityInRange(line.Quantity) || !keys.Add("A:" + line.DefinitionId))
                {
                    return ContractCodes.RejectReason.TRADE_ITEM_CHANGED;
                }
            }

            return ContractCodes.RejectReason.NONE;
        }

        private static bool IsValidDefinitionId(string? definitionId)
        {
            return !string.IsNullOrEmpty(definitionId) && (definitionId.Length <= MAX_DEFINITION_ID_LENGTH);
        }

        /// <summary> 양쪽 제안의 형식. 양쪽이 모두 비어 있으면 주고받을 것이 없는 거래라 거절한다 </summary>
        public static int Validate(TradeSideOffer offerA, TradeSideOffer offerB)
        {
            int reason = ValidateSide(offerA);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return reason;
            }

            reason = ValidateSide(offerB);
            if (reason != ContractCodes.RejectReason.NONE)
            {
                return reason;
            }

            bool emptyA = ((offerA.Items == null) || (offerA.Items.Length == 0)) && (offerA.Currency == 0);
            bool emptyB = ((offerB.Items == null) || (offerB.Items.Length == 0)) && (offerB.Currency == 0);
            return (emptyA && emptyB) ? ContractCodes.RejectReason.TRADE_ITEM_CHANGED : ContractCodes.RejectReason.NONE;
        }

        /// <summary> 원장 줄의 방향 구분. 같은 계정이 같은 자산을 주고받아도 줄이 겹치지 않는다 </summary>
        public static string ScopeOf(bool fromA)
        {
            return fromA ? TradeCodes.SCOPE_A_TO_B : TradeCodes.SCOPE_B_TO_A;
        }
    }
}
