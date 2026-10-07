// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 거래 제안의 품목 한 줄. 장비는 itemId, 수량형은 quantity를 쓴다 </summary>
    [DataContract]
    public sealed class TradeItemLine
    {
        /****************************************
        *                Fields
        ****************************************/

        // MarketCodes.ASSET_*
        [DataMember(Name = "assetKind")]    private string _assetKind;
        [DataMember(Name = "definitionId")] private string _definitionId;
        // 장비 인스턴스 ID, 수량형이면 null
        [DataMember(Name = "itemId")]       private string _itemId;
        // 장비는 1
        [DataMember(Name = "quantity")]     private int    _quantity;

        /****************************************
        *              Properties
        ****************************************/

        public string AssetKind    => _assetKind;
        public string DefinitionId => _definitionId;
        public string ItemId       => _itemId;
        public int    Quantity     => _quantity;

        /****************************************
        *                Methods
        ****************************************/

        public TradeItemLine(string assetKind, string definitionId, string itemId, int quantity)
        {
            _assetKind    = assetKind;
            _definitionId = definitionId;
            _itemId       = itemId;
            _quantity     = quantity;
        }

        private TradeItemLine() { }
    }
}
