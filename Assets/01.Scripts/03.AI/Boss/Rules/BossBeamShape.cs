// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 고정 평행 빔 한 줄의 기하 값이다. 피해 영역은 폭을 가진 띠와 원형 전장의 교집합이며, 서버 판정과 클라이언트 표현이 같은 값과 같은 경계를 쓴다
    // 띠는 방향 직선을 중심으로 폭의 절반만큼 양쪽이고, 방향 쪽 끝은 전장 원이 정한다(띠 모서리가 전장 밖으로 나가지 않는다)
    // 시작 순간 한 번 정하면 바뀌지 않는다. 회전 빔 등 움직이는 빔은 이 값을 매 틱 새로 만들어 쓰는 후속 확장이며 지금은 만들지 않았다
    /// <summary> 빔 한 줄의 위치와 방향과 폭과 전장 정보 </summary>
    public readonly struct BossBeamShape
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _arenaCenter;
        private readonly float   _arenaRadius;
        private readonly Vector3 _direction;
        private readonly float   _lateralOffset;
        private readonly float   _width;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 전장 중심, 보스의 위치가 아니라 전장 정보에서 온 값이다 </summary>
        public Vector3 ArenaCenter => _arenaCenter;

        /// <summary> 전장 반경(m) </summary>
        public float ArenaRadius => _arenaRadius;

        /// <summary> 빔이 뻗는 방향, 수평 단위 벡터 </summary>
        public Vector3 Direction => _direction;

        /// <summary> 방향의 오른쪽을 양수로 잰, 전장 중심에서 빔 중심선까지의 거리(m) </summary>
        public float LateralOffset => _lateralOffset;

        /// <summary> 띠의 폭(m) </summary>
        public float Width => _width;

        /// <summary> 방향의 오른쪽, 위에서 볼 때 시계 방향으로 90도다 </summary>
        public Vector3 Right => BossBeamGeometry.RightOf(_direction);

        /// <summary> 전장 중심에서 중심선으로 내린 수선의 발, 표현이 띠를 놓는 기준점이다 </summary>
        public Vector3 LineCenter => _arenaCenter + (Right * _lateralOffset);

        /****************************************
        *                Methods
        ****************************************/

        public BossBeamShape(Vector3 arenaCenter, float arenaRadius, Vector3 direction, float lateralOffset, float width)
        {
            _arenaCenter   = arenaCenter;
            _arenaRadius   = arenaRadius;
            _direction     = direction;
            _lateralOffset = lateralOffset;
            _width         = width;
        }
    }
}
