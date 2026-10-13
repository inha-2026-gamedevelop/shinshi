namespace Shinshi.AI
{
    // 시험 구동기와 시험이 쓰는 여우불 유도와 봉인 핵 기믹의 임시 시작값이다. 기획서가 정한 값은 그대로 옮겼고 플레이 시험 전 값이다.
    // 기획이 정하지 않은 값(핵 사이 최소 간격, 하드 휩쓸기의 범위와 각도)은 검증 가능한 임시 개발값이며 운영 값이 아니다.
    // 운영 서버는 이 값을 쓰지 않는다. 운영 데이터(GameDB)로 옮기기 전까지 이 파일이 시험용 값의 유일한 자리다
    /// <summary> 여우불 유도와 봉인 핵 기믹의 시험용 설정 생성 </summary>
    public static class WolbaekFoxfireSealDevCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float TRIGGER_HEALTH_RATE = 0.25f;
        public const int   REQUIRED_SUCCESSES  = 3;
        public const float TIME_LIMIT_SECONDS  = 60f;

        // 하드는 반격 중 0%에서 멈추고 0% 기믹에 넘기며, 이지는 다음 경계가 없다
        private const float HARD_NEXT_BOUNDARY   = 0f;
        private const float PREPARE_SECONDS      = 3f;
        private const float TRACK_SECONDS        = 4f;
        private const float ESCAPE_SECONDS       = 2f;
        private const float CLEANUP_SECONDS      = 1f;
        private const float BLAST_RADIUS         = 1.5f;
        private const float CORE_HIT_RADIUS      = 1.5f;
        private const float BLAST_DAMAGE_RATE    = 0.25f;
        private const float COUNTER_SECONDS      = 6f;

        // 임시 개발값, 기획 미정
        private const float DEV_MIN_CORE_SEPARATION = 6f;
        private const float DEV_SWEEP_RANGE         = 16f;
        private const float DEV_SWEEP_HALF_ANGLE    = 60f;

        // 기획 확정값
        private const float SWEEP_FIRST_HIT_SECONDS  = 1.5f;
        private const float SWEEP_SECOND_HIT_SECONDS = 3f;
        private const float SWEEP_TELEGRAPH_SECONDS  = 0.8f;
        private const float SWEEP_DAMAGE_RATE        = 0.25f;
        private const float SWEEP_PUSH_DISTANCE      = 1f;

        /****************************************
        *                Methods
        ****************************************/

        public static WolbaekFoxfireSealSpec CreateSpec(bool isHard)
        {
            WolbaekSealSweepSpec sweep = isHard ? CreateSweep() : null;
            float? next = isHard ? HARD_NEXT_BOUNDARY : (float?)null;

            return new WolbaekFoxfireSealSpec(TRIGGER_HEALTH_RATE, next, PREPARE_SECONDS, TRACK_SECONDS, ESCAPE_SECONDS,
                CLEANUP_SECONDS, REQUIRED_SUCCESSES, TIME_LIMIT_SECONDS, BLAST_RADIUS, CORE_HIT_RADIUS, BLAST_DAMAGE_RATE,
                DEV_MIN_CORE_SEPARATION, COUNTER_SECONDS, sweep);
        }

        public static WolbaekSealSweepSpec CreateSweep()
        {
            return new WolbaekSealSweepSpec(new[] { SWEEP_FIRST_HIT_SECONDS, SWEEP_SECOND_HIT_SECONDS }, SWEEP_TELEGRAPH_SECONDS,
                SWEEP_DAMAGE_RATE, SWEEP_PUSH_DISTANCE, DEV_SWEEP_RANGE, DEV_SWEEP_HALF_ANGLE);
        }
    }
}
