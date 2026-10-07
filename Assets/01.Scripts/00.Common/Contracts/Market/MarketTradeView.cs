// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 성사 기록 한 줄 </summary>
    [DataContract]
    public sealed class MarketTradeView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "tradeId")]         private string _tradeId;
        [DataMember(Name = "listingId")]       private string _listingId;
        // MarketCodes.TradeRole, 요청한 계정 기준
        [DataMember(Name = "role")]            private string _role;
        [DataMember(Name = "definitionId")]    private string _definitionId;
        [DataMember(Name = "quantity")]        private int    _quantity;
        [DataMember(Name = "unitPrice")]       private long   _unitPrice;
        [DataMember(Name = "gross")]           private long   _gross;
        [DataMember(Name = "fee")]             private long   _fee;
        [DataMember(Name = "net")]             private long   _net;
        [DataMember(Name = "createdAtUnixMs")] private long   _createdAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string TradeId         => _tradeId;
        public string ListingId       => _listingId;
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

        public MarketTradeView(string tradeId, string listingId, string role, string definitionId, int quantity, long unitPrice, long gross, long fee, long net, long createdAtUnixMs)
        {
            _tradeId         = tradeId;
            _listingId       = listingId;
            _role            = role;
            _definitionId    = definitionId;
            _quantity        = quantity;
            _unitPrice       = unitPrice;
            _gross           = gross;
            _fee             = fee;
            _net             = net;
            _createdAtUnixMs = createdAtUnixMs;
        }

        private MarketTradeView() { }
    }
}
