// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 발사 위치와 방향과 속도는 서버가 확정한 값이고 클라이언트는 이 값으로 표시만 한다
    /// <summary> 투사체 한 발의 발사 입력, 패턴이 만들어 지속물 관리자에 넘긴다 </summary>
    public readonly struct BossProjectileSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int             _castInstanceId;
        private readonly int             _actionInstanceId;
        private readonly int             _attackerId;
        private readonly string          _definitionId;
        private readonly Vector3         _origin;
        private readonly Vector3         _direction;
        private readonly float           _speed;
        private readonly float           _range;
        private readonly float           _width;
        private readonly float           _rawDamage;
        private readonly EDamageFlags    _flags;
        private readonly double          _launchServerTime;
        private readonly BossDamageGate  _gate;
        private readonly BossProjectileHitLimit _hitLimit;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 예고부터 회복까지 한 번의 시전 번호, 투사체 한 발을 가리키는 키로도 쓴다 </summary>
        public int CastInstanceId => _castInstanceId;

        /// <summary> 투사체 한 발의 동작 번호, 피해 요청마다 같은 값을 쓴다 </summary>
        public int ActionInstanceId => _actionInstanceId;

        public int            AttackerId   => _attackerId;
        public string         DefinitionId => _definitionId;

        /// <summary> 발사 위치, 보스의 서버 위치다 </summary>
        public Vector3        Origin       => _origin;

        /// <summary> 수평 단위 방향, 발사 뒤에도 바뀌지 않는다 </summary>
        public Vector3        Direction    => _direction;

        /// <summary> m/초 </summary>
        public float          Speed        => _speed;

        /// <summary> 최대 이동 거리(m), 벽이 있으면 그 앞에서 먼저 사라진다 </summary>
        public float          Range        => _range;

        /// <summary> 충돌 폭(m) </summary>
        public float          Width        => _width;

        /// <summary> 방어 전 피해량 </summary>
        public float          RawDamage    => _rawDamage;

        public EDamageFlags   Flags        => _flags;

        /// <summary> 발사된 서버 시각, 틱 시각이 아니라 FSM의 단계 경계라 틱이 거칠어도 정확하다 </summary>
        public double         LaunchServerTime => _launchServerTime;

        /// <summary> 피해는 이 게이트로만 낸다, 사망과 전투 종료로 닫히면 나가지 않는다 </summary>
        public BossDamageGate Gate         => _gate;

        /// <summary> 같은 시전의 발들이 함께 쓰는 대상별 피격 기록, 비어 있으면 대상 상한 없이 첫 명중에 사라지는 기존 규칙이다. 있으면 상한에 닿은 대상은 지나치고 예비 슬롯을 쓸 수 있다 </summary>
        public BossProjectileHitLimit HitLimit => _hitLimit;

        /****************************************
        *                Methods
        ****************************************/

        public BossProjectileSpec(int castInstanceId, int actionInstanceId, int attackerId, string definitionId, Vector3 origin,
            Vector3 direction, float speed, float range, float width, float rawDamage, EDamageFlags flags, double launchServerTime,
            BossDamageGate gate, BossProjectileHitLimit hitLimit = null)
        {
            _hitLimit         = hitLimit;
            _castInstanceId   = castInstanceId;
            _actionInstanceId = actionInstanceId;
            _attackerId       = attackerId;
            _definitionId     = definitionId;
            _origin           = origin;
            _direction        = direction;
            _speed            = speed;
            _range            = range;
            _width            = width;
            _rawDamage        = rawDamage;
            _flags            = flags;
            _launchServerTime = launchServerTime;
            _gate             = gate;
        }
    }
}
