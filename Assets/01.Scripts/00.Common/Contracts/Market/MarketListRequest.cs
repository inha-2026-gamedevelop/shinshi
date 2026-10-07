// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 등록. actor는 인증된 계정이고 캐릭터는 경로의 characterId다. 장비는 itemId, 수량형은 quantity를 쓴다 </summary>
    [DataContract]
    public sealed class MarketListRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // Guid "D" 형식, 재전송은 같은 값
        [DataMember(Name = "operationId")]  private string _operationId;
        // MarketCodes.ASSET_*
        [DataMember(Name = "assetKind")]    private string _assetKind;
        [DataMember(Name = "definitionId")] private string _definitionId;
        // 장비 인스턴스 ID, 수량형이면 null
        [DataMember(Name = "itemId")]       private string _itemId;
        // 장비는 1
        [DataMember(Name = "quantity")]     private int    _quantity;
        [DataMember(Name = "unitPrice")]    private long   _unitPrice;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId  => _operationId;
        public string AssetKind    => _assetKind;
        public string DefinitionId => _definitionId;
        public string ItemId       => _itemId;
        public int    Quantity     => _quantity;
        public long   UnitPrice    => _unitPrice;

        /****************************************
        *                Methods
        ****************************************/

        public MarketListRequest(string operationId, string assetKind, string definitionId, string itemId, int quantity, long unitPrice)
        {
            _operationId  = operationId;
            _assetKind    = assetKind;
            _definitionId = definitionId;
            _itemId       = itemId;
            _quantity     = quantity;
            _unitPrice    = unitPrice;
        }

        private MarketListRequest() { }
    }
}
