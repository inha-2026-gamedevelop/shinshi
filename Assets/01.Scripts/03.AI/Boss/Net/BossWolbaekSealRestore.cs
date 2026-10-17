namespace Shinshi.AI
{
    // 규칙 스냅샷(WolbaekSealSnapshot)에 지금 차례의 핵 파괴 결과를 더한다. 판정 전이면 0이다.
    // 추적 중 여우불 위치는 서버가 틱마다 바꾸는 값이라 사건으로 보내지 않는다. 떨어져 나간 것과 고정된 것만 위치가 있고 나머지는 NaN이며,
    // 표현은 NaN 자리를 대상 참가자의 발밑으로 그린다
    /// <summary> 늦은 접속, 재접속에 넘기는 여우불 봉인의 현재 상태 </summary>
    public struct BossWolbaekSealRestore
    {
        /****************************************
        *              Properties
        ****************************************/

        public WolbaekSealSnapshot Snapshot { get; set; }

        /// <summary> 지금 차례에서 파괴된 핵, i번째 비트가 i번 핵이다 </summary>
        public byte DestroyedCoreMask { get; set; }
    }
}
