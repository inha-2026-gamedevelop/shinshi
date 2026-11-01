namespace Shinshi.AI
{
    // 시험 구동기와 시험이 쓰는 최후의 영역 기믹의 임시 시작값이다. 기획서가 정한 값은 그대로 옮겼고 플레이 시험 전 값이다.
    // 기획이 정하지 않은 값(노란 여우불 몸체 반경, 묶음 사이 간격)은 검증 가능한 임시 개발값이며 운영 값이 아니다.
    // 운영 서버는 이 값을 쓰지 않는다. 운영 데이터(GameDB)로 옮기기 전까지 이 파일이 시험용 값의 유일한 자리다
    /// <summary> 최후의 영역 기믹의 시험용 설정 생성 </summary>
    public static class WolbaekFinalAreaDevCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        // 기획 확정값
        public const float CENTRAL_RADIUS = 8f;
        public const float YELLOW_HEALTH  = 8000f;

        private const float COLLAPSE_SECONDS     = 4f;
        private const int   YELLOW_ONE_LIVING    = 2;
        private const int   YELLOW_TWO_LIVING    = 3;
        private const int   YELLOW_THREE_LIVING  = 4;
        private const int   YELLOW_FOUR_LIVING   = 4;
        private const float YELLOW_DEFENSE       = 0f;
        private const float RED_TRACK_SECONDS    = 4f;
        private const float RED_FUSE_SECONDS     = 2f;
        private const float RED_BLAST_RADIUS     = 2f;
        private const float RED_SINGLE_RATE      = 0.6f;
        private const float REVIVE_PROTECT_SECONDS = 3f;
        private const bool  HAS_SPACE_SHRINK       = false; // 2페이즈 붕괴로 이미 좁아진 맵을 쓴다

        // 임시 개발값, 기획 미정
        private const float DEV_YELLOW_BODY_RADIUS = 1f;
        private const float DEV_BUNDLE_GAP_SECONDS = 0f;

        /****************************************
        *                Methods
        ****************************************/

        public static WolbaekFinalAreaSpec CreateSpec()
        {
            int[] counts = { YELLOW_ONE_LIVING, YELLOW_TWO_LIVING, YELLOW_THREE_LIVING, YELLOW_FOUR_LIVING };
            return new WolbaekFinalAreaSpec(COLLAPSE_SECONDS, CENTRAL_RADIUS, counts, YELLOW_HEALTH, YELLOW_DEFENSE,
                DEV_YELLOW_BODY_RADIUS, RED_TRACK_SECONDS, RED_FUSE_SECONDS, RED_BLAST_RADIUS, RED_SINGLE_RATE,
                DEV_BUNDLE_GAP_SECONDS, REVIVE_PROTECT_SECONDS, HAS_SPACE_SHRINK);
        }
    }
}
