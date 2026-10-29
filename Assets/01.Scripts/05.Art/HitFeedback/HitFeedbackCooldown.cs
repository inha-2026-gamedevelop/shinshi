namespace Shinshi.Art
{
    /// <summary> 임팩트 프레임처럼 연달아 내면 화면이 깜빡이는 연출의 재사용 간격을 지키는 순수 판정기, 시간은 호출하는 쪽이 넘기는 초 단위 값이다 </summary>
    public sealed class HitFeedbackCooldown
    {
        /****************************************
        *                Fields
        ****************************************/

        private double _nextTime = double.NegativeInfinity;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지금 낼 수 있으면 참이고, 이때부터 cooldownSeconds 동안은 다시 내지 못한다 </summary>
        public bool TryUse(double now, float cooldownSeconds)
        {
            if (now < _nextTime)
            {
                return false;
            }

            _nextTime = now + cooldownSeconds;
            return true;
        }

        public void Reset()
        {
            _nextTime = double.NegativeInfinity;
        }
    }
}
