// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 확정한 발사 값만 담는다. 클라이언트는 충돌과 벽을 다시 계산하지 않고 속도와 경로 길이로 비행을 표시만 한다
    /// <summary> 투사체 한 발의 발사 입력, 투사체 번호가 한 발을 가리키는 키다 </summary>
    public readonly struct BossProjectileStart
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _bossId;
        private readonly int     _projectileId;
        private readonly int     _castId;
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

        /// <summary> 투사체 한 발의 번호, 서버의 동작 번호다. 한 시전이 여러 발을 쏘므로 시전 번호가 아니라 이 번호가 한 발을 가리키는 키다 </summary>
        public int ProjectileId => _projectileId;

        /// <summary> 이 발이 속한 시전 번호, 같은 번호의 예고 표현을 발사와 함께 마무리하는 데 쓴다. 단발 시전은 투사체 번호와 같다 </summary>
        public int CastId => _castId;

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

        // 시전 번호를 따로 주지 않으면 투사체 번호가 시전 번호다(단발)
        public BossProjectileStart(int bossId, int projectileId, Vector3 origin, Vector3 direction, float speed, float width,
            float pathLength, double launchServerTime)
            : this(bossId, projectileId, projectileId, origin, direction, speed, width, pathLength, launchServerTime)
        {
        }

        public BossProjectileStart(int bossId, int projectileId, int castId, Vector3 origin, Vector3 direction, float speed,
            float width, float pathLength, double launchServerTime)
        {
            _bossId           = bossId;
            _projectileId     = projectileId;
            _castId           = castId;
            _origin           = origin;
            _direction        = direction;
            _speed            = speed;
            _width            = width;
            _pathLength       = pathLength;
            _launchServerTime = launchServerTime;
        }
    }
}
