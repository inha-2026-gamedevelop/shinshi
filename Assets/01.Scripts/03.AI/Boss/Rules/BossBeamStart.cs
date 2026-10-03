namespace Shinshi.AI
{
    // 서버가 확정한 빔 예고 값만 담는다. 클라이언트는 이 값으로 예고와 지속 빔을 그릴 뿐 배치와 판정을 다시 계산하지 않는다
    // 빔 한 줄은 빔 번호가 가리키는 키다. 시전 번호는 같은 시전의 빔들이 함께 쓰는 묶음이라 한 빔의 알림이 다른 빔을 덮어쓰거나 지우지 않는다
    /// <summary> 빔 한 줄의 예고 시작 입력 </summary>
    public readonly struct BossBeamStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int           _bossId;
        private readonly int           _beamId;
        private readonly int           _castId;
        private readonly BossBeamShape _shape;
        private readonly double        _startServerTime;
        private readonly float         _telegraphSeconds;
        private readonly float         _durationSeconds;
        private readonly float         _tickInterval;

        /****************************************
        *              Properties
        ****************************************/

        public int BossId => _bossId;

        /// <summary> 빔 한 줄의 번호, 서버가 발급한 값이다 </summary>
        public int BeamId => _beamId;

        /// <summary> 이 빔이 속한 시전 번호, 한 시전이 설치한 빔들의 묶음이다 </summary>
        public int CastId => _castId;

        public BossBeamShape Shape => _shape;

        /// <summary> 예고가 시작된 서버 시각 </summary>
        public double StartServerTime => _startServerTime;

        /// <summary> 예고 길이(초), 서버가 활성을 알리면 지속 빔이 시작한다 </summary>
        public float TelegraphSeconds => _telegraphSeconds;

        /// <summary> 활성 뒤 유지 시간(초) </summary>
        public float DurationSeconds => _durationSeconds;

        /// <summary> 피해 간격(초), 표현이 주기를 알리는 데 쓰며 타격 자체는 서버 알림으로만 표시한다 </summary>
        public float TickInterval => _tickInterval;

        /// <summary> 예고가 끝나는 서버 시각 </summary>
        public double TelegraphEndServerTime => _startServerTime + _telegraphSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossBeamStart(int bossId, int beamId, int castId, in BossBeamShape shape, double startServerTime,
            float telegraphSeconds, float durationSeconds, float tickInterval)
        {
            _bossId           = bossId;
            _beamId           = beamId;
            _castId           = castId;
            _shape            = shape;
            _startServerTime  = startServerTime;
            _telegraphSeconds = telegraphSeconds;
            _durationSeconds  = durationSeconds;
            _tickInterval     = tickInterval;
        }
    }
}
