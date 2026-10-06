// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 계정 공동 보관 약초 재고와 보관 상한 </summary>
    [DataContract]
    public sealed class LifeHerbStock
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "definitionId")] private string _definitionId;
        [DataMember(Name = "quantity")]     private long   _quantity;
        [DataMember(Name = "cap")]          private long   _cap;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;
        public long   Quantity     => _quantity;
        public long   Cap          => _cap;

        /****************************************
        *                Methods
        ****************************************/

        public LifeHerbStock(string definitionId, long quantity, long cap)
        {
            _definitionId = definitionId;
            _quantity     = quantity;
            _cap          = cap;
        }

        private LifeHerbStock() { }
    }
}
