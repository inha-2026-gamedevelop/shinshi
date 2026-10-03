namespace Shinshi.AI
{
    // 서버가 확정한 별형 빔 예고 값만 담는다. 클라이언트는 이 값으로 예고와 회전을 그릴 뿐 좌우 추첨, 기준 각도, 판정을 다시 계산하지 않는다
    // 별 구조 하나는 구조 번호가 가리키는 키이고 시전 번호는 같은 시전의 묶음이다. 안의 빔은 0부터 번호 순서로 꼭짓점과 이어진다(빔별 식별)
    /// <summary> 별형 빔 구조 하나의 예고 시작 입력 </summary>
    public readonly struct BossStarStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int           _bossId;
        private readonly int           _starId;
        private readonly int           _castId;
        private readonly BossStarShape _shape;
        private readonly double        _startServerTime;
        private readonly float         _telegraphSeconds;
        private readonly float         _durationSeconds;
        private readonly float         _tickInterval;

        /****************************************
        *              Properties
        ****************************************/

        public int BossId => _bossId;

        /// <summary> 별 구조 한 개의 번호, 서버가 발급한 값이다 </summary>
        public int StarId => _starId;

        /// <summary> 이 구조가 속한 시전 번호 </summary>
        public int CastId => _castId;

        public BossStarShape Shape => _shape;

        public double StartServerTime => _startServerTime;

        /// <summary> 예고 길이(초), 서버가 활성을 알리면 회전하는 위험 빔이 시작한다 </summary>
        public float TelegraphSeconds => _telegraphSeconds;

        /// <summary> 활성 뒤 회전과 유지 시간(초) </summary>
        public float DurationSeconds => _durationSeconds;

        /// <summary> 피해 간격(초), 표현이 주기를 알리는 데 쓰며 타격 자체는 서버 알림으로만 표시한다 </summary>
        public float TickInterval => _tickInterval;

        public double TelegraphEndServerTime => _startServerTime + _telegraphSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossStarStart(int bossId, int starId, int castId, in BossStarShape shape, double startServerTime,
            float telegraphSeconds, float durationSeconds, float tickInterval)
        {
            _bossId           = bossId;
            _starId           = starId;
            _castId           = castId;
            _shape            = shape;
            _startServerTime  = startServerTime;
            _telegraphSeconds = telegraphSeconds;
            _durationSeconds  = durationSeconds;
            _tickInterval     = tickInterval;
        }
    }
}
