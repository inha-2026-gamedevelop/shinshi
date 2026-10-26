// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 피격 한 건이다. 임팩트를 어디에 어느 쪽으로 터뜨릴지만 담고, 맞았는지는 클라이언트가 다시 판단하지 않는다
    // 서버 패턴의 OnHit 알림은 아직 맞은 사람 수만 넘기므로, 복제 계층이 이 값을 채워 넘기는 계약은 후속 요청에서 정한다
    /// <summary> 월백 공격에 플레이어가 맞은 순간의 표시용 입력 </summary>
    public readonly struct WolbaekPlayerHit
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _targetId;
        private readonly string  _patternId;
        private readonly Vector3 _position;
        private readonly Vector3 _direction;
        private readonly double  _serverTime;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 맞은 대상의 CombatId </summary>
        public int TargetId => _targetId;

        /// <summary> 맞은 공격의 패턴 정의 ID, 로그와 진단용이다 </summary>
        public string PatternId => _patternId;

        /// <summary> 임팩트 중심, 맞은 대상의 몸 위치 </summary>
        public Vector3 Position => _position;

        /// <summary> 공격이 대상에게 향한 방향, 수평 길이가 0이면 방향 없는 공격이다 </summary>
        public Vector3 Direction => _direction;

        public double ServerTime => _serverTime;

        /****************************************
        *               Constructor
        ****************************************/

        public WolbaekPlayerHit(int targetId, string patternId, Vector3 position, Vector3 direction, double serverTime)
        {
            _targetId   = targetId;
            _patternId  = patternId;
            _position   = position;
            _direction  = direction;
            _serverTime = serverTime;
        }
    }
}
