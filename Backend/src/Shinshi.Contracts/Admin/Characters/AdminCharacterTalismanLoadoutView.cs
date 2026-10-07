// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 기술별 부적 장착 칸. tier는 저장된 0~2 값 </summary>
    [DataContract]
    public sealed class AdminCharacterTalismanLoadoutView
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

        public AdminCharacterTalismanLoadoutView(string skillId, int tier, string effectId)
        {
            _skillId  = skillId;
            _tier     = tier;
            _effectId = effectId;
        }

        private AdminCharacterTalismanLoadoutView() { }
    }
}
