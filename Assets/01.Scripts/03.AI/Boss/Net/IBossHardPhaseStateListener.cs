namespace Shinshi.AI
{
    // 표현 연결부가 IBossStateListener와 함께 구현하면 BossState가 하드 2페이즈 사건과 복원을 넘긴다. 구현하지 않으면 버려진다.
    // 복원은 기본 복원(IBossStateListener) 뒤에 오고, 끝난 시전은 오지 않는다
    /// <summary> 클라이언트가 받는 월백 하드 2페이즈 사건과 늦은 접속, 재접속 복원 </summary>
    public interface IBossHardPhaseStateListener : IBossHardPhaseEventListener
    {
        /// <summary> 진행 중인 P01, StruckCount는 이미 타격한 단계 수다 </summary>
        void OnStarStageRestored(in BossStarStageSnapshot snapshot);

        /// <summary> 진행 중인 P02, 예고한 줄 수와 타격한 줄 수를 함께 준다 </summary>
        void OnGridBeamRestored(in BossGridBeamSnapshot snapshot);

        /// <summary> 진행 중이거나 잔류 위험이 남은 P03 </summary>
        void OnChainFanRestored(in BossChainFanSnapshot snapshot);

        /// <summary> 활성 전인 P04 추적 장판 하나, Center는 최근 중심이다 </summary>
        void OnTrackingAreaRestored(in BossTrackingAreaSnapshot snapshot);

        /// <summary> 진행 중인 P05, Shots의 FiredCount번째부터가 아직 나가지 않은 발이다 </summary>
        void OnBarrageRestored(in BossBarrageSnapshot snapshot);

        /// <summary> 진행 중인 P06 </summary>
        void OnCircleBurstRestored(in BossCircleBurstSnapshot snapshot);
    }
}
