// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 등록 한 건 </summary>
    [DataContract]
    public sealed class MarketListingView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "listingId")]       private string _listingId;
        // MarketCodes.ASSET_*
        [DataMember(Name = "assetKind")]       private string _assetKind;
        [DataMember(Name = "definitionId")]    private string _definitionId;
        // 장비 인스턴스 ID, 수량형이면 null
        [DataMember(Name = "itemId")]          private string _itemId;
        [DataMember(Name = "grade")]           private int    _grade;
        [DataMember(Name = "classId")]         private int?   _classId;
        // MarketCodes.Category
        [DataMember(Name = "category")]        private int    _category;
        [DataMember(Name = "unitPrice")]       private long   _unitPrice;
        [DataMember(Name = "listedQty")]       private int    _listedQty;
        [DataMember(Name = "remainingQty")]    private int    _remainingQty;
        // MarketCodes.ListingState
        [DataMember(Name = "state")]           private string _state;
        [DataMember(Name = "grossTotal")]      private long   _grossTotal;
        [DataMember(Name = "feeTotal")]        private long   _feeTotal;
        [DataMember(Name = "netTotal")]        private long   _netTotal;
        [DataMember(Name = "createdAtUnixMs")] private long   _createdAtUnixMs;
        [DataMember(Name = "expiresAtUnixMs")] private long   _expiresAtUnixMs;
        // 종료 전이면 0
        [DataMember(Name = "closedAtUnixMs")]  private long   _closedAtUnixMs;
        // 판매자가 결과를 확인했는지
        [DataMember(Name = "seen")]            private bool   _seen;
        // 요청한 계정의 등록인지
        [DataMember(Name = "mine")]            private bool   _mine;

        /****************************************
        *              Properties
        ****************************************/

        public string ListingId       => _listingId;
        public string AssetKind       => _assetKind;
        public string DefinitionId    => _definitionId;
        public string ItemId          => _itemId;
        public int    Grade           => _grade;
        public int?   ClassId         => _classId;
        public int    Category        => _category;
        public long   UnitPrice       => _unitPrice;
        public int    ListedQty       => _listedQty;
        public int    RemainingQty    => _remainingQty;
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

        public MarketListingView(string listingId, string assetKind, string definitionId, string itemId, int grade, int? classId, int category, long unitPrice, int listedQty, int remainingQty, string state, long grossTotal, long feeTotal, long netTotal, long createdAtUnixMs, long expiresAtUnixMs, long closedAtUnixMs, bool seen, bool mine)
        {
            _listingId       = listingId;
            _assetKind       = assetKind;
            _definitionId    = definitionId;
            _itemId          = itemId;
            _grade           = grade;
            _classId         = classId;
            _category        = category;
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

        private MarketListingView() { }
    }
}
