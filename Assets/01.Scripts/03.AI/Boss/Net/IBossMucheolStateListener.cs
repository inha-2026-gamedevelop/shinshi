namespace Shinshi.AI
{
    // 표현 연결부가 IBossStateListener와 함께 구현하면 BossState가 무철 사건과 복원을 넘긴다. 구현하지 않으면 무철 전용 사건은 버려진다.
    // 복원은 기본 복원(IBossStateListener) 뒤에 오고, 끝난 시전은 오지 않는다
    /// <summary> 클라이언트가 받는 무철 전용 사건과 늦은 접속, 재접속 복원 </summary>
    public interface IBossMucheolStateListener : IBossMucheolEventListener
    {
        /// <summary> 진행 중인 콤보나 순차 타격 한 건, Kind로 뷰를 고르고 StruckCount까지는 이미 확정된 타다 </summary>
        void OnStrikeChainRestored(in BossStrikeChainSnapshot snapshot);

        /// <summary> 진행 중인 연속 돌진의 현재 돌진 하나 </summary>
        void OnMultiDashRestored(in BossMultiDashSnapshot snapshot);

        /// <summary> 진행 중인 전장 공격 한 건 </summary>
        void OnBattlefieldRestored(in BossBattlefieldSnapshot snapshot);

        /// <summary> 시전이 아직 끝나지 않은 돌진의 충돌 결과, 회복 표시는 ServerTime과 RecoverySeconds로 이어 그린다 </summary>
        void OnChargeRestored(in BossChargeOutcome outcome);
    }
}
