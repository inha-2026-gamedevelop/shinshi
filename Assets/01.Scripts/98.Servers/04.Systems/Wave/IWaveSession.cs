namespace Shinshi.Systems
{
    // 세션 바인딩은 이 계약만 부르고 웨이브 구현 클래스를 고치지 않는다.
    // 시간을 인자로 받는 이유는 Mirror 시계에 묶이지 않아야 EditMode 테스트가 420초를 바로 돌릴 수 있어서다
    /// <summary> 일반전 6웨이브 진행과 제한 시간 판정, 세션 바인딩이 시작하고 서버 시각으로 진행시킨다 </summary>
    public interface IWaveSession
    {
        /// <summary> 시작했고 아직 끝나지 않았다 </summary>
        bool IsRunning { get; }

        /// <summary> 성공, 시간 초과, Abort 중 하나로 끝났다 </summary>
        bool IsFinished { get; }

        /// <summary> HUD 동기화용 현재 진행 값 </summary>
        WaveProgressSnapshot Progress { get; }

        /// <summary> 결과 통지를 받을 대상을 정한다, Begin 전에 한 번 부른다 </summary>
        void SetListener(IWaveSessionListener listener);

        /// <summary> 전투를 시작한다, 출정이 Running에 들어간 서버 시각을 넘기며 제한 시간은 이 시각부터 잰다 </summary>
        void Begin(double serverTime);

        // 마지막 처치와 제한 시간 도달이 같은 틱이면 성공을 먼저 판정한다(2026-10-01 결정). 구현체는 처치 반영 뒤 시간 초과를 본다
        /// <summary> 서버 시각으로 예정 출현과 제한 시간을 진행한다, 시작 전이거나 끝난 뒤에는 아무것도 하지 않는다 </summary>
        void Tick(double serverTime);

        /// <summary> 서비스 장애 취소처럼 바깥 원인으로 멈춘다, 결과 통지를 보내지 않고 스폰 디렉터도 멈춘다 </summary>
        void Abort();
    }
}
