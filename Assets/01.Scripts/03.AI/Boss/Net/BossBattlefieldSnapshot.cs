namespace Shinshi.AI
{
    /// <summary> 진행 중인 전장 공격의 현재 상태 </summary>
    public struct BossBattlefieldSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public BossBattlefieldStart Start { get; set; }

        /// <summary> 타격이 이미 확정됐다 </summary>
        public bool IsStruck { get; set; }

        public double StruckServerTime { get; set; }
    }
}
