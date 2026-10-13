namespace Shinshi.AI
{
    // 시험 구동기가 쓰는 부적진 기믹의 임시 시작값이다. 기획서의 초기값을 그대로 옮겼고 플레이 시험 전 값이다.
    // 운영 서버는 이 값을 쓰지 않는다. 운영 데이터(GameDB)로 옮기기 전까지 이 파일이 시험용 값의 유일한 자리다.
    // 이지는 세부 공격 구성이 정해지지 않아 보스 공격 없이 자리 유지만 있는 설정이다. 하드 값을 이지에 그대로 쓰지 않는다
    /// <summary> 부적진 기믹의 시험용 설정 생성 </summary>
    public static class WolbaekTalismanDevCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float TRIGGER_HEALTH_RATE  = 0.75f;
        public const float TRANSITION_SECONDS   = 3f;
        public const float HOLD_SECONDS         = 5f;
        public const float COUNTER_SECONDS      = 6f;
        public const float STRUCTURE_DISTANCE   = 10f;
        public const float STRUCTURE_RADIUS     = 3f;

        private const float INNER_RING_MIN      = 7f;
        private const float RING_SPLIT          = 10f;
        private const float OUTER_RING_MAX      = 13f;
        private const float LINE_WIDTH          = 3f;
        private const float TELEGRAPH_SECONDS   = 1.2f;
        private const float ATTACK_INTERVAL     = 2f;
        private const float DAMAGE_REF          = 0.12f;
        private const float PUSH_DISTANCE       = 1f;

        /****************************************
        *                Methods
        ****************************************/

        // nextBoundaryHealthRate는 반격 시간 중 보스가 더 내려가지 못하는 다음 경계다. 월백은 2페이즈 전환 비율을 넘긴다
        public static WolbaekTalismanSpec CreateSpec(bool isHard, float nextBoundaryHealthRate)
        {
            WolbaekTalismanAttackSpec attack = isHard
                ? new WolbaekTalismanAttackSpec(INNER_RING_MIN, RING_SPLIT, OUTER_RING_MAX, LINE_WIDTH, TELEGRAPH_SECONDS,
                    ATTACK_INTERVAL, DAMAGE_REF, PUSH_DISTANCE, true)
                : null;

            return new WolbaekTalismanSpec(TRIGGER_HEALTH_RATE, nextBoundaryHealthRate, TRANSITION_SECONDS, HOLD_SECONDS,
                COUNTER_SECONDS, STRUCTURE_DISTANCE, STRUCTURE_RADIUS, attack);
        }
    }
}
