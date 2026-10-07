namespace Shinshi.Combat
{
    /// <summary> 파티 메시지가 참가자 한 명에 대해 보내는 출정, 구간 준 피해 합계 </summary>
    public readonly struct CombatMeterPartyEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int   _combatId;
        private readonly float _encounterDamage;
        private readonly float _phaseDamage;

        /****************************************
        *              Properties
        ****************************************/

        // PlayerCombatState.CombatId와 같은 값이다. 클라이언트는 이 번호로 이름, 직업을 찾는다
        public int   CombatId        => _combatId;
        public float EncounterDamage => _encounterDamage;
        public float PhaseDamage     => _phaseDamage;

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterPartyEntry(int combatId, float encounterDamage, float phaseDamage)
        {
            _combatId        = combatId;
            _encounterDamage = encounterDamage;
            _phaseDamage     = phaseDamage;
        }
    }
}
