namespace Shinshi.AI
{
    // 줄은 발사 순서대로 예고되고 타격되므로 수만 있으면 어느 줄까지인지 정해진다
    /// <summary> 진행 중인 격자 순차 빔의 현재 상태 </summary>
    public struct BossGridBeamSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossGridBeamStart Start { get; set; }

        /// <summary> 강조 예고를 시작한 줄 수 </summary>
        public int TelegraphedCount { get; set; }

        /// <summary> 타격한 줄 수 </summary>
        public int StruckCount { get; set; }
    }
}
