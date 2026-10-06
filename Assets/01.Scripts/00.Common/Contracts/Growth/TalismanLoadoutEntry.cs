// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 부적 조합 한 칸, 기술마다 단계(0 보조1, 1 보조2, 2 핵심)당 하나 </summary>
    [DataContract]
    public sealed class TalismanLoadoutEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "skillId")]  private string _skillId;
        [DataMember(Name = "tier")]     private int    _tier;
        [DataMember(Name = "effectId")] private string _effectId;

        /****************************************
        *              Properties
        ****************************************/

        public string SkillId  => _skillId;
        public int    Tier     => _tier;
        public string EffectId => _effectId;

        /****************************************
        *                Methods
        ****************************************/

        public TalismanLoadoutEntry(string skillId, int tier, string effectId)
        {
            _skillId  = skillId;
            _tier     = tier;
            _effectId = effectId;
        }

        private TalismanLoadoutEntry() { }
    }
}
