namespace Shinshi.Network
{
    // 고정 창 방식이다. 창 경계에서 최대 두 배까지 몰릴 수 있지만 API 호출 상한을 지키는 데는 충분하고 상태가 두 값뿐이다
    /// <summary> 연결 하나의 요청 수를 1초 창으로 센다, TownMember가 종류별로 하나씩 든다 </summary>
    public struct TownRequestThrottle
    {
        /****************************************
        *                Fields
        ****************************************/

        private double _windowStart;
        private int    _count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이번 창에 limit보다 적게 받았으면 하나를 세고 참 </summary>
        public bool TryAccept(double now, int limit, double windowSeconds)
        {
            if ((now - _windowStart) >= windowSeconds)
            {
                _windowStart = now;
                _count       = 0;
            }

            if (_count >= limit)
            {
                return false;
            }

            ++_count;
            return true;
        }
    }
}
