// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 경매장 쓰기 결과. 구매는 거래 ID와 금액을 함께 받는다. 재전송도 처음 결과를 받는다 </summary>
    [DataContract]
    public sealed class MarketOperationResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "result")]    private OperationResult _result;
        // 영향을 받은 등록, 없으면 null
        [DataMember(Name = "listingId")] private string          _listingId;
        // 구매가 성사됐을 때의 market_trades ID, 아니면 null
        [DataMember(Name = "tradeId")]   private string          _tradeId;
        // 구매 총액, 구매가 아니면 0
        [DataMember(Name = "gross")]     private long            _gross;
        [DataMember(Name = "fee")]       private long            _fee;
        [DataMember(Name = "net")]       private long            _net;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult Result    => _result;
        public string          ListingId => _listingId;
        public string          TradeId   => _tradeId;
        public long            Gross     => _gross;
        public long            Fee       => _fee;
        public long            Net       => _net;

        /****************************************
        *                Methods
        ****************************************/

        public MarketOperationResponse(OperationResult result, string listingId, string tradeId, long gross, long fee, long net)
        {
            _result    = result;
            _listingId = listingId;
            _tradeId   = tradeId;
            _gross     = gross;
            _fee       = fee;
            _net       = net;
        }

        private MarketOperationResponse() { }
    }
}
