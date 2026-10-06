// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 출정에 고정한 부적 한 칸과 효과 레벨 </summary>
    [DataContract]
    public sealed class CombatTalismanEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "skillId")]  private string _skillId;
        [DataMember(Name = "tier")]     private int    _tier;
        [DataMember(Name = "effectId")] private string _effectId;
        [DataMember(Name = "level")]    private int    _level;

        /****************************************
        *              Properties
        ****************************************/

        public string SkillId  => _skillId;
        public int    Tier     => _tier;
        public string EffectId => _effectId;
        public int    Level    => _level;

        /****************************************
        *                Methods
        ****************************************/

        public CombatTalismanEntry(string skillId, int tier, string effectId, int level)
        {
            _skillId  = skillId;
            _tier     = tier;
            _effectId = effectId;
            _level    = level;
        }

        private CombatTalismanEntry() { }
    }
}
