namespace Shinshi.AI
{
    // 별형 빔 구조의 활성과 타격과 소멸 알림이다. 복제 계층이 클라이언트로 전달하는 구성을 가정하며 구현은 아직 없다
    // 클라이언트는 피해와 대상 조회를 다시 계산하지 않고, 배치와 회전 방향과 속도는 예고 알림(IBossStarTelegraphListener)이 준 값을 쓴다
    // 회전각은 보내지 않는다. 활성 서버 시각부터 클라이언트가 서버 시각으로 계산한다
    /// <summary> 별형 빔의 활성, 타격 회차, 소멸 시점 알림 </summary>
    public interface IBossStarListener
    {
        /// <summary> 활성, 이 서버 시각부터 duration 동안 회전하고 tickInterval마다 타격한다. 활성 순간이 첫 타격이다 </summary>
        void OnStarActivated(int castInstanceId, int starId, float durationSeconds, float tickInterval, double activateServerTime);

        /// <summary> 타격 회차 하나가 끝났다, hitIndex는 0부터이고 roundServerTime은 그 회차가 도래한 시각이다. hitCount는 피해 요청이 실제로 나간 대상 수다 </summary>
        void OnStarTick(int castInstanceId, int starId, int hitIndex, double roundServerTime, int hitCount);

        /// <summary> 소멸, Expired는 지속 시간 종료(추가 타격 없음), Cancelled는 즉시 제거다 </summary>
        void OnStarEnded(int castInstanceId, int starId, EBossAreaEnd end, double endServerTime);
    }
}
