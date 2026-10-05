namespace Shinshi.Network
{
    // 던전 세션은 종료 판정 직후, 종료 이벤트(SessionEnded)를 저장 큐에 넣기 전에 부른다. 이 안에서 넣은 이벤트는 종료 이벤트보다
    // 앞 순번을 받아 같은 배치에서도 API가 먼저 처리한다. 종료 뒤에는 세션이 이벤트를 받지 않는다(API가 SESSION_TERMINAL로 거절해
    // 저장 큐가 멈춘다). 전투 런타임이 구현한다
    /// <summary> 출정 종료 직전 마지막 이벤트를 넣을 기회, 서버 메인 스레드에서 출정마다 한 번 부른다 </summary>
    public interface ISessionEndingListener
    {
        /// <summary> 아직 진행 중으로 보이는 마지막 순간이다. 저장 큐가 멈췄으면 넣기가 거절된다 </summary>
        void OnSessionEnding(double serverTime);
    }
}
