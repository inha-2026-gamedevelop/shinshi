namespace Shinshi.Network
{
    /// <summary> 성장 상태의 강화 실패 보정 하나, 키는 부위와 현재 단계다 </summary>
    public readonly struct TownPityEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly byte _slot;
        private readonly int  _stage;
        private readonly int  _failures;

        /****************************************
        *              Properties
        ****************************************/

        public byte Slot     => _slot;
        public int  Stage    => _stage;
        public int  Failures => _failures;

        /****************************************
        *                Methods
        ****************************************/

        public TownPityEntry(byte slot, int stage, int failures)
        {
            _slot     = slot;
            _stage    = stage;
            _failures = failures;
        }
    }
}
