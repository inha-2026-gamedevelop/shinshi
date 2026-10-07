// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 거래 한 쪽의 동결된 제안 </summary>
    [DataContract]
    public sealed class TradeSideOffer
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]   private string          _accountId;
        [DataMember(Name = "characterId")] private string          _characterId;
        // TradeCodes.TRADE_MAX_ITEMS 이하
        [DataMember(Name = "items")]       private TradeItemLine[] _items;
        // 0 이상
        [DataMember(Name = "currency")]    private long            _currency;

        /****************************************
        *              Properties
        ****************************************/

        public string          AccountId   => _accountId;
        public string          CharacterId => _characterId;
        public TradeItemLine[] Items       => _items;
        public long            Currency    => _currency;

        /****************************************
        *                Methods
        ****************************************/

        public TradeSideOffer(string accountId, string characterId, TradeItemLine[] items, long currency)
        {
            _accountId   = accountId;
            _characterId = characterId;
            _items       = items;
            _currency    = currency;
        }

        private TradeSideOffer() { }
    }
}
