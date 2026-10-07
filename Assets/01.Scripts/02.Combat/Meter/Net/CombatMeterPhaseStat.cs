namespace Shinshi.Combat
{
    /// <summary> 보스 단계 하나의 경과 초와 피해 합 </summary>
    public readonly struct CombatMeterPhaseStat
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly byte  _phase;
        private readonly float _seconds;
        private readonly float _damage;

        /****************************************
        *              Properties
        ****************************************/

        public byte  Phase   => _phase;
        public float Seconds => _seconds;
        public float Damage  => _damage;

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterPhaseStat(byte phase, float seconds, float damage)
        {
            _phase   = phase;
            _seconds = seconds;
            _damage  = damage;
        }
    }
}
