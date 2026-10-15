namespace Shinshi.AI
{
    /// <summary> 진행 중인 동시 원형 폭발의 현재 상태 </summary>
    public struct BossCircleBurstSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossCircleBurstStart Start { get; set; }

        /// <summary> 이미 폭발했다 </summary>
        public bool IsStruck { get; set; }
    }
}
