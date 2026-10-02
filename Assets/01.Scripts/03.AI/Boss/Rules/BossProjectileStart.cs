// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 발사 값만 담는다. 클라이언트는 충돌과 벽을 다시 계산하지 않고 속도와 경로 길이로 비행을 표시만 한다
    /// <summary> 투사체 한 발의 발사 입력, 시전 번호가 투사체를 가리키는 키다 </summary>
    public readonly struct BossProjectileStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _projectileId;
        private readonly Vector3 _origin;
        private readonly Vector3 _direction;
        private readonly float   _speed;
        private readonly float   _width;
        private readonly float   _pathLength;
        private readonly double  _launchServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public int BossId => _bossId;

        /// <summary> 서버의 시전 번호, 같은 번호의 예고 표현과 이어지지만 예고가 끝나도 투사체는 따로 남는다 </summary>
        public int ProjectileId => _projectileId;

        /// <summary> 발사 위치, 서버가 확정한 보스 위치다 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 수평 단위 방향, 발사 뒤에도 바뀌지 않는다 </summary>
        public Vector3 Direction => _direction;

        /// <summary> m/초 </summary>
        public float Speed => _speed;

        /// <summary> 투사체 폭(m) </summary>
        public float Width => _width;

        /// <summary> 날아갈 수 있는 최대 길이, 벽이 있으면 그 앞까지이고 최대 사거리를 넘지 않는다 </summary>
        public float PathLength => _pathLength;

        /// <summary> 발사된 서버 시각 </summary>
        public double LaunchServerTime => _launchServerTime;

        /****************************************
        *                Methods
        ****************************************/

        public BossProjectileStart(int bossId, int projectileId, Vector3 origin, Vector3 direction, float speed, float width,
            float pathLength, double launchServerTime)
        {
            _bossId           = bossId;
            _projectileId     = projectileId;
            _origin           = origin;
            _direction        = direction;
            _speed            = speed;
            _width            = width;
            _pathLength       = pathLength;
            _launchServerTime = launchServerTime;
        }
    }
}
