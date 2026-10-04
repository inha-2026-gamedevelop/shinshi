// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 물약 완제품 한 종류의 재고. reserved는 출정에 지참으로 예약된 수량이고 가용량은 quantity - reserved다 </summary>
    [DataContract]
    public sealed class LifePotionStock
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "definitionId")] private string _definitionId;
        [DataMember(Name = "quantity")]     private long   _quantity;
        [DataMember(Name = "reserved")]     private long   _reserved;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;
        public long   Quantity     => _quantity;
        public long   Reserved     => _reserved;

        /****************************************
        *                Methods
        ****************************************/

        public LifePotionStock(string definitionId, long quantity, long reserved)
        {
            _definitionId = definitionId;
            _quantity     = quantity;
            _reserved     = reserved;
        }

        private LifePotionStock() { }
    }
}
