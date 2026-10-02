// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 출정에 지참할 물약 한 종류, 가용 재고 안에서만 예약된다 </summary>
    [DataContract]
    public sealed class ReservationPotion
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

        public ReservationPotion(string definitionId, string bindingKind, long quantity)
        {
            _definitionId = definitionId;
            _bindingKind  = bindingKind;
            _quantity     = quantity;
        }

        private ReservationPotion() { }
    }
}
