namespace Shinshi.AI
{
    /// <summary> 진행 중인 단계형 별 빔의 현재 상태 </summary>
    public struct BossStarStageSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossStarStageStart Start { get; set; }

        /// <summary> 이미 타격한 단계 수 </summary>
        public int StruckCount { get; set; }
    }
}
