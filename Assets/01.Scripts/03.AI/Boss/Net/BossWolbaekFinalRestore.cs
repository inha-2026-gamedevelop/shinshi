namespace Shinshi.AI
{
    // 노란 여우불 체력은 사건으로 보내지 않아 살아 있으면 최대 체력, 부서졌으면 0이다.
    // 고정 전 빨간 여우불 위치는 떨어져 나간 것만 있고 나머지는 NaN이며, 표현은 NaN 자리를 대상 참가자의 발밑으로 그린다
    /// <summary> 늦은 접속, 재접속에 넘기는 최후의 영역의 현재 상태 </summary>
    public struct BossWolbaekFinalRestore
    {
        /****************************************
        *              Properties
        ****************************************/

        public WolbaekFinalSnapshot Snapshot { get; set; }
    }
}
