namespace Shinshi.AI
{
    /// <summary> 결계 위험 구역의 표시 단계 </summary>
    public enum EBossBarrierPhase
    {
        /// <summary> 서버가 예고를 시작하기 전이거나 정리된 뒤다 </summary>
        None      = 0,

        /// <summary> 위험 구역을 미리 보여 주는 중, 피해는 없다 </summary>
        Telegraph = 1,

        /// <summary> 지속 위험 구역이 활성됐다, 전투 종료나 리셋까지 유지한다 </summary>
        Active    = 2
    }
}
