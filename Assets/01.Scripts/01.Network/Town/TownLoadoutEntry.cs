namespace Shinshi.Network
{
    /// <summary> 부적 조합 한 칸, 성장 요청과 성장 상태가 함께 쓴다 </summary>
    public readonly struct TownLoadoutEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _skillId;
        private readonly int    _tier;
        private readonly string _effectId;

        /****************************************
        *              Properties
        ****************************************/

        public string SkillId  => _skillId;
        public int    Tier     => _tier;
        public string EffectId => _effectId;

        /****************************************
        *                Methods
        ****************************************/

        public TownLoadoutEntry(string skillId, int tier, string effectId)
        {
            _skillId  = skillId;
            _tier     = tier;
            _effectId = effectId;
        }
    }
}
