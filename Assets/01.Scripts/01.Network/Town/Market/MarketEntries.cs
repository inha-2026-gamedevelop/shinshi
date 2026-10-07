namespace Shinshi.Network
{
    /// <summary> 검색 결과의 정의 ID 묶음 하나 </summary>
    public readonly struct MarketGroupEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _definitionId;
        private readonly int    _grade;
        private readonly long   _lowestUnitPrice;
        private readonly long   _totalQty;
        private readonly int    _listingCount;
        private readonly long   _recentUnitPrice;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId    => _definitionId;
        public int    Grade           => _grade;
        public long   LowestUnitPrice => _lowestUnitPrice;
        public long   TotalQty        => _totalQty;
        public int    ListingCount    => _listingCount;
        public long   RecentUnitPrice => _recentUnitPrice;

        /****************************************
        *                Methods
        ****************************************/

        public MarketGroupEntry(string definitionId, int grade, long lowestUnitPrice, long totalQty, int listingCount,
            long recentUnitPrice)
        {
            _definitionId    = definitionId;
            _grade           = grade;
            _lowestUnitPrice = lowestUnitPrice;
            _totalQty        = totalQty;
            _listingCount    = listingCount;
            _recentUnitPrice = recentUnitPrice;
        }
    }

    /// <summary> 등록 하나, 다른 사람 등록 목록과 내 등록 목록이 같이 쓴다 </summary>
    public readonly struct MarketListingEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _listingId;
        private readonly string _assetKind;
        private readonly string _definitionId;
        private readonly string _itemId;
        private readonly int    _grade;
        private readonly long   _unitPrice;
        private readonly int    _listedQty;
        private readonly int    _remainingQty;
        private readonly string _state;
        private readonly long   _grossTotal;
        private readonly long   _feeTotal;
        private readonly long   _netTotal;
        private readonly long   _createdAtUnixMs;
        private readonly long   _expiresAtUnixMs;
        private readonly long   _closedAtUnixMs;
        private readonly bool   _seen;
        private readonly bool   _mine;

        /****************************************
        *              Properties
        ****************************************/

        public string ListingId       => _listingId;
        public string AssetKind       => _assetKind;
        public string DefinitionId    => _definitionId;
        public string ItemId          => _itemId;
        public int    Grade           => _grade;
        public long   UnitPrice       => _unitPrice;
        public int    ListedQty       => _listedQty;
        public int    RemainingQty    => _remainingQty;

        // MarketCodes.ListingState
        public string State           => _state;
        public long   GrossTotal      => _grossTotal;
        public long   FeeTotal        => _feeTotal;
        public long   NetTotal        => _netTotal;
        public long   CreatedAtUnixMs => _createdAtUnixMs;
        public long   ExpiresAtUnixMs => _expiresAtUnixMs;
        public long   ClosedAtUnixMs  => _closedAtUnixMs;
        public bool   Seen            => _seen;
        public bool   Mine            => _mine;

        /****************************************
        *                Methods
        ****************************************/

        public MarketListingEntry(string listingId, string assetKind, string definitionId, string itemId, int grade, long unitPrice,
            int listedQty, int remainingQty, string state, long grossTotal, long feeTotal, long netTotal, long createdAtUnixMs,
            long expiresAtUnixMs, long closedAtUnixMs, bool seen, bool mine)
        {
            _listingId       = listingId;
            _assetKind       = assetKind;
            _definitionId    = definitionId;
            _itemId          = itemId;
            _grade           = grade;
            _unitPrice       = unitPrice;
            _listedQty       = listedQty;
            _remainingQty    = remainingQty;
            _state           = state;
            _grossTotal      = grossTotal;
            _feeTotal        = feeTotal;
            _netTotal        = netTotal;
            _createdAtUnixMs = createdAtUnixMs;
            _expiresAtUnixMs = expiresAtUnixMs;
            _closedAtUnixMs  = closedAtUnixMs;
            _seen            = seen;
            _mine            = mine;
        }
    }

    /// <summary> 내 구매, 판매 기록 한 줄 </summary>
    public readonly struct MarketTradeEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _tradeId;
        private readonly string _role;
        private readonly string _definitionId;
        private readonly int    _quantity;
        private readonly long   _unitPrice;
        private readonly long   _gross;
        private readonly long   _fee;
        private readonly long   _net;
        private readonly long   _createdAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string TradeId         => _tradeId;

        // MarketCodes.TradeRole
        public string Role            => _role;
        public string DefinitionId    => _definitionId;
        public int    Quantity        => _quantity;
        public long   UnitPrice       => _unitPrice;
        public long   Gross           => _gross;
        public long   Fee             => _fee;
        public long   Net             => _net;
        public long   CreatedAtUnixMs => _createdAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public MarketTradeEntry(string tradeId, string role, string definitionId, int quantity, long unitPrice, long gross, long fee,
            long net, long createdAtUnixMs)
        {
            _tradeId         = tradeId;
            _role            = role;
            _definitionId    = definitionId;
            _quantity        = quantity;
            _unitPrice       = unitPrice;
            _gross           = gross;
            _fee             = fee;
            _net             = net;
            _createdAtUnixMs = createdAtUnixMs;
        }
    }
}
