// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 계정 보관 수량형 아이템 한 줄(보스 재료, 물약, 미등록 부적) </summary>
    [DataContract]
    public sealed class GrowthItemView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "definitionId")] private string _definitionId;
        [DataMember(Name = "bindingKind")]  private string _bindingKind;
        [DataMember(Name = "quantity")]     private long   _quantity;
        // 출정 지참 등으로 묶인 양, 가용량은 quantity - reserved
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

        public GrowthItemView(string definitionId, string bindingKind, long quantity, long reserved)
        {
            _definitionId = definitionId;
            _bindingKind  = bindingKind;
            _quantity     = quantity;
            _reserved     = reserved;
        }

        private GrowthItemView() { }
    }
}
