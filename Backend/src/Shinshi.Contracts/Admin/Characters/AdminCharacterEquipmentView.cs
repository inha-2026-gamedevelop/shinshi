// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 캐릭터 소유 장비 개체의 현재 저장 상태 </summary>
    [DataContract]
    public sealed class AdminCharacterEquipmentView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "itemId")]          private string _itemId;
        [DataMember(Name = "definitionId")]    private string _definitionId;
        [DataMember(Name = "slot")]            private int    _slot;
        [DataMember(Name = "grade")]           private int    _grade;
        [DataMember(Name = "isTraining")]      private bool   _isTraining;
        [DataMember(Name = "origin")]          private string _origin;
        [DataMember(Name = "enhancement")]     private int    _enhancement;
        [DataMember(Name = "equipped")]        private bool   _equipped;
        [DataMember(Name = "locked")]          private bool   _locked;
        [DataMember(Name = "bound")]           private bool   _bound;
        [DataMember(Name = "createdAtUnixMs")] private long   _createdAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string ItemId          => _itemId;
        public string DefinitionId    => _definitionId;
        public int    Slot            => _slot;
        public int    Grade           => _grade;
        public bool   IsTraining      => _isTraining;
        public string Origin          => _origin;
        public int    Enhancement     => _enhancement;
        public bool   Equipped        => _equipped;
        public bool   Locked          => _locked;
        public bool   Bound           => _bound;
        public long   CreatedAtUnixMs => _createdAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterEquipmentView(string itemId, string definitionId, int slot, int grade,
            bool isTraining, string origin, int enhancement, bool equipped, bool locked, bool bound,
            long createdAtUnixMs)
        {
            _itemId          = itemId;
            _definitionId    = definitionId;
            _slot            = slot;
            _grade           = grade;
            _isTraining      = isTraining;
            _origin          = origin;
            _enhancement     = enhancement;
            _equipped        = equipped;
            _locked          = locked;
            _bound           = bound;
            _createdAtUnixMs = createdAtUnixMs;
        }

        private AdminCharacterEquipmentView() { }
    }
}
