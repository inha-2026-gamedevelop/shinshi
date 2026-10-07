namespace Shinshi.Combat
{
    /// <summary> 결과 화면 파티 행 한 명의 준 피해, 받은 피해, 사망, 부활 </summary>
    public readonly struct CombatMeterResultEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int    _combatId;
        private readonly string _displayName;
        private readonly float  _damage;
        private readonly float  _damageTaken;
        private readonly byte   _deaths;
        private readonly byte   _revives;

        /****************************************
        *              Properties
        ****************************************/

        public int    CombatId    => _combatId;
        public string DisplayName => _displayName;
        public float  Damage      => _damage;
        public float  DamageTaken => _damageTaken;
        public byte   Deaths      => _deaths;
        public byte   Revives     => _revives;

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterResultEntry(int combatId, string displayName, float damage, float damageTaken, byte deaths,
            byte revives)
        {
            _combatId    = combatId;
            _displayName = displayName;
            _damage      = damage;
            _damageTaken = damageTaken;
            _deaths      = deaths;
            _revives     = revives;
        }
    }
}
