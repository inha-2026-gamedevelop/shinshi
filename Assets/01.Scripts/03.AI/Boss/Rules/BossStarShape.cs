// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 별형 회전 빔의 기하 값이다. 서버가 예고 시작 순간 고정하고, 서버 판정과 표현이 같은 값을 읽는다
    // 꼭짓점 0은 기준 방향에 놓이고 나머지는 같은 각도 간격으로 시계 방향(위에서 볼 때)으로 놓인다
    // 회전 방향은 부호로 담는다. 양수는 오른쪽(시계 방향), 음수는 왼쪽이다
    /// <summary> 별형 빔 구조 하나의 배치와 회전 값 </summary>
    public readonly struct BossStarShape
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _arenaCenter;
        private readonly float   _arenaRadius;
        private readonly Vector3 _baseDirection;
        private readonly float   _ringRadius;
        private readonly int     _vertexCount;
        private readonly int     _connectStep;
        private readonly float   _beamWidth;
        private readonly float   _signedDegreesPerSecond;

        /****************************************
        *              Properties
        ****************************************/

        public Vector3 ArenaCenter => _arenaCenter;
        public float   ArenaRadius => _arenaRadius;

        /// <summary> 꼭짓점 0이 놓이는 수평 단위 방향, 활성 전에는 이 배치 그대로이다 </summary>
        public Vector3 BaseDirection => _baseDirection;

        /// <summary> m, 전장 중심에서 꼭짓점까지 거리 </summary>
        public float RingRadius => _ringRadius;

        public int VertexCount => _vertexCount;

        /// <summary> 꼭짓점 k를 몇 칸 뒤 꼭짓점과 잇는가, 빔 k는 꼭짓점 k와 (k + 이 값) 꼭짓점 사이다 </summary>
        public int ConnectStep => _connectStep;

        /// <summary> m, 빔 폭 </summary>
        public float BeamWidth => _beamWidth;

        /// <summary> 도/초, 부호가 방향이다 </summary>
        public float SignedDegreesPerSecond => _signedDegreesPerSecond;

        public bool RotatesRight => _signedDegreesPerSecond >= 0f;

        /// <summary> 빔 수, 꼭짓점마다 하나씩이다 </summary>
        public int BeamCount => _vertexCount;

        /****************************************
        *                Methods
        ****************************************/

        public BossStarShape(Vector3 arenaCenter, float arenaRadius, Vector3 baseDirection, float ringRadius, int vertexCount,
            int connectStep, float beamWidth, float signedDegreesPerSecond)
        {
            _arenaCenter            = arenaCenter;
            _arenaRadius            = arenaRadius;
            _baseDirection          = baseDirection;
            _ringRadius             = ringRadius;
            _vertexCount            = vertexCount;
            _connectStep            = connectStep;
            _beamWidth              = beamWidth;
            _signedDegreesPerSecond = signedDegreesPerSecond;
        }
    }
}
