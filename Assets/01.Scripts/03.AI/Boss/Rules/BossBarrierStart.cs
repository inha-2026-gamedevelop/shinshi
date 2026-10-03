// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 값만 담는다. 클라이언트는 이 시각들로 예고와 활성 단계를 재현하고 피해와 대상은 계산하지 않는다
    // 늦게 접속한 클라이언트도 이 값 하나로 지금 단계를 복원할 수 있다
    /// <summary> 결계 위험 구역 한 개의 표시 입력, 바깥 반경과 안쪽 반경 사이가 위험 구역이다 </summary>
    public readonly struct BossBarrierStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _barrierId;
        private readonly Vector3 _center;
        private readonly float   _outerRadius;
        private readonly float   _innerRadius;
        private readonly float   _tickInterval;
        private readonly double  _telegraphServerTime;
        private readonly double  _activateServerTime;
        private readonly double  _completeServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public int BossId    => _bossId;

        /// <summary> 서버가 발급한 번호, 리셋 뒤 다시 시작하면 다른 번호다 </summary>
        public int BarrierId => _barrierId;

        public Vector3 Center      => _center;
        public float   OuterRadius => _outerRadius;

        /// <summary> 안전 구역의 반경 </summary>
        public float   InnerRadius => _innerRadius;

        public float   TickInterval => _tickInterval;

        public double TelegraphServerTime => _telegraphServerTime;

        /// <summary> 이 시각부터 피해가 난다 </summary>
        public double ActivateServerTime => _activateServerTime;

        /// <summary> 전개 시퀀스가 끝나는 시각, 위험 구역은 이 뒤에도 유지된다 </summary>
        public double CompleteServerTime => _completeServerTime;

        /****************************************
        *                Methods
        ****************************************/

        public BossBarrierStart(int bossId, int barrierId, Vector3 center, float outerRadius, float innerRadius,
            float tickInterval, double telegraphServerTime, double activateServerTime, double completeServerTime)
        {
            _bossId              = bossId;
            _barrierId           = barrierId;
            _center              = center;
            _outerRadius         = outerRadius;
            _innerRadius         = innerRadius;
            _tickInterval        = tickInterval;
            _telegraphServerTime = telegraphServerTime;
            _activateServerTime  = activateServerTime;
            _completeServerTime  = completeServerTime;
        }
    }
}
