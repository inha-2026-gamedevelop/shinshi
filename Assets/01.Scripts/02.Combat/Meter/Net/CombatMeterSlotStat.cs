namespace Shinshi.Combat
{
    /// <summary> 슬롯 하나의 피해 합, 적중 수, 최대 타격 </summary>
    public readonly struct CombatMeterSlotStat
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float  _damage;
        private readonly ushort _hits;
        private readonly float  _maxHit;

        /****************************************
        *              Properties
        ****************************************/

        public float  Damage => _damage;
        public ushort Hits   => _hits;
        public float  MaxHit => _maxHit;

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterSlotStat(float damage, ushort hits, float maxHit)
        {
            _damage = damage;
            _hits   = hits;
            _maxHit = maxHit;
        }
    }
}
