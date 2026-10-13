namespace Shinshi.AI
{
    // 시험 구동기가 쓰는 색 결계 기믹의 임시 시작값이다. 기획서의 초기값과 제안값을 그대로 옮겼고 플레이 시험 전 값이다.
    // 운영 서버는 이 값을 쓰지 않는다. 운영 데이터(GameDB)로 옮기기 전까지 이 파일이 시험용 값의 유일한 자리다.
    // 결계 후보 좌표(WolbaekWardDevSites)는 실제 맵 검증을 거친 값이 아니라 시험 전장 중심 둘레의 임시 배치다. 운영 좌표가 아니다
    /// <summary> 색 결계 기믹의 시험용 설정 생성 </summary>
    public static class WolbaekColorWardDevCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float TRIGGER_HEALTH_RATE = 0.5f;

        private const float GATHER_RADIUS        = 5f;
        public const float GATHER_HOLD_SECONDS  = 8f;
        private const float WARD_RADIUS          = 3f;
        public const int   STAGE_COUNT          = 3;
        private const float SEARCH_SECONDS       = 7f;
        private const float TELEGRAPH_SECONDS    = 1.5f;
        private const int   STRIKE_COUNT         = 3;
        private const float STRIKE_INTERVAL      = 1.5f;
        private const float STRIKE_DAMAGE_RATE   = 0.3f;
        private const float STRIKE_PUSH_DISTANCE = 1f;
        private const float MAX_SITE_TRAVEL      = 15f;
        private const float MIN_WARD_SEPARATION  = 8f;
        private const float LINK_WARN_DISTANCE   = 3f;
        private const float LINK_DAMAGE_DISTANCE = 4f;
        private const float LINK_TICK_SECONDS    = 1f;
        private const float LINK_TIER_ONE_UPPER  = 5f;
        private const float LINK_TIER_TWO_UPPER  = 6f;
        private const float LINK_TIER_ONE_RATE   = 0.05f;
        private const float LINK_TIER_TWO_RATE   = 0.1f;
        private const float LINK_TIER_THREE_RATE = 0.2f;
        private const float REJOIN_GRACE_SECONDS = 8f;

        /****************************************
        *                Methods
        ****************************************/

        public static WolbaekColorWardSpec CreateSpec()
        {
            return new WolbaekColorWardSpec(TRIGGER_HEALTH_RATE, GATHER_RADIUS, GATHER_HOLD_SECONDS, WARD_RADIUS, STAGE_COUNT,
                SEARCH_SECONDS, TELEGRAPH_SECONDS, STRIKE_COUNT, STRIKE_INTERVAL, STRIKE_DAMAGE_RATE, STRIKE_PUSH_DISTANCE,
                MAX_SITE_TRAVEL, MIN_WARD_SEPARATION, LINK_WARN_DISTANCE, LINK_DAMAGE_DISTANCE, LINK_TICK_SECONDS,
                new[] { LINK_TIER_ONE_UPPER, LINK_TIER_TWO_UPPER },
                new[] { LINK_TIER_ONE_RATE, LINK_TIER_TWO_RATE, LINK_TIER_THREE_RATE }, REJOIN_GRACE_SECONDS);
        }
    }
}
