// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 구매. 등록 한 건에서 수량을 정한다. 가격이 바뀌었으면 MarketPriceChanged로 거절된다 </summary>
    [DataContract]
    public sealed class MarketBuyRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // Guid "D" 형식, 재전송은 같은 값
        [DataMember(Name = "operationId")]       private string _operationId;
        [DataMember(Name = "listingId")]         private string _listingId;
        [DataMember(Name = "quantity")]          private int    _quantity;
        [DataMember(Name = "expectedUnitPrice")] private long   _expectedUnitPrice;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId       => _operationId;
        public string ListingId         => _listingId;
        public int    Quantity          => _quantity;
        public long   ExpectedUnitPrice => _expectedUnitPrice;

        /****************************************
        *                Methods
        ****************************************/

        public MarketBuyRequest(string operationId, string listingId, int quantity, long expectedUnitPrice)
        {
            _operationId       = operationId;
            _listingId         = listingId;
            _quantity          = quantity;
            _expectedUnitPrice = expectedUnitPrice;
        }

        private MarketBuyRequest() { }
    }
}
