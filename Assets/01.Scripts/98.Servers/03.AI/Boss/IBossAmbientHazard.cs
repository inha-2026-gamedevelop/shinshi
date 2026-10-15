namespace Shinshi.AI
{
    // 하드 상시 소형 낙하 여우불처럼 패턴 선택과 별개로 계속 도는 환경 위험물을 기믹이 멈추고 다시 켜는 창구다.
    // 그런 위험물은 아직 구현되지 않았다. 구현이 생기면 이 계약을 구현해 기믹에 넘긴다
    /// <summary> 상시 환경 위험물의 정지와 재개 </summary>
    public interface IBossAmbientHazard
    {
        /// <summary> 새 생성을 멈추고 이미 예고하거나 예약한 타격을 정리한다 </summary>
        void Suspend();

        /// <summary> 정해진 서버 시각부터 다시 생성한다, 이미 도는 중이면 무시한다 </summary>
        void Resume(double serverTime);
    }
}
