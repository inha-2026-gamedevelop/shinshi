using System;

namespace Shinshi.Network
{
    /// <summary> 거래 제안의 품목 한 칸, itemId가 있으면 장비 하나이고 없으면 수량형 아이템이다 </summary>
    public readonly struct TradeOfferLine : IEquatable<TradeOfferLine>
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _definitionId;
        private readonly string _itemId;
        private readonly int    _quantity;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;
        public string ItemId       => _itemId;
        public int    Quantity     => _quantity;
        public bool   IsEquipment  => !string.IsNullOrEmpty(_itemId);

        /****************************************
        *                Methods
        ****************************************/

        public TradeOfferLine(string definitionId, string itemId, int quantity)
        {
            _definitionId = definitionId;
            _itemId       = itemId;
            _quantity     = quantity;
        }

        public bool Equals(TradeOfferLine other)
        {
            return (_quantity == other._quantity) && (string.Equals(_definitionId, other._definitionId, StringComparison.Ordinal))
                && (string.Equals(_itemId, other._itemId, StringComparison.Ordinal));
        }

        public override bool Equals(object obj)
        {
            return (obj is TradeOfferLine other) && (Equals(other));
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_definitionId, _itemId, _quantity);
        }
    }
}
