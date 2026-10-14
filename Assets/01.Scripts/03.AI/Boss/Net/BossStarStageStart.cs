namespace Shinshi.AI
{
    // 단계 k의 각도는 StepDegrees x k, 타격 예정 시각은 CastStartServerTime + FirstStrikeSeconds + k x StrikeIntervalSeconds다
    /// <summary> 하드 2페이즈 P01 단계형 별 빔 한 시전의 입력 </summary>
    public readonly struct BossStarStageStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int           _bossId;
        private readonly int           _castId;
        private readonly int           _starId;
        private readonly BossStarShape _shape;
        private readonly float         _stepDegrees;
        private readonly int           _stageCount;
        private readonly double        _castStartServerTime;
        private readonly float         _firstStrikeSeconds;
        private readonly float         _strikeIntervalSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 서버가 발급한 구조 번호 </summary>
        public int StarId => _starId;

        /// <summary> 별 구조의 배치 </summary>
        public BossStarShape Shape => _shape;

        /// <summary> 단계마다 도는 각도(도) </summary>
        public float StepDegrees => _stepDegrees;

        public int StageCount => _stageCount;

        public double CastStartServerTime => _castStartServerTime;

        public float FirstStrikeSeconds => _firstStrikeSeconds;

        public float StrikeIntervalSeconds => _strikeIntervalSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossStarStageStart(int bossId, int castId, int starId, BossStarShape shape, float stepDegrees,
            int stageCount, double castStartServerTime, float firstStrikeSeconds, float strikeIntervalSeconds)
        {
            _bossId                = bossId;
            _castId                = castId;
            _starId                = starId;
            _shape                 = shape;
            _stepDegrees           = stepDegrees;
            _stageCount            = stageCount;
            _castStartServerTime   = castStartServerTime;
            _firstStrikeSeconds    = firstStrikeSeconds;
            _strikeIntervalSeconds = strikeIntervalSeconds;
        }
    }
}
