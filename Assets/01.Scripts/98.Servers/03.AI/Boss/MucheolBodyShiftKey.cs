namespace Shinshi.AI
{
    // 클라이언트 모션 키(MucheolMotionKey)와 같은 규칙이다. 서버가 같은 클립 시간에 같은 몸 위치에 있어야 판정과 보이는 위치가 맞는다
    /// <summary> 패턴 시각 하나를 클립 시각에 묶는 몸 이동 키, 기준은 시전 시작이나 타 확정 시각이다 </summary>
    public readonly struct MucheolBodyShiftKey
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> 시전 시작(예고 시작)을 기준으로 삼는 키의 타 번호 </summary>
        public const int CAST_START = -1;

        /****************************************
        *                Fields
        ****************************************/

        private readonly int   _strikeIndex;
        private readonly float _offsetSeconds;
        private readonly float _clipSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 기준 타 번호, CAST_START면 시전 시작이다 </summary>
        public int StrikeIndex => _strikeIndex;

        /// <summary> 기준 시각에 더하는 초 </summary>
        public float OffsetSeconds => _offsetSeconds;

        /// <summary> 이 시각에 재생 중인 클립 초 </summary>
        public float ClipSeconds => _clipSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolBodyShiftKey(int strikeIndex, float offsetSeconds, float clipSeconds)
        {
            _strikeIndex   = strikeIndex;
            _offsetSeconds = offsetSeconds;
            _clipSeconds   = clipSeconds;
        }

        public static MucheolBodyShiftKey AtCast(float clipSeconds)
        {
            return new MucheolBodyShiftKey(CAST_START, 0f, clipSeconds);
        }

        public static MucheolBodyShiftKey AtStrike(int strikeIndex, float offsetSeconds, float clipSeconds)
        {
            return new MucheolBodyShiftKey(strikeIndex, offsetSeconds, clipSeconds);
        }
    }
}
