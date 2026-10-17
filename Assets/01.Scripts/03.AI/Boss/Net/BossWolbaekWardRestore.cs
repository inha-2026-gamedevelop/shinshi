namespace Shinshi.AI
{
    // 규칙 스냅샷(WolbaekWardSnapshot)에 없는 진행 중 광역 공격 예고를 더한다. 예고의 시작 시각과 중심이 없으면 표현이 추측해야 한다
    /// <summary> 늦은 접속, 재접속에 넘기는 색 결계의 현재 상태 </summary>
    public struct BossWolbaekWardRestore
    {
        /****************************************
        *              Properties
        ****************************************/

        public WolbaekWardSnapshot Snapshot { get; set; }

        /// <summary> 판정 전인 광역 공격 예고가 있다 </summary>
        public bool HasPendingStrike { get; set; }

        public WolbaekWardStrike PendingStrike { get; set; }
    }
}
