// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 전용 결과(EBossChargeEnd, BossChargeCollision)를 표현용 값으로 풀었다. 벽과 구조물은 StructureId로 나뉜다
    /// <summary> 뿔 들이받기 돌진이 어떻게 끝났는지와 회복 시간 </summary>
    public readonly struct BossChargeOutcome
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _castId;
        private readonly bool    _isCollision;
        private readonly Vector3 _position;
        private readonly float   _travelDistance;
        private readonly int     _structureId;
        private readonly float   _recoverySeconds;
        private readonly double  _serverTime;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId), 정의 ID(boss_mucheol 같은 문자열)가 아니다 </summary>
        public int BossId => _bossId;

        /// <summary> 서버가 시전마다 발급한 시전 번호 </summary>
        public int CastId => _castId;

        /// <summary> 벽이나 구조물에 막혀 끝났다, 거짓이면 정해진 거리를 다 갔다 </summary>
        public bool IsCollision => _isCollision;

        /// <summary> 보스가 멈춘 위치 </summary>
        public Vector3 Position => _position;

        /// <summary> m, 돌진 시작점에서 멈출 때까지 간 거리 </summary>
        public float TravelDistance => _travelDistance;

        /// <summary> 막은 구조물의 식별자, 벽이거나 충돌이 아니면 0이다 </summary>
        public int StructureId => _structureId;

        /// <summary> 초, 실행 종료 경계부터 재는 회복 시간 </summary>
        public float RecoverySeconds => _recoverySeconds;

        /// <summary> 서버가 결과를 알린 틱 시각 </summary>
        public double ServerTime => _serverTime;

        /****************************************
        *                Methods
        ****************************************/

        public BossChargeOutcome(int bossId, int castId, bool isCollision, Vector3 position, float travelDistance,
            int structureId, float recoverySeconds, double serverTime)
        {
            _bossId          = bossId;
            _castId          = castId;
            _isCollision     = isCollision;
            _position        = position;
            _travelDistance  = travelDistance;
            _structureId     = structureId;
            _recoverySeconds = recoverySeconds;
            _serverTime      = serverTime;
        }
    }
}
