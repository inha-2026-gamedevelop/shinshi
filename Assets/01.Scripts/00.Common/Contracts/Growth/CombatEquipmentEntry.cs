// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 출정에 고정한 착용 장비 한 부위 </summary>
    [DataContract]
    public sealed class CombatEquipmentEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "definitionId")] private string _definitionId;
        [DataMember(Name = "grade")]        private int    _grade;
        [DataMember(Name = "isTraining")]   private bool   _isTraining;
        [DataMember(Name = "enhancement")]  private int    _enhancement;
        // 옵션 두 줄, 예전에 고정한 출정 스냅숏에는 없어 0(옵션 없음)으로 읽힌다
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

        public string DefinitionId => _definitionId;
        public int    Grade        => _grade;
        public bool   IsTraining   => _isTraining;
        public int    Enhancement  => _enhancement;

        /****************************************
        *                Methods
        ****************************************/

        public CombatEquipmentEntry(string definitionId, int grade, bool isTraining, int enhancement,
            int option1Kind = 0, int option1Value = 0, int option2Kind = 0, int option2Value = 0)
        {
            _option1Kind  = option1Kind;
            _option1Value = option1Value;
            _option2Kind  = option2Kind;
            _option2Value = option2Value;
            _definitionId = definitionId;
            _grade        = grade;
            _isTraining   = isTraining;
            _enhancement  = enhancement;
        }

        private CombatEquipmentEntry() { }
    }
}
