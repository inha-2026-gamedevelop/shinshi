namespace Shinshi.Network
{
    /// <summary> 게이트웨이 회로 상태 </summary>
    public enum EBackendCircuitState
    {
        Closed   = 0,

        // 호출하지 않고 바로 실패시킨다
        Open     = 1,

        // 열린 시간이 지나 시험 호출 한 건만 보낸 상태
        HalfOpen = 2
    }
}
