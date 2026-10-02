// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 귀속 아이템 수량. reserved는 진행 중 출정이 잡아 둔 양이다 </summary>
    [DataContract]
    public sealed class AdminAccountItemView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "definitionId")] private string _definitionId;
        [DataMember(Name = "bindingKind")]  private string _bindingKind;
        [DataMember(Name = "quantity")]     private long   _quantity;
        [DataMember(Name = "reserved")]     private long   _reserved;

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;
        public string BindingKind  => _bindingKind;
        public long   Quantity     => _quantity;
        public long   Reserved     => _reserved;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountItemView(string definitionId, string bindingKind, long quantity, long reserved)
        {
            _definitionId = definitionId;
            _bindingKind  = bindingKind;
            _quantity     = quantity;
            _reserved     = reserved;
        }

        private AdminAccountItemView() { }
    }
}
