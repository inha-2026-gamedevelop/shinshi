namespace Shinshi.Combat
{
    /// <summary> 피해 적용 중 개체가 제거돼도 적용 직전의 귀속을 보존하는 한 건의 값 </summary>
    internal readonly struct CombatMeterHitContext
    {
        /****************************************
        *              Properties
        ****************************************/

        internal int  AttackerIndex { get; }
        internal int  VictimIndex   { get; }
        internal bool IsSummon      { get; }
        internal bool IsEnemyTarget { get; }
        internal bool IsBossTarget  { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal CombatMeterHitContext(int attackerIndex, int victimIndex, bool isSummon, bool isEnemyTarget,
            bool isBossTarget)
        {
            AttackerIndex = attackerIndex;
            VictimIndex   = victimIndex;
            IsSummon      = isSummon;
            IsEnemyTarget = isEnemyTarget;
            IsBossTarget  = isBossTarget;
        }
    }
}
