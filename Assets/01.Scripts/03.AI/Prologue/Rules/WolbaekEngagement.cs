namespace Shinshi.AI
{
    /// <summary> 월백 교전의 최대 30초와 종료 조건, 시각은 호출자가 넘기는 서버 시각이라 틱 할당이 없다 </summary>
    public sealed class WolbaekEngagement
    {
        /****************************************
        *                Fields
        ****************************************/

        public const double DURATION_SECONDS = 30.0;

        private double      _startTime = 0.0;
        private bool        _started   = false;
        private EWolbaekEnd _end       = EWolbaekEnd.None;

        /****************************************
        *              Properties
        ****************************************/

        public bool        IsRunning => (_started) && (_end == EWolbaekEnd.None);
        public EWolbaekEnd End       => _end;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 등장 연출이 끝나 조작이 가능해진 순간에 부른다, 두 번째 호출은 무시한다 </summary>
        public void Begin(double now)
        {
            if (_started)
            {
                return;
            }

            _started   = true;
            _startTime = now;
        }

        /// <summary> 공격하지 않아도 시간은 흐른다, 30초가 지나면 끝난다. 이미 끝났으면 먼저 정해진 이유를 돌려준다 </summary>
        public EWolbaekEnd Update(double now)
        {
            if ((!_started) || (_end != EWolbaekEnd.None))
            {
                return _end;
            }

            if ((now - _startTime) >= DURATION_SECONDS)
            {
                _end = EWolbaekEnd.TimeUp;
            }

            return _end;
        }

        /// <summary> 체력 0이면 재도전 없이 끝난다, 30초 종료가 먼저였다면 그 이유를 유지한다 </summary>
        public EWolbaekEnd ReportPlayerDown(double now)
        {
            if (Update(now) != EWolbaekEnd.None)
            {
                return _end;
            }

            if (_started)
            {
                _end = EWolbaekEnd.PlayerDown;
            }

            return _end;
        }
    }
}
