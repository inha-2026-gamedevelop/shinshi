namespace Shinshi.Art
{
    // 워밍 재생은 표현 전용이다. 네트워크 송신, 피해, 히트 이벤트, 오디오, 카메라 흔들림을 일으키지 않는다
    /// <summary> 프리워밍 러너가 실제 재생 경로로 한 번 그리게 하는 프리팹 루트 컴포넌트 </summary>
    public interface IPrewarmTarget
    {
        /// <summary> 워밍 표시를 시작한다, 러너 스테이지 안의 현재 위치와 방향을 그대로 쓴다 </summary>
        void BeginPrewarm();

        /// <summary> 시작 뒤 지난 실제 시간(초)으로 표시를 진행한다, 매 프레임 한 번 불린다 </summary>
        void TickPrewarm(float elapsedSeconds);

        /// <summary> 표시를 멈추고 숨긴다, 러너는 이 호출 뒤 인스턴스를 파괴하거나 보관한다 </summary>
        void EndPrewarm();
    }
}
