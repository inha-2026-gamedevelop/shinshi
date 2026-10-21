namespace Shinshi.AI
{
    // 표현 연결부가 IBossStateListener와 함께 구현하면 BossState가 봉인 사건과 복원을 넘긴다. 구현하지 않으면 봉인 사건은 버려진다
    /// <summary> 클라이언트가 받는 무철 방향 봉인 사건과 늦은 접속, 재접속 복원 </summary>
    public interface IBossMucheolSealStateListener : IBossMucheolSealEventListener
    {
        /// <summary> 표식이 나와 있는 봉인의 지금 방향과 타수 </summary>
        void OnSealRestored(EMucheolSealSide side, int hitCount, int requiredHits);
    }
}
