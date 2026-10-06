namespace Shinshi.AI
{
    // 구조물 조회의 결과다. 거리는 이동 창구의 벽 앞 거리(GetClearDistance)와 같은 뜻이라 몸체 반경이 구조물에 닿기 직전까지의 거리다
    /// <summary> 돌진 경로에서 처음 만난 구조물, 구조물 식별자와 그 앞까지 갈 수 있는 거리 </summary>
    public readonly struct BossChargeStructureHit
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int   _structureId;
        private readonly float _distance;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 구조물 식별자, 맵과 서버가 같은 값으로 구조물을 가리킨다. 0은 구조물이 아니다 </summary>
        public int StructureId => _structureId;

        /// <summary> m, 조회 시작점에서 구조물 앞까지 갈 수 있는 거리 </summary>
        public float Distance => _distance;

        /****************************************
        *                Methods
        ****************************************/

        public BossChargeStructureHit(int structureId, float distance)
        {
            _structureId = structureId;
            _distance    = distance;
        }
    }
}
