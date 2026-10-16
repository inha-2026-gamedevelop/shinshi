namespace Shinshi.AI
{
    // 프리뷰(Dev_MucheolMotionVfxPreview)의 MotionCue 시간 키를 서버 기준 시각과 그 뒤 오프셋으로 다시 적은 값이다
    /// <summary> 무철 모션 시간 키 하나, 기준 시각에서 Offset초 뒤에 클립이 ClipSeconds에 있어야 한다 </summary>
    public readonly struct MucheolMotionKey
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EMucheolMotionAnchor _anchor;
        private readonly int                  _strikeIndex;
        private readonly float                _offset;
        private readonly float                _clipSeconds;

        /****************************************
        *              Properties
        ****************************************/

        public EMucheolMotionAnchor Anchor => _anchor;

        /// <summary> Anchor가 Strike일 때 몇 번째 타인가 </summary>
        public int StrikeIndex => _strikeIndex;

        /// <summary> 초, 기준 시각에서 더하는 시간, 음수면 기준보다 앞이다 </summary>
        public float Offset => _offset;

        /// <summary> 초, 이 시각의 클립 시간 </summary>
        public float ClipSeconds => _clipSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolMotionKey(EMucheolMotionAnchor anchor, float offset, float clipSeconds, int strikeIndex = 0)
        {
            _anchor      = anchor;
            _strikeIndex = strikeIndex;
            _offset      = offset;
            _clipSeconds = clipSeconds;
        }

        /// <summary> strikeIndex번째 타 확정 시각에서 offset초 뒤의 키 </summary>
        public static MucheolMotionKey AtStrike(int strikeIndex, float offset, float clipSeconds)
        {
            return new MucheolMotionKey(EMucheolMotionAnchor.Strike, offset, clipSeconds, strikeIndex);
        }
    }
}
