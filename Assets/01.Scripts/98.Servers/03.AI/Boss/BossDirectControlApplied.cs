namespace Shinshi.AI
{
    // 제어가 보스에게 실제로 적용됐다는 사실을 알린다. 허용 구간이 아니거나 이미 받았거나 면역이라 버려진 제어는 알리지 않는다
    // 속박 등 취소형이 아닌 제어의 보스 동작은 협의 전이라 이 사실을 받는 쪽에서 나중에 붙인다
    /// <summary> 보스에 적용된 직접 제어의 결과 </summary>
    public readonly struct BossDirectControlApplied
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossDirectControl _control;
        private readonly float             _appliedSeconds;
        private readonly bool              _cancelledPattern;
        private readonly string            _patternId;

        /****************************************
        *              Properties
        ****************************************/

        public BossDirectControl Control => _control;

        /// <summary> 요청 시간을 보스 정의의 직접 제어 최대 시간으로 자른 값, 요청이 없었으면 0 </summary>
        public float AppliedSeconds => _appliedSeconds;

        /// <summary> 이 제어로 진행 중이던 패턴이 취소됐다 </summary>
        public bool CancelledPattern => _cancelledPattern;

        /// <summary> 제어가 적용된 시점에 진행 중이던 패턴의 정의 ID </summary>
        public string PatternId => _patternId;

        /****************************************
        *                Methods
        ****************************************/

        public BossDirectControlApplied(BossDirectControl control, float appliedSeconds, bool cancelledPattern, string patternId)
        {
            _control          = control;
            _appliedSeconds   = appliedSeconds;
            _cancelledPattern = cancelledPattern;
            _patternId        = patternId;
        }
    }
}
