namespace Shinshi.AI
{
    // 서버가 확정한 2페이즈 전환 연출 구간 한 건이다. 서버(BossServer)가 구간 동안 보스를 무적으로 두고 인계를 미루며,
    // BossState의 전환 구간 SyncVar로 복제한다. 성공 사건이나 PhaseState만으로 만들지 않는다. 그 둘에는 전환 번호와 구간 시각이 없다
    /// <summary> 월백 2페이즈 전환 컷신 한 번의 서버 확정 값, 전투 번호, 전환 번호, 시작과 종료 서버 시각 </summary>
    public readonly struct WolbaekPhase2TransitionCue
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 전투 대상 번호, BossState.CombatId와 같아야 지금 연결의 사건이다 </summary>
        public int CombatId { get; }

        /// <summary> 같은 전투 안에서 전환마다 커지는 번호, 0 이하는 잘못된 값이다 </summary>
        public int TransitionId { get; }

        public double StartServerTime { get; }

        /// <summary> 서버가 2페이즈 전투를 재개하는 시각, 클라이언트 재생 종료가 이 시각을 정하지 않는다 </summary>
        public double EndServerTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekPhase2TransitionCue(int combatId, int transitionId, double startServerTime, double endServerTime)
        {
            CombatId        = combatId;
            TransitionId    = transitionId;
            StartServerTime = startServerTime;
            EndServerTime   = endServerTime;
        }
    }
}
