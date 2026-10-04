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

        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId => _definitionId;
        public int    Grade        => _grade;
        public bool   IsTraining   => _isTraining;
        public int    Enhancement  => _enhancement;

        /****************************************
        *                Methods
        ****************************************/

        public CombatEquipmentEntry(string definitionId, int grade, bool isTraining, int enhancement)
        {
            _definitionId = definitionId;
            _grade        = grade;
            _isTraining   = isTraining;
            _enhancement  = enhancement;
        }

        private CombatEquipmentEntry() { }
    }
}
