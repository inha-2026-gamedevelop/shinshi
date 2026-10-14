namespace Shinshi.AI
{
    // Shots는 지금까지 예고한 발을 발 번호 순서로 담는다. 발사는 순서대로 처리되므로 FiredCount번째부터가 아직 나가지 않은 발이다
    /// <summary> 진행 중인 전방위 난사의 현재 상태 </summary>
    public struct BossBarrageSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossBarrageStart Start { get; set; }

        /// <summary> 발사 처리가 끝난 발 수 </summary>
        public int FiredCount { get; set; }

        /// <summary> 예고한 발 목록, 없으면 빈 배열이다 </summary>
        public BossBarrageShot[] Shots { get; set; }
    }
}
