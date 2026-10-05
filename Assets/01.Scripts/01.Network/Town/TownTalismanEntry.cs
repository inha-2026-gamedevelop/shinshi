namespace Shinshi.Network
{
    /// <summary> 성장 상태의 부적 효과 하나, level 0은 실패 보정만 쌓인 상태다 </summary>
    public readonly struct TownTalismanEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _effectId;
        private readonly int    _level;
        private readonly int    _registerFailures;

        /****************************************
        *              Properties
        ****************************************/

        public string EffectId         => _effectId;
        public int    Level            => _level;
        public int    RegisterFailures => _registerFailures;

        /****************************************
        *                Methods
        ****************************************/

        public TownTalismanEntry(string effectId, int level, int registerFailures)
        {
            _effectId         = effectId;
            _level            = level;
            _registerFailures = registerFailures;
        }
    }
}
