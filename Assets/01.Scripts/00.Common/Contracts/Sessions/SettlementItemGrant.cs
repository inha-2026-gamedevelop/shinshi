// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 정산으로 지급할 계정 아이템 한 묶음 </summary>
    [DataContract]
    public sealed class SettlementItemGrant
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "definitionId")] private string _definitionId;
        [DataMember(Name = "bindingKind")]  private string _bindingKind;
        [DataMember(Name = "quantity")]     private long   _quantity;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;
        public string BindingKind  => _bindingKind;
        public long   Quantity     => _quantity;

        /****************************************
        *                Methods
        ****************************************/

        public SettlementItemGrant(string definitionId, string bindingKind, long quantity)
        {
            _definitionId = definitionId;
            _bindingKind  = bindingKind;
            _quantity     = quantity;
        }

        private SettlementItemGrant() { }
    }
}
