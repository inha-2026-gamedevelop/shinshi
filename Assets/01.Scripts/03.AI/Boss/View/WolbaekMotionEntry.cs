namespace Shinshi.AI
{
    // 시험 씬(Dev_WolbaekAppearPreview)에서 확정한 패턴 하나의 모션 값이다. 서버 판정 시각은 사건이 알려 주므로 여기에 두지 않는다
    /// <summary> 패턴 하나의 월백 모션 상태 이름과 힘이 실리는 순간과 느리게 늘리는 구간 </summary>
    public readonly struct WolbaekMotionEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _stateName;
        private readonly float  _clipPeakSeconds;
        private readonly float  _forcedSpeed;
        private readonly float  _slowFromClip;
        private readonly float  _slowToClip;
        private readonly float  _slowSpeed;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 컨트롤러 상태 이름 </summary>
        public string StateName => _stateName;

        /// <summary> 초, 클립에서 가장 힘이 실리는 순간 </summary>
        public float ClipPeakSeconds => _clipPeakSeconds;

        /// <summary> 0보다 크면 이 재생 속도로 고정한다 </summary>
        public float ForcedSpeed => _forcedSpeed;

        /// <summary> 초, 느리게 늘리는 클립 구간의 시작과 끝, 속도가 0이면 쓰지 않는다 </summary>
        public float SlowFromClip => _slowFromClip;

        public float SlowToClip => _slowToClip;

        /// <summary> 느리게 늘리는 구간의 재생 속도, 0이면 구간이 없다 </summary>
        public float SlowSpeed => _slowSpeed;

        public bool HasSlowSegment => _slowSpeed > 0f;

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekMotionEntry(string stateName, float clipPeakSeconds, float forcedSpeed = 0f, float slowFromClip = 0f,
            float slowToClip = 0f, float slowSpeed = 0f)
        {
            _stateName       = stateName;
            _clipPeakSeconds = clipPeakSeconds;
            _forcedSpeed     = forcedSpeed;
            _slowFromClip    = slowFromClip;
            _slowToClip      = slowToClip;
            _slowSpeed       = slowSpeed;
        }
    }
}
