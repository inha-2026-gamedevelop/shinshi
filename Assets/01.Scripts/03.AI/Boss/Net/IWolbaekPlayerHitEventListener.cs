namespace Shinshi.AI
{
    // 서버 몸체(BossServer)가 월백 공격에 맞은 대상 한 명을 복제 상태(BossState)로 넘기고, 상태가 같은 형태로 클라이언트 수신부에 넘기는 선택 계약이다.
    // 표시용 일회성 사건이라 진행 중 기록에 남기지 않고 늦은 접속과 재접속에서 다시 재생하지 않는다. 수신부가 구현하지 않으면 사건은 버려진다
    /// <summary> 월백 공격에 맞은 대상 한 명의 임팩트 사건 </summary>
    public interface IWolbaekPlayerHitEventListener
    {
        /// <summary> 서버가 피해 요청을 낸 대상 한 명이다, 대상마다 한 건씩 온다 </summary>
        void OnPlayerHit(in WolbaekPlayerHit hit);
    }
}
