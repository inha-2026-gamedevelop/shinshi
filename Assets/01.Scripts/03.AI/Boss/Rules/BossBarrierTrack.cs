namespace Shinshi.AI
{
    // 서버 시각과 서버가 알린 정리 여부만으로 표시 단계를 정하는 순수 모델이다. 활성 시각 전에는 어떤 경우에도 Active가 되지 않는다
    /// <summary> 결계 위험 구역 한 개의 표시 상태 </summary>
    public sealed class BossBarrierTrack
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossBarrierStart _start;

        private bool _isCleared;

        /****************************************
        *              Properties
        ****************************************/

        public BossBarrierStart Start => _start;

        public bool IsCleared => _isCleared;

        /****************************************
        *                Methods
        ****************************************/

        public BossBarrierTrack(in BossBarrierStart start)
        {
            _start = start;
        }

        public EBossBarrierPhase GetPhase(double serverNow)
        {
            if (_isCleared || (serverNow < _start.TelegraphServerTime))
            {
                return EBossBarrierPhase.None;
            }

            return (serverNow < _start.ActivateServerTime) ? EBossBarrierPhase.Telegraph : EBossBarrierPhase.Active;
        }

        /// <summary> 활성 뒤 흐른 시간(초), 활성 전이면 0 </summary>
        public float GetActiveAge(double serverNow)
        {
            return (serverNow > _start.ActivateServerTime) ? (float)(serverNow - _start.ActivateServerTime) : 0f;
        }

        /// <summary> 예고 진행 비율 0~1 </summary>
        public float GetTelegraphRatio(double serverNow)
        {
            double length = _start.ActivateServerTime - _start.TelegraphServerTime;
            if (length <= 0d)
            {
                return 1f;
            }

            double ratio = (serverNow - _start.TelegraphServerTime) / length;
            if (ratio < 0d)
            {
                return 0f;
            }

            return (float)((ratio > 1d) ? 1d : ratio);
        }

        /// <summary> 서버가 알린 정리, 한 번 정리되면 되돌리지 않는다 </summary>
        public bool TryClear()
        {
            if (_isCleared)
            {
                return false;
            }

            _isCleared = true;
            return true;
        }
    }
}
