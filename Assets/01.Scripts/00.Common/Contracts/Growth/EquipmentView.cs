// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 보유 장비 하나 </summary>
    [DataContract]
    public sealed class EquipmentView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "itemId")]       private string _itemId;
        [DataMember(Name = "definitionId")] private string _definitionId;
        // EEquipmentSlot
        [DataMember(Name = "slot")]         private int    _slot;
        // EItemGrade
        [DataMember(Name = "grade")]        private int    _grade;
        [DataMember(Name = "isTraining")]   private bool   _isTraining;
        // GrowthCodes.EquipmentOrigin
        [DataMember(Name = "origin")]       private string _origin;
        [DataMember(Name = "enhancement")]  private int    _enhancement;
        [DataMember(Name = "equipped")]     private bool   _equipped;
        [DataMember(Name = "locked")]       private bool   _locked;
        // 캐릭터 귀속, 거래 불가
        [DataMember(Name = "bound")]        private bool   _bound;
        // 유저 경매장에 보관 중이라 착용, 강화, 계승, 잠금, 분해를 할 수 없다
        [DataMember(Name = "inMarket")]     private bool   _inMarket;
        // 옵션 두 줄, 종류는 EquipmentOptionRules의 KIND_* 값이고 값은 만분율이다. 종류 0은 옵션 없음
        [DataMember(Name = "option1Kind")]  private int    _option1Kind;
        [DataMember(Name = "option1Value")] private int    _option1Value;
        [DataMember(Name = "option2Kind")]  private int    _option2Kind;
        [DataMember(Name = "option2Value")] private int    _option2Value;

        /****************************************
        *              Properties
        ****************************************/

        public int    Option1Kind  => _option1Kind;
        public int    Option1Value => _option1Value;
        public int    Option2Kind  => _option2Kind;
        public int    Option2Value => _option2Value;

        public string ItemId       => _itemId;
        public string DefinitionId => _definitionId;
        public int    Slot         => _slot;
        public int    Grade        => _grade;
        public bool   IsTraining   => _isTraining;
        public string Origin       => _origin;
        public int    Enhancement  => _enhancement;
        public bool   Equipped     => _equipped;
        public bool   Locked       => _locked;
        public bool   Bound        => _bound;
        public bool   InMarket     => _inMarket;

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentView(string itemId, string definitionId, int slot, int grade, bool isTraining, string origin,
            int enhancement, bool equipped, bool locked, bool bound, bool inMarket = false,
            int option1Kind = 0, int option1Value = 0, int option2Kind = 0, int option2Value = 0)
        {
            _option1Kind  = option1Kind;
            _option1Value = option1Value;
            _option2Kind  = option2Kind;
            _option2Value = option2Value;
            _itemId       = itemId;
            _definitionId = definitionId;
            _slot         = slot;
            _grade        = grade;
            _isTraining   = isTraining;
            _origin       = origin;
            _enhancement  = enhancement;
            _equipped     = equipped;
            _locked       = locked;
            _bound        = bound;
            _inMarket     = inMarket;
        }

        private EquipmentView() { }
    }
}
