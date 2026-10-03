namespace Shinshi.AI
{
    // 표현 연결부(View)가 구현해 BossState.SetListener로 넘긴다. 연결하는 순간과 스폰 직후에 진행 중인 공격을 복원으로 먼저 받고,
    // 그 뒤로는 사건을 받는다. 끝난 공격과 반환된 예약은 복원에 오지 않는다
    /// <summary> 클라이언트가 받는 보스 사건과 늦은 접속, 재접속 복원 </summary>
    public interface IBossStateListener : IBossEventListener
    {
        /// <summary> 진행 중인 예고 한 건, 표현은 BossTelegraphPresenter.Restore로 넘긴다 </summary>
        void OnTelegraphRestored(in BossTelegraphSnapshot snapshot);

        /// <summary> 날고 있는 투사체 한 발 </summary>
        void OnProjectileRestored(in BossProjectileSnapshot snapshot);

        /// <summary> 남아 있는 장판 한 개 </summary>
        void OnAreaRestored(in BossAreaSnapshot snapshot);
    }
}
