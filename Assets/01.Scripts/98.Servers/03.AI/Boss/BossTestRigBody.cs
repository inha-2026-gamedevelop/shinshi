// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 시험 구동기가 쓰는 보스 몸체 대역이다. CombatServerRuntime의 BossServer는 돌진, 투사체, 장판 창구를 패턴 팩토리에 넘기지 않아
    // P01, P04, P05를 만들 수 없으므로 그 연결을 한 몸체를 따로 둔다. 이동 창구는 원형 전장 경계로 몸체를 막고, 장애물 레이어가 있으면 벽도 본다
    /// <summary> 시험 구동기용 보스 몸체, 체력과 생사와 이동 제한을 맡고 두뇌에 체력을 넘긴다 </summary>
    public sealed class BossTestRigBody : IDamageable, IBossVitals, IBossControlSource, IBossMovement, IBossWarp, IBossArenaSource
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float DIRECTION_EPSILON = 0.0001f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly int               _combatId;
        private readonly float             _maxHealth;
        private readonly float             _defense;
        private readonly float             _bodyRadius;
        private readonly Vector3           _arenaCenter;
        private readonly float             _arenaRadius;
        private readonly CombatLineOfSight _lineOfSight;

        private Vector3               _position;
        private Vector3               _forward;
        private float                 _health;
        private EParticipantLifeState _lifeState = EParticipantLifeState.Alive;

        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId        => _combatId;
        public ECombatFaction        Faction         => ECombatFaction.Enemy;
        public EParticipantLifeState LifeState       => _lifeState;
        public bool                  IsSummon        => false;
        public int                   OwnerCombatId   => CombatIds.NONE;
        public Vector3               Position        => _position;
        public Vector3               Forward         => _forward;
        public float                 BodyRadius      => _bodyRadius;
        public float                 Health          => _health;
        public float                 MaxHealth       => _maxHealth;
        public float                 Defense         => _defense;
        public bool                  IsGuardingFront => false;

        /// <summary> 두뇌가 연결된 뒤 채운다, 전투 중이 아니면 피해를 받지 않는다 </summary>
        public BossBrain Brain { get; set; }

        public bool IsInvulnerable => (_lifeState != EParticipantLifeState.Alive) ||
            ((Brain != null) && (Brain.FlowState != EBossFlowState.Combat));

        public float HealthRate   => (_maxHealth > 0f) ? Mathf.Clamp01(_health / _maxHealth) : 0f;
        public bool  IsHealthZero => _health <= Constants.Combat.HEALTH_EPSILON;

        /****************************************
        *                Methods
        ****************************************/

        public BossTestRigBody(int combatId, Vector3 position, Vector3 forward, float maxHealth, float defense,
            float bodyRadius, Vector3 arenaCenter, float arenaRadius, CombatLineOfSight lineOfSight)
        {
            _combatId    = combatId;
            _position    = position;
            _forward     = Flatten(forward, Vector3.forward);
            _maxHealth   = maxHealth;
            _health      = maxHealth;
            _defense     = defense;
            _bodyRadius  = bodyRadius;
            _arenaCenter = arenaCenter;
            _arenaRadius = arenaRadius;
            _lineOfSight = lineOfSight;
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            if (_lifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            if (isLethal)
            {
                _health    = 0f;
                _lifeState = EParticipantLifeState.Dead;
                return;
            }

            _health = Mathf.Max(0f, _health - appliedAmount);
        }

        /// <summary> 예고 방향으로 몸을 돌린다, 다음 선택의 방향 구간이 조준과 맞게 하려는 것이다 </summary>
        public void Face(Vector3 direction)
        {
            _forward = Flatten(direction, _forward);
        }

        public bool TryConsumeDirectControl(out BossDirectControl control)
        {
            control = default;
            return false;
        }

        // 시험 구동기가 전장 정보를 패턴에 주는 입력이다, 월백 2페이즈 P07이 후보의 전장 경계 검사에 쓴다
        public bool TryGetArena(out Vector3 center, out float radius)
        {
            center = _arenaCenter;
            radius = _arenaRadius;
            return _arenaRadius > 0f;
        }

        // 서버 몸체가 하게 될 순간 이동 검사의 로컬 대역이다. 전장 경계는 몸체 반경만큼 안쪽이고, 장애물 레이어가 있으면 네 방향으로 몸체 반경만큼 벽을 본다
        public bool CanStandAt(Vector3 point)
        {
            Vector3 offset = point - _arenaCenter;
            offset.y = 0f;
            if (offset.magnitude > Mathf.Max(0f, _arenaRadius - _bodyRadius))
            {
                return false;
            }

            if ((_lineOfSight == null) || (!_lineOfSight.IsEnabled))
            {
                return true;
            }

            return (!_lineOfSight.IsBlocked(point, point + (Vector3.forward * _bodyRadius)))
                && (!_lineOfSight.IsBlocked(point, point + (Vector3.back * _bodyRadius)))
                && (!_lineOfSight.IsBlocked(point, point + (Vector3.right * _bodyRadius)))
                && (!_lineOfSight.IsBlocked(point, point + (Vector3.left * _bodyRadius)));
        }

        // 후보에서 대상까지 정적 지형이 직선을 막지 않는가, 길찾기가 아니라 직선 시야 검사로 대신한다
        public bool HasPath(Vector3 from, Vector3 to)
        {
            return (_lineOfSight == null) || (!_lineOfSight.IsEnabled) || (!_lineOfSight.IsBlocked(from, to));
        }

        // 서버 몸체라면 여기서 권한을 다시 확인한다, 대역도 같은 검사를 거쳐 위치를 바로 바꾼다
        public bool TryWarp(Vector3 point, out Vector3 landed)
        {
            if (!CanStandAt(point))
            {
                landed = _position;
                return false;
            }

            _position = point;
            landed    = point;
            return true;
        }

        // 전장 경계 안쪽으로 몸체 중심이 갈 수 있는 거리다. 경계는 몸체 반경만큼 안쪽에 둔다
        public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
        {
            float clear = Mathf.Min(maxDistance, DistanceToBoundary(origin, direction, bodyRadius));

            if ((_lineOfSight != null) && (_lineOfSight.IsEnabled))
            {
                float wall = _lineOfSight.GetClearDistance(origin, direction, maxDistance + bodyRadius) - bodyRadius;
                clear = Mathf.Min(clear, wall);
            }

            return Mathf.Clamp(clear, 0f, maxDistance);
        }

        public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
        {
            Vector3 delta    = to - from;
            float   distance = delta.magnitude;
            if (distance <= 0f)
            {
                return from;
            }

            Vector3 direction = delta / distance;
            _position = from + (direction * GetClearDistance(from, direction, distance, bodyRadius));
            return _position;
        }

        // 원 안의 점에서 수평 방향으로 원 둘레까지의 거리, 시작점이 이미 밖이면 0이다
        private float DistanceToBoundary(Vector3 origin, Vector3 direction, float bodyRadius)
        {
            float   limit  = Mathf.Max(0f, _arenaRadius - bodyRadius);
            Vector3 offset = new Vector3(origin.x - _arenaCenter.x, 0f, origin.z - _arenaCenter.z);
            Vector3 flat   = new Vector3(direction.x, 0f, direction.z);

            float a = flat.sqrMagnitude;
            if (a <= DIRECTION_EPSILON)
            {
                return 0f;
            }

            float b = Vector3.Dot(offset, flat);
            float c = offset.sqrMagnitude - (limit * limit);
            if (c >= 0f)
            {
                return 0f;
            }

            float discriminant = (b * b) - (a * c);
            return (-b + Mathf.Sqrt(discriminant)) / a;
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            float sqrMagnitude = direction.sqrMagnitude;
            if (sqrMagnitude <= DIRECTION_EPSILON)
            {
                return fallback;
            }

            return direction / Mathf.Sqrt(sqrMagnitude);
        }
    }
}
