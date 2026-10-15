namespace Shinshi.AI
{
    /// <summary> 진행 중인 잔류 부채꼴 연쇄의 현재 상태 </summary>
    public struct BossChainFanSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossChainFanStart Start { get; set; }

        /// <summary> 폭발한 구역 수 </summary>
        public int BlastedCount { get; set; }

        /// <summary> 잔류 위험이 끝났다 </summary>
        public bool IsDangerEnded { get; set; }
    }
}
