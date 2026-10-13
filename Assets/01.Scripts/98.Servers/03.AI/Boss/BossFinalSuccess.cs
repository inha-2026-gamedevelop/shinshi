namespace Shinshi.AI
{
    /// <summary> 하드 0% 최종 성공 신호, 보스 처치와 정산의 중복 방지 키는 전투 식별자다 </summary>
    public readonly struct BossFinalSuccess
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호, 처치와 정산을 멱등 처리하는 기준이다 </summary>
        public int BossCombatId { get; }

        /// <summary> 최종 성공을 확정한 기믹의 시전 번호 </summary>
        public int CastInstanceId { get; }

        /// <summary> 최종 성공이 확정된 서버 시각 </summary>
        public double ServerTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        public BossFinalSuccess(int bossCombatId, int castInstanceId, double serverTime)
        {
            BossCombatId   = bossCombatId;
            CastInstanceId = castInstanceId;
            ServerTime     = serverTime;
        }
    }
}
