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

        /// <summary> 예고 중이거나 활성된 빔 한 줄, 표현은 BossTelegraphPresenter.RestoreBeam으로 넘긴다 </summary>
        void OnBeamRestored(in BossBeamSnapshot snapshot);

        /// <summary> 예고 중이거나 활성된 별형 빔 구조 하나, 표현은 BossTelegraphPresenter.RestoreStar로 넘긴다 </summary>
        void OnStarRestored(in BossStarSnapshot snapshot);

        // 단계는 받은 시각과 서버 시각으로 정해지므로 시작 값 하나만 넘긴다
        /// <summary> 예고 중이거나 활성된 결계 위험 구역, 표현은 BossBarrierView.Begin으로 넘긴다 </summary>
        void OnBarrierRestored(in BossBarrierStart start);
    }
}
