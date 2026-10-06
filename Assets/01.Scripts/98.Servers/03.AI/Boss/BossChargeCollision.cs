// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 돌진이 막힌 결과, 종류와 구조물 식별자와 멈춘 위치와 간 거리 </summary>
    public readonly struct BossChargeCollision
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EBossChargeObstacleKind _kind;
        private readonly int                     _structureId;
        private readonly Vector3                 _position;
        private readonly float                   _travelDistance;

        /****************************************
        *              Properties
        ****************************************/

        public EBossChargeObstacleKind Kind => _kind;

        /// <summary> 구조물에 막혔으면 그 식별자, 아니면 0이다 </summary>
        public int StructureId => _structureId;

        /// <summary> 보스가 멈춘 위치 </summary>
        public Vector3 Position => _position;

        /// <summary> m, 돌진 시작점에서 멈출 때까지 실제로 간 거리 </summary>
        public float TravelDistance => _travelDistance;

        /****************************************
        *                Methods
        ****************************************/

        public BossChargeCollision(EBossChargeObstacleKind kind, int structureId, Vector3 position, float travelDistance)
        {
            _kind           = kind;
            _structureId    = structureId;
            _position       = position;
            _travelDistance = travelDistance;
        }
    }
}
