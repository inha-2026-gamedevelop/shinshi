// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 일반 적 4역할의 체력, 공격, 제어, 예고 </summary>
    [CreateAssetMenu(fileName = "EnemyCatalog", menuName = "Shinshi/EnemyCatalogSO")]
    public class EnemyCatalogSO : GameDataSO
    {
        /// <summary> 공격 판정과 예고가 함께 쓰는 형태, 보스 패턴도 같은 명세를 쓴다 </summary>
        [Serializable]
        public class AttackShapeSpec
        {
            /****************************************
            *              Constants
            ****************************************/

            private const float MAX_ARC_ANGLE = 360f;

            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private EEnemyAttackShape _shape;
            [SerializeField] private float             _range;           // m, 부채꼴과 원과 장판은 반경, 돌진은 이동 거리, 투사체는 사거리, 직선은 길이
            [SerializeField] private float             _arcAngle;        // 도, 부채꼴만 쓴다
            [SerializeField] private float             _width;           // m, 돌진, 투사체, 직선만 쓴다
            [SerializeField] private float             _projectileSpeed; // m/s, 투사체만 쓴다

            public EEnemyAttackShape Shape           => _shape;
            public float             Range           => _range;
            public float             ArcAngle        => _arcAngle;
            public float             Width           => _width;
            public float             ProjectileSpeed => _projectileSpeed;

            /****************************************
            *                Methods
            ****************************************/

            public AttackShapeSpec(EEnemyAttackShape shape, float range, float arcAngle, float width, float projectileSpeed)
            {
                _shape           = shape;
                _range           = range;
                _arcAngle        = arcAngle;
                _width           = width;
                _projectileSpeed = projectileSpeed;
            }

            public static AttackShapeSpec Fan(float radius, float arcAngle)                 { return new AttackShapeSpec(EEnemyAttackShape.Fan, radius, arcAngle, 0f, 0f); }
            public static AttackShapeSpec Circle(float radius)                              { return new AttackShapeSpec(EEnemyAttackShape.Circle, radius, 0f, 0f, 0f); }
            public static AttackShapeSpec Dash(float distance, float width)                 { return new AttackShapeSpec(EEnemyAttackShape.Dash, distance, 0f, width, 0f); }
            public static AttackShapeSpec Projectile(float range, float width, float speed) { return new AttackShapeSpec(EEnemyAttackShape.Projectile, range, 0f, width, speed); }
            public static AttackShapeSpec TargetArea(float radius)                          { return new AttackShapeSpec(EEnemyAttackShape.TargetArea, radius, 0f, 0f, 0f); }
            public static AttackShapeSpec Line(float length, float width)                   { return new AttackShapeSpec(EEnemyAttackShape.Line, length, 0f, width, 0f); }

            /// <summary> 형태에 필요한 값만 검사한다 </summary>
            public void Validate(List<string> errors, string owner, string field)
            {
                DataValidation.RequirePositive(errors, owner, field + ".Range", _range);

                switch (_shape)
                {
                    case EEnemyAttackShape.Fan:
                        DataValidation.RequirePositiveAtMost(errors, owner, field + ".ArcAngle", _arcAngle, MAX_ARC_ANGLE);
                        break;
                    case EEnemyAttackShape.Dash:
                    case EEnemyAttackShape.Line:
                        DataValidation.RequirePositive(errors, owner, field + ".Width", _width);
                        break;
                    case EEnemyAttackShape.Projectile:
                        DataValidation.RequirePositive(errors, owner, field + ".Width", _width);
                        DataValidation.RequirePositive(errors, owner, field + ".ProjectileSpeed", _projectileSpeed);
                        break;
                    case EEnemyAttackShape.Circle:
                    case EEnemyAttackShape.TargetArea:
                        break;
                    default:
                        errors.Add($"{owner}.{field}.Shape = {_shape}: 정의되지 않은 형태다");
                        break;
                }
            }
        }

        /// <summary> 일반 적 한 역할의 능력치와 대표 공격 </summary>
        [Serializable]
        public class EnemyDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string          _definitionId;
            [SerializeField] private string          _displayKey;
            [SerializeField] private EEnemyRole      _role;
            [SerializeField] private float           _healthRef;            // 지역 고정 권장 공격력 Aref의 배수
            [SerializeField] private float           _defense;              // 피해 감소 공식에 넣는 방어
            [SerializeField] private float           _moveSpeed;            // m/s
            [SerializeField] private float           _attackDamageRef;      // 지역 고정 권장 최대 체력 Href의 배수, 방어 전
            [SerializeField] private float           _attackInterval;       // 초, 대표 공격 주기
            [SerializeField] private float           _telegraphSeconds;     // 초, 공격 예고
            [SerializeField] private float           _attackActiveSeconds;  // 초, 이동하며 판정하는 시간, 즉발이면 0
            [SerializeField] private AttackShapeSpec _attackShape;
            [SerializeField] private bool            _guardable;            // 전방 반격 방어로 막을 수 있는가
            [SerializeField] private bool            _controllable;         // 속박 같은 제어를 받는가
            [SerializeField] private float           _controlDurationScale; // 받는 제어 지속 시간 배율, 1이면 표시값 그대로

            public string          DefinitionId         => _definitionId;
            public string          DisplayKey           => _displayKey;
            public EEnemyRole      Role                 => _role;
            public float           HealthRef            => _healthRef;
            public float           Defense              => _defense;
            public float           MoveSpeed            => _moveSpeed;
            public float           AttackDamageRef      => _attackDamageRef;
            public float           AttackInterval       => _attackInterval;
            public float           TelegraphSeconds     => _telegraphSeconds;
            public float           AttackActiveSeconds  => _attackActiveSeconds;
            public AttackShapeSpec AttackShape          => _attackShape;
            public bool            Guardable            => _guardable;
            public bool            Controllable         => _controllable;
            public float           ControlDurationScale => _controlDurationScale;

            /****************************************
            *                Methods
            ****************************************/

            public EnemyDefinition(string definitionId, EEnemyRole role, float healthRef, float defense, float moveSpeed,
                float attackDamageRef, float attackInterval, float telegraphSeconds, float attackActiveSeconds,
                AttackShapeSpec attackShape, float controlDurationScale)
            {
                _definitionId         = definitionId;
                _displayKey           = definitionId + DISPLAY_KEY_SUFFIX;
                _role                 = role;
                _healthRef            = healthRef;
                _defense              = defense;
                _moveSpeed            = moveSpeed;
                _attackDamageRef      = attackDamageRef;
                _attackInterval       = attackInterval;
                _telegraphSeconds     = telegraphSeconds;
                _attackActiveSeconds  = attackActiveSeconds;
                _attackShape          = attackShape;
                _guardable            = true;
                _controllable         = true;
                _controlDurationScale = controlDurationScale;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string DISPLAY_KEY_SUFFIX = ".name";
        private const int    ENEMY_ROLE_COUNT   = 4;
        private const float  MAX_RATE           = 1f;

        /****************************************
        *                Fields
        ****************************************/

        // 일반 적 공격은 모두 반격으로 막을 수 있고 제어를 받는다. 정예만 속박 지속 시간이 절반이다
        // TODO(기웅): 돌진 산귀의 이동 시간, 원거리 산귀 투사체 폭은 기획 미정이라 자리값이다
        // TODO(기웅): 정예 내려치기 원의 중심(자기 위치 또는 전방 지점)은 기획 미정이다
        // TODO(기웅): 최초 타깃 기준과 재선택 조건이 정해지면 필드를 추가한다
        [Header("일반 적 (EEnemyRole 순서)")]
        [SerializeField] private EnemyDefinition[] _enemies =
        {
            new EnemyDefinition("enemy_melee", EEnemyRole.Melee, 2.4f, 100f, 3.8f,
                0.08f, 1.8f, 0.45f, 0f, AttackShapeSpec.Fan(1.5f, 90f), 1f),
            new EnemyDefinition("enemy_ranged", EEnemyRole.Ranged, 1.8f, 100f, 3.0f,
                0.07f, 2.5f, 0.75f, 0f, AttackShapeSpec.Projectile(14f, 0.5f, 8f), 1f),
            new EnemyDefinition("enemy_charger", EEnemyRole.Charger, 3.2f, 100f, 4.0f,
                0.14f, 4f, 0.9f, 0.45f, AttackShapeSpec.Dash(6f, 1.5f), 1f),
            new EnemyDefinition("enemy_elite", EEnemyRole.Elite, 8.0f, 150f, 3.2f,
                0.20f, 5f, 1.2f, 0f, AttackShapeSpec.Circle(3f), 0.5f)
        };

        // 한 플레이어에게 동시에 걸리는 공격 예고 수, 넘치는 적은 접근, 재배치하며 첫 공격을 늦춘다
        [Header("예고 밀도")]
        [SerializeField] private int _maxCloseTelegraphsPerTarget      = 3; // 근접, 돌진, 정예 합산
        [SerializeField] private int _maxProjectileTelegraphsPerTarget = 2; // 원거리 투사체

        [NonSerialized] private readonly DefinitionLookup<EnemyDefinition> _lookup = new DefinitionLookup<EnemyDefinition>();

        public IReadOnlyList<EnemyDefinition> Enemies                          => _enemies;
        public int                            MaxCloseTelegraphsPerTarget      => _maxCloseTelegraphsPerTarget;
        public int                            MaxProjectileTelegraphsPerTarget => _maxProjectileTelegraphsPerTarget;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _lookup.Invalidate();
        }

        protected override void OnValidate()
        {
            _lookup.Invalidate();
            base.OnValidate();
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool ContainsEnemy(string enemyId)
        {
            return _lookup.Contains(_enemies, enemyId);
        }

        public bool TryGetEnemy(string enemyId, out EnemyDefinition enemy)
        {
            return _lookup.TryGet(_enemies, enemyId, out enemy);
        }

        /// <summary> 역할에 해당하는 정의를 찾는다, 없으면 null </summary>
        public EnemyDefinition FindByRole(EEnemyRole role)
        {
            if (_enemies == null)
            {
                return null;
            }

            for (int i = 0; i < _enemies.Length; ++i)
            {
                if ((_enemies[i] != null) && (_enemies[i].Role == role))
                {
                    return _enemies[i];
                }
            }

            return null;
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(EnemyCatalogSO);

            DataValidation.RequirePositive(errors, OWNER, nameof(_maxCloseTelegraphsPerTarget), _maxCloseTelegraphsPerTarget);
            DataValidation.RequirePositive(errors, OWNER, nameof(_maxProjectileTelegraphsPerTarget), _maxProjectileTelegraphsPerTarget);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_enemies), _enemies);
            DataValidation.RequireCount(errors, OWNER, nameof(_enemies), _enemies, ENEMY_ROLE_COUNT);
            if (_enemies == null)
            {
                return;
            }

            var seenRoles = new HashSet<EEnemyRole>();
            for (int i = 0; i < _enemies.Length; ++i)
            {
                EnemyDefinition enemy = _enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                string field = $"{nameof(_enemies)}[{enemy.DefinitionId}]";

                if (!Enum.IsDefined(typeof(EEnemyRole), enemy.Role))
                {
                    errors.Add($"{OWNER}.{field}.Role = {enemy.Role}: 정의되지 않은 역할이다");
                }
                else if (!seenRoles.Add(enemy.Role))
                {
                    errors.Add($"{OWNER}.{field}.Role = {enemy.Role}: 역할이 중복된다");
                }

                DataValidation.RequirePositive(errors, OWNER, field + ".HealthRef", enemy.HealthRef);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".Defense", enemy.Defense);
                DataValidation.RequirePositive(errors, OWNER, field + ".MoveSpeed", enemy.MoveSpeed);
                DataValidation.RequirePositive(errors, OWNER, field + ".AttackDamageRef", enemy.AttackDamageRef);
                DataValidation.RequirePositive(errors, OWNER, field + ".AttackInterval", enemy.AttackInterval);
                DataValidation.RequirePositive(errors, OWNER, field + ".TelegraphSeconds", enemy.TelegraphSeconds);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".AttackActiveSeconds", enemy.AttackActiveSeconds);
                DataValidation.RequirePositiveAtMost(errors, OWNER, field + ".ControlDurationScale", enemy.ControlDurationScale, MAX_RATE);

                if (enemy.AttackShape == null)
                {
                    errors.Add($"{OWNER}.{field}.AttackShape: 공격 형태가 비어 있다");
                    continue;
                }

                enemy.AttackShape.Validate(errors, OWNER, field + ".AttackShape");
            }
        }
    }
}
