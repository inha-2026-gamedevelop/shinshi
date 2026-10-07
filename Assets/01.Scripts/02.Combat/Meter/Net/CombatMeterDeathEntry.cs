namespace Shinshi.Combat
{
    /// <summary> 사망 요약 한 건, 사망 직전 받은 피해 출처와 양, 사망까지 남은 시간 </summary>
    public readonly struct CombatMeterDeathEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _sourceId;
        private readonly float  _amount;
        private readonly float  _secondsBeforeDeath;

        /****************************************
        *              Properties
        ****************************************/

        public string SourceId           => _sourceId;
        public float  Amount              => _amount;
        public float  SecondsBeforeDeath  => _secondsBeforeDeath;

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterDeathEntry(string sourceId, float amount, float secondsBeforeDeath)
        {
            _sourceId           = sourceId;
            _amount             = amount;
            _secondsBeforeDeath = secondsBeforeDeath;
        }
    }
}
