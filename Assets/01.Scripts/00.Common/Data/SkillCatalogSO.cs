// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 직업 기술 12종과 신령사 강림 3종의 비용, 재사용 시간, 타임라인, 형태 </summary>
    [CreateAssetMenu(fileName = "SkillCatalog", menuName = "Shinshi/SkillCatalogSO")]
    public class SkillCatalogSO : GameDataSO
    {
        /// <summary> 기술 실행 단계별 길이, Windup, Active, Recovery 순서로 진행한다 </summary>
        [Serializable]
        public class SkillTimeline
        {
            /****************************************
            *                Fields
            ****************************************/

            // 반격은 Active가 전방 방어 창이고, 돌진은 Active 동안 이동한다
            [SerializeField] private float _windup;   // 초, 준비. 이 구간에서 회피로 취소해도 비용은 돌려주지 않는다
            [SerializeField] private float _active;   // 초, 0이면 발동 순간 한 번 판정
            [SerializeField] private float _recovery; // 초, 후딜. 타격 뒤 회피로 줄일 수 있다

            public float Windup   => _windup;
            public float Active   => _active;
            public float Recovery => _recovery;

            /****************************************
            *                Methods
            ****************************************/

            public SkillTimeline(float windup, float active, float recovery)
            {
                _windup   = windup;
                _active   = active;
                _recovery = recovery;
            }
        }

        /// <summary> 조준 방식과 판정 형태, 형태에 쓰지 않는 값은 0으로 둔다 </summary>
        [Serializable]
        public class SkillArea
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private ESkillAimMode _aimMode;
            [SerializeField] private ESkillShape   _shape;
            [SerializeField] private float         _castRange;       // m, 지점 기술의 최대 지정 거리, 투사체 사거리, 돌진 거리
            [SerializeField] private float         _radius;          // m, 부채꼴 길이 또는 원 반경
            [SerializeField] private float         _arcAngle;        // 도, 부채꼴 전체 각도
            [SerializeField] private float         _width;           // m, 직선 경로나 투사체 폭
            [SerializeField] private float         _projectileSpeed; // m/s
            [SerializeField] private float         _centerOffset;    // m, 방향 기술의 원 중심을 시전자 앞으로 미는 거리

            public ESkillAimMode AimMode         => _aimMode;
            public ESkillShape   Shape           => _shape;
            public float         CastRange       => _castRange;
            public float         Radius          => _radius;
            public float         ArcAngle        => _arcAngle;
            public float         Width           => _width;
            public float         ProjectileSpeed => _projectileSpeed;
            public float         CenterOffset    => _centerOffset;

            /****************************************
            *                Methods
            ****************************************/

            public SkillArea(ESkillAimMode aimMode, ESkillShape shape, float castRange = 0f, float radius = 0f,
                float arcAngle = 0f, float width = 0f, float projectileSpeed = 0f, float centerOffset = 0f)
            {
                _aimMode         = aimMode;
                _shape           = shape;
                _castRange       = castRange;
                _radius          = radius;
                _arcAngle        = arcAngle;
                _width           = width;
                _projectileSpeed = projectileSpeed;
                _centerOffset    = centerOffset;
            }
        }

        /// <summary> 방어, 치명타, 부적 보정 전의 피해 계수와 지속 효과 </summary>
        [Serializable]
        public class SkillEffectSpec
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private float _damageCoefficient;          // 공격력 배율, 최초 타격
            [SerializeField] private float _tickCoefficient;            // 공격력 배율, 지속 피해 1회분
            [SerializeField] private float _tickInterval;               // 초
            [SerializeField] private float _duration;                   // 초, 장판, 결계, 소환 유지 시간
            [SerializeField] private float _rootDuration;               // 초, 일반 적 기준 속박
            [SerializeField] private float _allyDamageBonus;            // 비율, 결계 안 적에게 시전자 동료가 주는 피해 증가
            [SerializeField] private float _companionAttackCoefficient; // 공격력 배율, 소환 동료 한 번 공격
            [SerializeField] private float _companionAttackInterval;    // 초
            [SerializeField] private float _companionAttackRange;       // m

            // 검사, 마검사, 궁수 기술이 쓰는 값. 기존 직업 기술은 모두 0이다
            [SerializeField] private float _secondHitCoefficient; // 공격력 배율, 최초 타격 뒤 같은 판정 형태의 두 번째 타격. 0이면 없음
            [SerializeField] private float _secondHitDelay;       // 초, 최초 타격에서 두 번째 타격까지
            [SerializeField] private int   _projectileCount;      // 동시에 쏘는 투사체 수. 0이나 1이면 한 발
            [SerializeField] private float _spreadAngle;          // 도, 동시 투사체 사이 각도
            [SerializeField] private int   _maxTargets;           // 투사체 하나가 맞히는 최대 적 수. 0이나 1이면 첫 적에서 소멸
            [SerializeField] private int   _repeatCount;          // 순차 발사 횟수. 0이나 1이면 한 번
            [SerializeField] private float _repeatInterval;       // 초, 순차 발사 사이
            [SerializeField] private float _retreatDistance;      // m, 조준 반대 방향 후퇴 거리
            [SerializeField] private float _retreatDuration;      // 초, 후퇴 이동 시간

            // 마검사 스택 기술이 쓰는 값. 다른 직업 기술은 모두 0이다
            [SerializeField] private int   _stackGain;            // 발동이 끝나면 얻는 스택. 장판은 생성 때, 순간 이동 베기는 도착 때
            [SerializeField] private float _stackCoefficient;     // 공격력 배율, 스택 하나당 첫 타격에 더하는 계수. 0보다 크면 시전 확정 때 스택을 모두 쓴다
            [SerializeField] private float _hitImmunityDuration;  // 초, 발동 순간부터 기절, 속박, 밀림을 받지 않는 시간. 피해는 그대로 받는다
            [SerializeField] private bool  _teleportToEnd;        // 직선 기술이 경로를 지나며 베고 발동 구간이 끝날 때 경로 끝으로 순간 이동한다

            // 궁수 속성 기술이 쓰는 값. 다른 직업 기술은 모두 0이다
            [SerializeField] private EArcherElement _archerElement;  // 자기 강화 기술이 이후 기본 공격에 싣는 속성. None이면 속성을 바꾸지 않는다
            [SerializeField] private int            _requiredStacks; // 시전에 필요한 스택, 0보다 크면 시전 확정 때 이만큼 쓴다. 모자라면 거절한다

            public float          DamageCoefficient          => _damageCoefficient;
            public float          TickCoefficient            => _tickCoefficient;
            public float          TickInterval               => _tickInterval;
            public float          Duration                   => _duration;
            public float          RootDuration               => _rootDuration;
            public float          AllyDamageBonus            => _allyDamageBonus;
            public float          CompanionAttackCoefficient => _companionAttackCoefficient;
            public float          CompanionAttackInterval    => _companionAttackInterval;
            public float          CompanionAttackRange       => _companionAttackRange;
            public float          SecondHitCoefficient       => _secondHitCoefficient;
            public float          SecondHitDelay             => _secondHitDelay;
            public int            ProjectileCount            => _projectileCount;
            public float          SpreadAngle                => _spreadAngle;
            public int            MaxTargets                 => _maxTargets;
            public int            RepeatCount                => _repeatCount;
            public float          RepeatInterval             => _repeatInterval;
            public float          RetreatDistance            => _retreatDistance;
            public float          RetreatDuration            => _retreatDuration;
            public int            StackGain                  => _stackGain;
            public float          StackCoefficient           => _stackCoefficient;
            public float          HitImmunityDuration        => _hitImmunityDuration;
            public bool           TeleportToEnd              => _teleportToEnd;
            public EArcherElement ArcherElement              => _archerElement;
            public int            RequiredStacks             => _requiredStacks;

            /****************************************
            *                Methods
            ****************************************/

            public SkillEffectSpec(float damageCoefficient = 0f, float tickCoefficient = 0f, float tickInterval = 0f,
                float duration = 0f, float rootDuration = 0f, float allyDamageBonus = 0f,
                float companionAttackCoefficient = 0f, float companionAttackInterval = 0f, float companionAttackRange = 0f,
                float secondHitCoefficient = 0f, float secondHitDelay = 0f, int projectileCount = 0, float spreadAngle = 0f,
                int maxTargets = 0, int repeatCount = 0, float repeatInterval = 0f, float retreatDistance = 0f,
                float retreatDuration = 0f, int stackGain = 0, float stackCoefficient = 0f, float hitImmunityDuration = 0f,
                bool teleportToEnd = false, EArcherElement archerElement = EArcherElement.None, int requiredStacks = 0)
            {
                _damageCoefficient          = damageCoefficient;
                _tickCoefficient            = tickCoefficient;
                _tickInterval               = tickInterval;
                _duration                   = duration;
                _rootDuration               = rootDuration;
                _allyDamageBonus            = allyDamageBonus;
                _companionAttackCoefficient = companionAttackCoefficient;
                _companionAttackInterval    = companionAttackInterval;
                _companionAttackRange       = companionAttackRange;
                _secondHitCoefficient       = secondHitCoefficient;
                _secondHitDelay             = secondHitDelay;
                _projectileCount            = projectileCount;
                _spreadAngle                = spreadAngle;
                _maxTargets                 = maxTargets;
                _repeatCount                = repeatCount;
                _repeatInterval             = repeatInterval;
                _retreatDistance            = retreatDistance;
                _retreatDuration            = retreatDuration;
                _stackGain                  = stackGain;
                _stackCoefficient           = stackCoefficient;
                _hitImmunityDuration        = hitImmunityDuration;
                _teleportToEnd              = teleportToEnd;
                _archerElement              = archerElement;
                _requiredStacks             = requiredStacks;
            }
        }

        /// <summary> 직업 고정 일반 기술 하나, 기력과 쿨다운을 함께 쓴다 </summary>
        [Serializable]
        public class SkillDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            private const string DISPLAY_KEY_SUFFIX = ".name";

            [SerializeField] private string          _definitionId;
            [SerializeField] private string          _displayKey;
            [SerializeField] private EPlayerClass    _playerClass;
            [SerializeField] private int             _slot;        // Q, W, E, R 순서의 0부터
            [SerializeField] private bool            _isSignature; // 대표 기술은 부적 단계마다 후보가 2개다
            [SerializeField] private float           _energyCost;  // 기력
            [SerializeField] private float           _cooldown;    // 초
            [SerializeField] private SkillTimeline   _timeline;
            [SerializeField] private SkillArea       _area;
            [SerializeField] private SkillEffectSpec _effect;

            // 선택값, HUD 기술 칸 그림. 비어 있으면 HUD가 가진 기본 그림을 그대로 쓴다
            [SerializeField] private Sprite          _icon;

            public string          DefinitionId => _definitionId;
            public string          DisplayKey   => _displayKey;
            public EPlayerClass    PlayerClass  => _playerClass;
            public int             Slot         => _slot;
            public bool            IsSignature  => _isSignature;
            public float           EnergyCost   => _energyCost;
            public float           Cooldown     => _cooldown;
            public SkillTimeline   Timeline     => _timeline;
            public SkillArea       Area         => _area;
            public SkillEffectSpec Effect       => _effect;
            public Sprite          Icon         => _icon;

            /****************************************
            *                Methods
            ****************************************/

            public SkillDefinition(string definitionId, EPlayerClass playerClass, int slot, bool isSignature,
                float energyCost, float cooldown, SkillTimeline timeline, SkillArea area, SkillEffectSpec effect)
            {
                _definitionId = definitionId;
                _displayKey   = definitionId + DISPLAY_KEY_SUFFIX;
                _playerClass  = playerClass;
                _slot         = slot;
                _isSignature  = isSignature;
                _energyCost   = energyCost;
                _cooldown     = cooldown;
                _timeline     = timeline;
                _area         = area;
                _effect       = effect;
            }
        }

        /// <summary> 신령사 공명 강림 하나, 공명을 쓰고 강림 공용 재사용 시간을 적용한다 </summary>
        [Serializable]
        public class AscensionDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            private const string DISPLAY_KEY_SUFFIX = ".name";

            // 세 강림 모두 지점 지정이다
            [SerializeField] private string _definitionId;
            [SerializeField] private string _displayKey;
            [SerializeField] private int    _slot;              // Z, X, C 순서의 0부터
            [SerializeField] private float  _resonanceCost;     // 공명, 시전 확정 때 소모
            [SerializeField] private float  _castRange;         // m, 최대 지정 거리
            [SerializeField] private float  _radius;            // m
            [SerializeField] private float  _windup;            // 초
            [SerializeField] private float  _recovery;          // 초
            [SerializeField] private float  _damageCoefficient; // 공격력 배율
            [SerializeField] private float  _rootDuration;      // 초, 일반 적 기준 속박. 0이면 없음
            [SerializeField] private bool   _singleTarget;      // 범위 안에서 커서와 가장 가까운 유효 적 하나만 타격

            // 선택값, HUD 강림 칸 그림. 비어 있으면 HUD가 가진 기본 그림을 그대로 쓴다
            [SerializeField] private Sprite _icon;

            public string DefinitionId      => _definitionId;
            public string DisplayKey        => _displayKey;
            public int    Slot              => _slot;
            public float  ResonanceCost     => _resonanceCost;
            public float  CastRange         => _castRange;
            public float  Radius            => _radius;
            public float  Windup            => _windup;
            public float  Recovery          => _recovery;
            public float  DamageCoefficient => _damageCoefficient;
            public float  RootDuration      => _rootDuration;
            public bool   SingleTarget      => _singleTarget;
            public Sprite Icon              => _icon;

            /****************************************
            *                Methods
            ****************************************/

            public AscensionDefinition(string definitionId, int slot, float resonanceCost, float castRange, float radius,
                float windup, float recovery, float damageCoefficient, float rootDuration, bool singleTarget)
            {
                _definitionId      = definitionId;
                _displayKey        = definitionId + DISPLAY_KEY_SUFFIX;
                _slot              = slot;
                _resonanceCost     = resonanceCost;
                _castRange         = castRange;
                _radius            = radius;
                _windup            = windup;
                _recovery          = recovery;
                _damageCoefficient = damageCoefficient;
                _rootDuration      = rootDuration;
                _singleTarget      = singleTarget;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int   SKILL_COUNT                = 12; // 구 직업 3종(무사, 도술사, 신령사)
        private const int   CLASS_SKILL_COUNT          = 12; // 검사, 마검사, 궁수
        private const int   SIGNATURE_SKILLS_PER_CLASS = 1;
        private const float MAX_ARC_ANGLE              = 360f;

        /****************************************
        *                Fields
        ****************************************/

        // 후딜은 무사 0.2초, 도술사와 신령사 0.15초를 공통 시작값으로 쓴다
        [Header("일반 기술 (직업마다 Q, W, E, R 4개)")]
        [SerializeField] private SkillDefinition[] _skills =
        {
            new SkillDefinition("skill_warrior_wide_slash", EPlayerClass.Warrior, slot: 0, isSignature: true, energyCost: 20f, cooldown: 6f,
                new SkillTimeline(0.2f, 0f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Cone, radius: 3f, arcAngle: 120f),
                new SkillEffectSpec(damageCoefficient: 2.2f)),
            // 첫 적 몸체 앞에서 정지
            new SkillDefinition("skill_warrior_dash", EPlayerClass.Warrior, slot: 1, isSignature: false, energyCost: 20f, cooldown: 8f,
                new SkillTimeline(0f, 0.3f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Line, castRange: 4f, width: 1.5f),
                new SkillEffectSpec(damageCoefficient: 1.8f)),
            new SkillDefinition("skill_warrior_ground_slam", EPlayerClass.Warrior, slot: 2, isSignature: false, energyCost: 30f, cooldown: 12f,
                new SkillTimeline(0.6f, 0f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Circle, radius: 3f, centerOffset: 2f),
                new SkillEffectSpec(damageCoefficient: 4.0f)),
            // 방어 창과 반격 범위가 같은 전방 120도다. 창이 끝날 때 성공했으면 한 번 반격한다
            new SkillDefinition("skill_warrior_counter", EPlayerClass.Warrior, slot: 3, isSignature: false, energyCost: 25f, cooldown: 12f,
                new SkillTimeline(0f, 0.5f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Cone, radius: 3f, arcAngle: 120f),
                new SkillEffectSpec(damageCoefficient: 3.5f)),

            new SkillDefinition("skill_sorcerer_spell_bolt", EPlayerClass.Sorcerer, slot: 0, isSignature: false, energyCost: 15f, cooldown: 4f,
                new SkillTimeline(0.15f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Projectile, castRange: 10f, width: 0.5f, projectileSpeed: 16f),
                new SkillEffectSpec(damageCoefficient: 1.6f)),
            // 4초 동안 0.5초마다, 총 4.5A
            new SkillDefinition("skill_sorcerer_flame_field", EPlayerClass.Sorcerer, slot: 1, isSignature: true, energyCost: 30f, cooldown: 12f,
                new SkillTimeline(0.3f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Point, ESkillShape.Circle, castRange: 8f, radius: 2.5f),
                new SkillEffectSpec(tickCoefficient: 0.5625f, tickInterval: 0.5f, duration: 4f)),
            new SkillDefinition("skill_sorcerer_binding_field", EPlayerClass.Sorcerer, slot: 2, isSignature: false, energyCost: 25f, cooldown: 14f,
                new SkillTimeline(0.3f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Point, ESkillShape.Circle, castRange: 8f, radius: 3f),
                new SkillEffectSpec(damageCoefficient: 1.0f, rootDuration: 2f)),
            // 준비 시간이 곧 낙하 예고 시간이다
            new SkillDefinition("skill_sorcerer_lightning", EPlayerClass.Sorcerer, slot: 3, isSignature: false, energyCost: 30f, cooldown: 10f,
                new SkillTimeline(0.7f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Point, ESkillShape.Circle, castRange: 9f, radius: 2f),
                new SkillEffectSpec(damageCoefficient: 3.8f)),

            new SkillDefinition("skill_summoner_summon_beast", EPlayerClass.Summoner, slot: 0, isSignature: true, energyCost: 30f, cooldown: 20f,
                new SkillTimeline(0.5f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Self, ESkillShape.Summon),
                new SkillEffectSpec(duration: 20f, companionAttackCoefficient: 0.35f, companionAttackInterval: 0.75f, companionAttackRange: 1.5f)),
            new SkillDefinition("skill_summoner_summon_spirit", EPlayerClass.Summoner, slot: 1, isSignature: false, energyCost: 30f, cooldown: 20f,
                new SkillTimeline(0.5f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Self, ESkillShape.Summon),
                new SkillEffectSpec(duration: 20f, companionAttackCoefficient: 0.45f, companionAttackInterval: 1f, companionAttackRange: 8f)),
            new SkillDefinition("skill_summoner_spirit_strike", EPlayerClass.Summoner, slot: 2, isSignature: false, energyCost: 15f, cooldown: 5f,
                new SkillTimeline(0.2f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Projectile, castRange: 9f, width: 0.5f, projectileSpeed: 16f),
                new SkillEffectSpec(damageCoefficient: 1.8f)),
            // 피해 보너스는 시전자 자신의 두 동료에게만 적용하고 다른 신령사의 결계와 중첩하지 않는다
            new SkillDefinition("skill_summoner_target_barrier", EPlayerClass.Summoner, slot: 3, isSignature: false, energyCost: 25f, cooldown: 12f,
                new SkillTimeline(0.3f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Point, ESkillShape.Circle, castRange: 8f, radius: 3f),
                new SkillEffectSpec(damageCoefficient: 0.7f, duration: 6f, allyDamageBonus: 0.2f))
        };

        // 기존 _skills 배열을 건드리지 않고 새 배열로 둬서 이미 저장된 에셋도 직렬화 기본값으로 이 12종을 받는다.
        // 후딜은 검사 0.2초, 마검사와 궁수는 기획의 기술별 값이다
        [Header("신규 직업 일반 기술 (검사, 마검사, 궁수 Q, W, E, R 4개)")]
        [SerializeField] private SkillDefinition[] _classSkills =
        {
            new SkillDefinition("skill_swordsman_slash", EPlayerClass.Swordsman, slot: 0, isSignature: true, energyCost: 20f, cooldown: 6f,
                new SkillTimeline(0.2f, 0f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Cone, radius: 3f, arcAngle: 120f),
                new SkillEffectSpec(damageCoefficient: 2.2f)),
            new SkillDefinition("skill_swordsman_dash", EPlayerClass.Swordsman, slot: 1, isSignature: false, energyCost: 20f, cooldown: 8f,
                new SkillTimeline(0f, 0.3f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Line, castRange: 4f, width: 1.5f),
                new SkillEffectSpec(damageCoefficient: 1.8f)),
            new SkillDefinition("skill_swordsman_focus_slash", EPlayerClass.Swordsman, slot: 2, isSignature: false, energyCost: 30f, cooldown: 12f,
                new SkillTimeline(0.6f, 0f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Cone, radius: 3f, arcAngle: 80f),
                new SkillEffectSpec(damageCoefficient: 4.0f)),
            // 두 번째 타격은 최초 타격 0.2초 뒤, Active가 그 구간이다
            new SkillDefinition("skill_swordsman_chain_slash", EPlayerClass.Swordsman, slot: 3, isSignature: false, energyCost: 25f, cooldown: 12f,
                new SkillTimeline(0.3f, 0.2f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Cone, radius: 3f, arcAngle: 90f),
                new SkillEffectSpec(damageCoefficient: 1.75f, secondHitCoefficient: 1.75f, secondHitDelay: 0.2f)),

            // 마검사는 스택형이다. 기존 ID와 부적 연결을 지키려고 ID를 판정 형태가 맞는 슬롯으로 옮겼다.
            // 투사체 부적(T20)이 붙은 thunder_wave는 W 검파, 장판 부적(T18)이 붙은 flame_field는 E 장판으로 쓴다
            // Q 수류 방벽, 3초 동안 기절, 속박, 밀림 면역
            new SkillDefinition("skill_mageknight_arcane_slash", EPlayerClass.Mageknight, slot: 0, isSignature: false, energyCost: 20f, cooldown: 14f,
                new SkillTimeline(0.1f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Self, ESkillShape.Buff),
                new SkillEffectSpec(hitImmunityDuration: 3f)),
            // W 검파 해방, 기본 0.85A에 스택당 0.17A를 더해 15스택이면 3.4A. 관통하며 시전 확정 때 스택을 0으로 만든다
            new SkillDefinition("skill_mageknight_thunder_wave", EPlayerClass.Mageknight, slot: 1, isSignature: false, energyCost: 25f, cooldown: 8f,
                new SkillTimeline(0.35f, 0f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Projectile, castRange: 14f, width: 2f, projectileSpeed: 20f),
                new SkillEffectSpec(damageCoefficient: 0.85f, maxTargets: 8, stackCoefficient: 0.17f)),
            // E 공간 절단진, 지정 지점에 고정, 4초 동안 0.5초마다 8회 총 4.5A, 생성 때 스택 +5
            new SkillDefinition("skill_mageknight_flame_field", EPlayerClass.Mageknight, slot: 2, isSignature: true, energyCost: 30f, cooldown: 12f,
                new SkillTimeline(0.3f, 0f, 0.15f),
                new SkillArea(ESkillAimMode.Point, ESkillShape.Circle, castRange: 7f, radius: 2.5f),
                new SkillEffectSpec(tickCoefficient: 0.5625f, tickInterval: 0.5f, duration: 4f, stackGain: 5)),
            // R 섬광 일섬, 6m 경로를 지나며 3.0A로 한 번 베고 발동 구간 0.45초가 끝날 때 경로 끝으로 순간 이동, 도착 때 스택 +10
            new SkillDefinition("skill_mageknight_seal_slash", EPlayerClass.Mageknight, slot: 3, isSignature: false, energyCost: 30f, cooldown: 12f,
                new SkillTimeline(0.15f, 0.45f, 0.2f),
                new SkillArea(ESkillAimMode.Direction, ESkillShape.Line, castRange: 6f, width: 2f),
                new SkillEffectSpec(damageCoefficient: 3.0f, stackGain: 10, teleportToEnd: true)),

            // 궁수는 속성형이다. 기존 ID와 부적 연결을 지키려고 ID를 형태가 맞는 슬롯으로 옮겼다.
            // 대표 기술 piercing_shot(T23 6효과)은 R 폭발, scatter_shot은 E 불이다. Q, W, E는 이후 기본 공격의 속성만 바꾼다
            // Q 얼음 부여, 맞힌 일반 적의 이동 속도를 잠시 늦춘다
            new SkillDefinition("skill_archer_rapid_shot", EPlayerClass.Archer, slot: 0, isSignature: false, energyCost: 10f, cooldown: 6f,
                new SkillTimeline(0.1f, 0f, 0.2f),
                new SkillArea(ESkillAimMode.Self, ESkillShape.Buff),
                new SkillEffectSpec(archerElement: EArcherElement.Ice)),
            // W 바람 부여, 바람인 동안 기본 공격 주기가 짧아지고 화살이 빨라진다
            new SkillDefinition("skill_archer_retreat_shot", EPlayerClass.Archer, slot: 1, isSignature: false, energyCost: 10f, cooldown: 6f,
                new SkillTimeline(0.1f, 0f, 0.2f),
                new SkillArea(ESkillAimMode.Self, ESkillShape.Buff),
                new SkillEffectSpec(archerElement: EArcherElement.Wind)),
            // E 불 부여, 맞힌 적에게 화상 지속 피해를 건다
            new SkillDefinition("skill_archer_scatter_shot", EPlayerClass.Archer, slot: 2, isSignature: false, energyCost: 10f, cooldown: 6f,
                new SkillTimeline(0.1f, 0f, 0.2f),
                new SkillArea(ESkillAimMode.Self, ESkillShape.Buff),
                new SkillEffectSpec(archerElement: EArcherElement.Fire)),
            // R 속성 폭발, 10스택을 모두 써서 사거리 안 커서 지점에 큰 원형 폭발. 준비 0.7초가 차지 동작이다
            new SkillDefinition("skill_archer_piercing_shot", EPlayerClass.Archer, slot: 3, isSignature: true, energyCost: 20f, cooldown: 3f,
                new SkillTimeline(0.7f, 0f, 0.3f),
                new SkillArea(ESkillAimMode.Point, ESkillShape.Circle, castRange: 12f, radius: 5f),
                new SkillEffectSpec(damageCoefficient: 5.6f, requiredStacks: 10))
        };

        // TODO(민성): 강림 후딜은 기획에 없어 신령사 공통 후딜 0.15초를 자리값으로 둔다. 기획 확정 뒤 교체
        [Header("공명 강림 (Z, X, C)")]
        [SerializeField] private AscensionDefinition[] _ascensions =
        {
            new AscensionDefinition("skill_summoner_ascension_single", slot: 0, resonanceCost: 60f, castRange: 10f, radius: 1.5f,
                windup: 0.6f, recovery: 0.15f, damageCoefficient: 12f, rootDuration: 0f, singleTarget: true),
            new AscensionDefinition("skill_summoner_ascension_area", slot: 1, resonanceCost: 80f, castRange: 10f, radius: 4f,
                windup: 0.7f, recovery: 0.15f, damageCoefficient: 7f, rootDuration: 0f, singleTarget: false),
            // 보스는 제어 허용 구간에서만 최대 1초 속박하며 이동을 강제하지 않는다
            new AscensionDefinition("skill_summoner_ascension_control", slot: 2, resonanceCost: 40f, castRange: 8f, radius: 3f,
                windup: 0.4f, recovery: 0.15f, damageCoefficient: 4f, rootDuration: 2f, singleTarget: false)
        };
        [SerializeField] private float _ascensionSharedCooldown = 3f; // 초, 세 강림 공용

        // 같은 행동이 여러 적을 맞혀도 한 번만 얻는다. 소환 자체와 강림 피해는 공명을 주지 않는다
        [Header("공명")]
        [SerializeField] private float _resonanceMax             = 100f;
        [SerializeField] private float _resonancePerBasicHit     = 1f;   // 본체 기본 공격 적중
        [SerializeField] private float _resonancePerSkillHit     = 4f;   // 본체 공격 기술 최초 유효 적중, 지속 장판은 생성당 한 번
        [SerializeField] private float _resonancePerCompanionHit = 0.5f; // 동료 공격 한 동작의 최초 유효 적중
        [SerializeField] private float _resonancePerCastCap      = 8f;   // 부적 보너스를 더한 본체 공명의 시전당 상한

        [Header("계약 동료 공통")]
        [SerializeField] private float _companionHealthRate     = 0.6f; // 소환 완료 때 본체 최대 체력에 대한 비율
        [SerializeField] private float _companionDefenseRate    = 1f;   // 본체 방어력에 대한 비율
        [SerializeField] private float _companionSearchRadius   = 10f;  // m, 결계 밖일 때 본체 기준 적 탐색 반경
        [SerializeField] private float _companionRecallDistance = 12f;  // m, 이 거리부터 추격보다 본체 복귀 우선
        [SerializeField] private float _companionWarpDistance   = 18f;  // m, 이 거리부터 본체 뒤로 즉시 복귀
        [SerializeField] private float _companionStuckSeconds   = 2f;   // 초, 접근할 길이 없으면 즉시 복귀
        [SerializeField] private float _companionWarpOffset     = 1.5f; // m, 즉시 복귀 지점의 본체 뒤 거리
        // TODO(민성): 돌등, 솔빛 이동 속도는 기획 미정이라 플레이어 이동 속도와 같은 자리값을 둔다
        [SerializeField] private float _companionMoveSpeed      = 5f;   // m/s

        // 마검사 스택 상한이다(궁수는 아래 궁수 스택 상한). 마검사는 적을 하나 이상 맞힌 기본 공격 한 번에 얻고 기술 적중은 기술의 StackGain만 준다
        [Header("직업 스택 (마검사, 궁수)")]
        [SerializeField] private int _mageknightStackMax      = 15;
        [SerializeField] private int _classStackPerBasicSwing = 1;

        // 기본 공격 화살이 적을 맞혔을 때만 쌓는다. 직전에 맞힌 속성과 다르면 전환 값, 같으면 유지 값이다. 상한은 궁수 스택 상한을 쓴다
        [Header("궁수 속성")]
        [SerializeField] private int   _archerStackMax           = 10;   // 궁수만 쓰는 스택 상한, R 필요 스택과 같아야 한다
        [SerializeField] private int   _archerStackPerSameHit    = 1;
        [SerializeField] private int   _archerStackPerSwitchHit  = 2;
        [SerializeField] private float _windAttackIntervalScale  = 0.8f; // 바람인 동안 기본 공격 단계 주기에 곱한다
        [SerializeField] private float _windProjectileSpeedScale = 1.5f; // 바람인 동안 기본 공격 화살 속도에 곱한다
        [SerializeField] private float _burnDuration             = 3f;   // 초, 불 화살 적중 뒤 화상 유지. 다시 맞으면 이 시간으로 갱신한다
        [SerializeField] private float _burnTickInterval         = 0.5f; // 초
        [SerializeField] private float _burnTickCoefficient      = 0.1f; // 공격력 배율, 화상 1회분
        [SerializeField] private float _chillDuration            = 1.5f; // 초, 얼음 화살 적중 뒤 둔화 유지
        [SerializeField] private float _chillSlowRate            = 0.4f; // 0에서 1 사이, 이동 속도 감소율. 보스는 받지 않는다

        [NonSerialized] private DefinitionLookup<SkillDefinition>     _skillLookup     = new DefinitionLookup<SkillDefinition>();
        [NonSerialized] private DefinitionLookup<AscensionDefinition> _ascensionLookup = new DefinitionLookup<AscensionDefinition>();
        [NonSerialized] private SkillDefinition[]                     _allSkills;

        /// <summary> 구 직업 12종 뒤에 신규 직업 12종을 이은 전체 일반 기술 24종, 처음 읽을 때 한 번 만들고 재사용한다 </summary>
        public IReadOnlyList<SkillDefinition>     Skills                   => GetAllSkills();
        public IReadOnlyList<AscensionDefinition> Ascensions               => _ascensions;
        public float                              AscensionSharedCooldown  => _ascensionSharedCooldown;
        public float                              ResonanceMax             => _resonanceMax;
        public float                              ResonancePerBasicHit     => _resonancePerBasicHit;
        public float                              ResonancePerSkillHit     => _resonancePerSkillHit;
        public float                              ResonancePerCompanionHit => _resonancePerCompanionHit;
        public float                              ResonancePerCastCap      => _resonancePerCastCap;
        public float                              CompanionHealthRate      => _companionHealthRate;
        public float                              CompanionDefenseRate     => _companionDefenseRate;
        public float                              CompanionSearchRadius    => _companionSearchRadius;
        public float                              CompanionRecallDistance  => _companionRecallDistance;
        public float                              CompanionWarpDistance    => _companionWarpDistance;
        public float                              CompanionStuckSeconds    => _companionStuckSeconds;
        public float                              CompanionWarpOffset      => _companionWarpOffset;
        public float                              CompanionMoveSpeed       => _companionMoveSpeed;
        public int                                MageknightStackMax       => _mageknightStackMax;

        /// <summary> 직업별 스택 상한, 스택을 쓰지 않는 직업은 0이다 </summary>
        public int GetStackMax(EPlayerClass playerClass)
        {
            switch (playerClass)
            {
                case EPlayerClass.Archer:     return _archerStackMax;
                case EPlayerClass.Mageknight: return _mageknightStackMax;
                default:                      return 0;
            }
        }

        public int                                ClassStackPerBasicSwing  => _classStackPerBasicSwing;
        public int                                ArcherStackMax           => _archerStackMax;
        public int                                ArcherStackPerSameHit    => _archerStackPerSameHit;
        public int                                ArcherStackPerSwitchHit  => _archerStackPerSwitchHit;
        public float                              WindAttackIntervalScale  => _windAttackIntervalScale;
        public float                              WindProjectileSpeedScale => _windProjectileSpeedScale;
        public float                              BurnDuration             => _burnDuration;
        public float                              BurnTickInterval         => _burnTickInterval;
        public float                              BurnTickCoefficient      => _burnTickCoefficient;
        public float                              ChillDuration            => _chillDuration;
        public float                              ChillSlowRate            => _chillSlowRate;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            InvalidateLookups();
        }

        protected override void OnValidate()
        {
            InvalidateLookups();
            base.OnValidate();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 일반 기술 24종 안에 있는지 본다, 강림 ID는 포함하지 않는다 </summary>
        public bool ContainsSkill(string skillId)
        {
            return _skillLookup.Contains(GetAllSkills(), skillId);
        }

        public bool TryGetSkill(string skillId, out SkillDefinition skill)
        {
            return _skillLookup.TryGet(GetAllSkills(), skillId, out skill);
        }

        private SkillDefinition[] GetAllSkills()
        {
            if (_allSkills != null)
            {
                return _allSkills;
            }

            int legacyCount = (_skills == null) ? 0 : _skills.Length;
            int classCount  = (_classSkills == null) ? 0 : _classSkills.Length;
            var all         = new SkillDefinition[legacyCount + classCount];
            for (int i = 0; i < legacyCount; ++i)
            {
                all[i] = _skills[i];
            }

            for (int i = 0; i < classCount; ++i)
            {
                all[legacyCount + i] = _classSkills[i];
            }

            _allSkills = all;
            return all;
        }

        public bool ContainsAscension(string ascensionId)
        {
            return _ascensionLookup.Contains(_ascensions, ascensionId);
        }

        public bool TryGetAscension(string ascensionId, out AscensionDefinition ascension)
        {
            return _ascensionLookup.TryGet(_ascensions, ascensionId, out ascension);
        }

        /// <summary> 직업과 Q, W, E, R 슬롯으로 기술을 찾는다, 할당 없이 선형 탐색한다 </summary>
        public bool TryGetSkillBySlot(EPlayerClass playerClass, int slot, out SkillDefinition skill)
        {
            skill = null;
            SkillDefinition[] all = GetAllSkills();
            for (int i = 0; i < all.Length; ++i)
            {
                SkillDefinition candidate = all[i];
                if ((candidate != null) && (candidate.PlayerClass == playerClass) && (candidate.Slot == slot))
                {
                    skill = candidate;
                    return true;
                }
            }

            return false;
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(SkillCatalogSO);

            DataValidation.RequireDefinitions(errors, OWNER, nameof(_skills), _skills);
            DataValidation.RequireCount(errors, OWNER, nameof(_skills), _skills, SKILL_COUNT);
            DataValidation.RequireDefinitions(errors, OWNER, nameof(_classSkills), _classSkills);
            DataValidation.RequireCount(errors, OWNER, nameof(_classSkills), _classSkills, CLASS_SKILL_COUNT);
            DataValidation.RequireDefinitions(errors, OWNER, nameof(_ascensions), _ascensions);
            DataValidation.RequireCount(errors, OWNER, nameof(_ascensions), _ascensions, Constants.Combat.SUMMON_ENHANCEMENT_COUNT);

            DataValidation.RequirePositive(errors, OWNER, nameof(_ascensionSharedCooldown), _ascensionSharedCooldown);
            DataValidation.RequirePositive(errors, OWNER, nameof(_resonanceMax), _resonanceMax);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_resonancePerBasicHit), _resonancePerBasicHit);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_resonancePerSkillHit), _resonancePerSkillHit);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_resonancePerCompanionHit), _resonancePerCompanionHit);
            DataValidation.RequireRange(errors, OWNER, nameof(_resonancePerCastCap), _resonancePerCastCap, _resonancePerSkillHit, _resonanceMax);

            DataValidation.RequirePositive(errors, OWNER, nameof(_companionHealthRate), _companionHealthRate);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_companionDefenseRate), _companionDefenseRate);
            DataValidation.RequirePositive(errors, OWNER, nameof(_companionSearchRadius), _companionSearchRadius);
            DataValidation.RequirePositive(errors, OWNER, nameof(_companionRecallDistance), _companionRecallDistance);
            DataValidation.RequireRange(errors, OWNER, nameof(_companionWarpDistance), _companionWarpDistance, _companionRecallDistance, float.MaxValue);
            DataValidation.RequirePositive(errors, OWNER, nameof(_companionStuckSeconds), _companionStuckSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_companionWarpOffset), _companionWarpOffset);
            DataValidation.RequirePositive(errors, OWNER, nameof(_companionMoveSpeed), _companionMoveSpeed);
            DataValidation.RequirePositive(errors, OWNER, nameof(_mageknightStackMax), _mageknightStackMax);
            DataValidation.RequirePositive(errors, OWNER, nameof(_archerStackMax), _archerStackMax);
            DataValidation.RequireRange(errors, OWNER, nameof(_classStackPerBasicSwing), _classStackPerBasicSwing, 0f, _mageknightStackMax);
            DataValidation.RequireRange(errors, OWNER, nameof(_archerStackPerSameHit), _archerStackPerSameHit, 0f, _archerStackMax);
            DataValidation.RequireRange(errors, OWNER, nameof(_archerStackPerSwitchHit), _archerStackPerSwitchHit, 0f, _archerStackMax);
            DataValidation.RequirePositiveAtMost(errors, OWNER, nameof(_windAttackIntervalScale), _windAttackIntervalScale, 1f);
            DataValidation.RequireRange(errors, OWNER, nameof(_windProjectileSpeedScale), _windProjectileSpeedScale, 1f, float.MaxValue);
            DataValidation.RequirePositive(errors, OWNER, nameof(_burnDuration), _burnDuration);
            DataValidation.RequirePositiveAtMost(errors, OWNER, nameof(_burnTickInterval), _burnTickInterval, _burnDuration);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_burnTickCoefficient), _burnTickCoefficient);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_chillDuration), _chillDuration);
            DataValidation.RequireRange(errors, OWNER, nameof(_chillSlowRate), _chillSlowRate, 0f, 1f);

            ValidateSkills(errors, OWNER);
            ValidateClassSlots(errors, OWNER);
            ValidateAscensions(errors, OWNER);
        }

        private void ValidateSkills(List<string> errors, string owner)
        {
            SkillDefinition[] all = GetAllSkills();
            for (int i = 0; i < all.Length; ++i)
            {
                SkillDefinition skill = all[i];
                if (skill == null)
                {
                    continue;
                }

                string field = $"{nameof(_skills)}[{skill.DefinitionId}]";

                for (int j = 0; j < i; ++j)
                {
                    if ((all[j] != null) && (string.Equals(all[j].DefinitionId, skill.DefinitionId, StringComparison.Ordinal)))
                    {
                        errors.Add($"{owner}.{field}: 두 배열에 같은 ID가 있다");
                    }
                }

                DataValidation.RequireRange(errors, owner, field + ".Slot", skill.Slot, 0f, Constants.Player.SKILL_SLOT_COUNT - 1);
                DataValidation.RequirePositive(errors, owner, field + ".EnergyCost", skill.EnergyCost);
                DataValidation.RequirePositive(errors, owner, field + ".Cooldown", skill.Cooldown);

                if ((skill.Timeline == null) || (skill.Area == null) || (skill.Effect == null))
                {
                    errors.Add($"{owner}.{field}: 타임라인, 범위, 효과 명세 중 비어 있는 것이 있다");
                    continue;
                }

                DataValidation.RequireNonNegative(errors, owner, field + ".Timeline.Windup", skill.Timeline.Windup);
                DataValidation.RequireNonNegative(errors, owner, field + ".Timeline.Active", skill.Timeline.Active);
                DataValidation.RequireNonNegative(errors, owner, field + ".Timeline.Recovery", skill.Timeline.Recovery);

                ValidateArea(errors, owner, field, skill);
                ValidateEffect(errors, owner, field, skill.Effect);
                DataValidation.RequireRange(errors, owner, field + ".Effect.RequiredStacks", skill.Effect.RequiredStacks, 0f, GetStackMax(skill.PlayerClass));
            }
        }

        private static void ValidateArea(List<string> errors, string owner, string field, SkillDefinition skill)
        {
            SkillArea area      = skill.Area;
            string    areaField = field + ".Area";

            if (area.AimMode == ESkillAimMode.Point)
            {
                DataValidation.RequirePositive(errors, owner, areaField + ".CastRange", area.CastRange);
            }

            DataValidation.RequireNonNegative(errors, owner, areaField + ".CenterOffset", area.CenterOffset);

            switch (area.Shape)
            {
                case ESkillShape.Cone:
                    DataValidation.RequirePositive(errors, owner, areaField + ".Radius", area.Radius);
                    DataValidation.RequirePositiveAtMost(errors, owner, areaField + ".ArcAngle", area.ArcAngle, MAX_ARC_ANGLE);
                    break;
                case ESkillShape.Circle:
                    DataValidation.RequirePositive(errors, owner, areaField + ".Radius", area.Radius);
                    break;
                case ESkillShape.Line:
                    DataValidation.RequirePositive(errors, owner, areaField + ".CastRange", area.CastRange);
                    DataValidation.RequirePositive(errors, owner, areaField + ".Width", area.Width);
                    DataValidation.RequirePositive(errors, owner, field + ".Timeline.Active", skill.Timeline.Active);
                    break;
                case ESkillShape.Projectile:
                    DataValidation.RequirePositive(errors, owner, areaField + ".CastRange", area.CastRange);
                    DataValidation.RequirePositive(errors, owner, areaField + ".Width", area.Width);
                    DataValidation.RequirePositive(errors, owner, areaField + ".ProjectileSpeed", area.ProjectileSpeed);
                    break;
                case ESkillShape.Buff:
                    // 자기 강화는 피격이상 면역이나 궁수 속성 부여 중 하나는 있어야 한다
                    if ((skill.Effect.HitImmunityDuration <= 0f) && (skill.Effect.ArcherElement == EArcherElement.None))
                    {
                        errors.Add($"{owner}.{field}.Effect: 자기 강화에 면역 시간과 궁수 속성이 모두 없다");
                    }

                    break;
                case ESkillShape.Summon:
                    DataValidation.RequirePositive(errors, owner, field + ".Effect.Duration", skill.Effect.Duration);
                    DataValidation.RequirePositive(errors, owner, field + ".Effect.CompanionAttackInterval", skill.Effect.CompanionAttackInterval);
                    DataValidation.RequirePositive(errors, owner, field + ".Effect.CompanionAttackRange", skill.Effect.CompanionAttackRange);
                    DataValidation.RequirePositive(errors, owner, field + ".Effect.CompanionAttackCoefficient", skill.Effect.CompanionAttackCoefficient);
                    break;
                default:
                    errors.Add($"{owner}.{areaField}.Shape: 처리하지 않는 형태 {area.Shape}");
                    break;
            }

            if ((skill.Effect.TeleportToEnd) && (area.Shape != ESkillShape.Line))
            {
                errors.Add($"{owner}.{field}.Effect.TeleportToEnd: 경로 끝 순간 이동은 직선 형태에만 쓴다");
            }

            if ((skill.Effect.ArcherElement != EArcherElement.None)
                && ((skill.PlayerClass != EPlayerClass.Archer) || (area.Shape != ESkillShape.Buff)))
            {
                errors.Add($"{owner}.{field}.Effect.ArcherElement: 속성 부여는 궁수의 자기 강화 기술에만 쓴다");
            }

            if ((skill.Effect.RequiredStacks > 0) && (skill.Effect.StackCoefficient > 0f))
            {
                errors.Add($"{owner}.{field}.Effect.RequiredStacks: 필요 스택과 스택 계수를 함께 쓰지 않는다");
            }
        }

        private static void ValidateEffect(List<string> errors, string owner, string field, SkillEffectSpec effect)
        {
            field += ".Effect";

            DataValidation.RequireNonNegative(errors, owner, field + ".DamageCoefficient", effect.DamageCoefficient);
            DataValidation.RequireNonNegative(errors, owner, field + ".TickCoefficient", effect.TickCoefficient);
            DataValidation.RequireNonNegative(errors, owner, field + ".Duration", effect.Duration);
            DataValidation.RequireNonNegative(errors, owner, field + ".RootDuration", effect.RootDuration);
            DataValidation.RequireNonNegative(errors, owner, field + ".AllyDamageBonus", effect.AllyDamageBonus);
            DataValidation.RequireNonNegative(errors, owner, field + ".CompanionAttackCoefficient", effect.CompanionAttackCoefficient);

            bool dealsDamage = (effect.DamageCoefficient > 0f) || (effect.TickCoefficient > 0f) || (effect.CompanionAttackCoefficient > 0f)
                || (effect.HitImmunityDuration > 0f) || (effect.ArcherElement != EArcherElement.None);
            if (!dealsDamage)
            {
                errors.Add($"{owner}.{field}: 직접 피해, 지속 피해, 동료 공격 계수, 면역 시간, 궁수 속성이 모두 0이다");
            }

            if (effect.TickCoefficient > 0f)
            {
                DataValidation.RequirePositive(errors, owner, field + ".TickInterval", effect.TickInterval);
                DataValidation.RequirePositive(errors, owner, field + ".Duration", effect.Duration);
            }

            if (effect.AllyDamageBonus > 0f)
            {
                DataValidation.RequirePositive(errors, owner, field + ".Duration", effect.Duration);
            }

            DataValidation.RequireNonNegative(errors, owner, field + ".SecondHitCoefficient", effect.SecondHitCoefficient);
            DataValidation.RequireNonNegative(errors, owner, field + ".SecondHitDelay", effect.SecondHitDelay);
            DataValidation.RequireNonNegative(errors, owner, field + ".ProjectileCount", effect.ProjectileCount);
            DataValidation.RequireNonNegative(errors, owner, field + ".SpreadAngle", effect.SpreadAngle);
            DataValidation.RequireNonNegative(errors, owner, field + ".MaxTargets", effect.MaxTargets);
            DataValidation.RequireNonNegative(errors, owner, field + ".RepeatCount", effect.RepeatCount);
            DataValidation.RequireNonNegative(errors, owner, field + ".RepeatInterval", effect.RepeatInterval);
            DataValidation.RequireNonNegative(errors, owner, field + ".RetreatDistance", effect.RetreatDistance);
            DataValidation.RequireNonNegative(errors, owner, field + ".RetreatDuration", effect.RetreatDuration);
            DataValidation.RequireNonNegative(errors, owner, field + ".StackGain", effect.StackGain);
            DataValidation.RequireNonNegative(errors, owner, field + ".StackCoefficient", effect.StackCoefficient);
            DataValidation.RequireNonNegative(errors, owner, field + ".HitImmunityDuration", effect.HitImmunityDuration);
            DataValidation.RequireNonNegative(errors, owner, field + ".RequiredStacks", effect.RequiredStacks);

            if (effect.SecondHitCoefficient > 0f)
            {
                DataValidation.RequirePositive(errors, owner, field + ".SecondHitDelay", effect.SecondHitDelay);
            }

            if (effect.ProjectileCount > 1)
            {
                DataValidation.RequirePositive(errors, owner, field + ".SpreadAngle", effect.SpreadAngle);
            }

            if (effect.RepeatCount > 1)
            {
                DataValidation.RequirePositive(errors, owner, field + ".RepeatInterval", effect.RepeatInterval);
            }

            if (effect.RetreatDistance > 0f)
            {
                DataValidation.RequirePositive(errors, owner, field + ".RetreatDuration", effect.RetreatDuration);
            }
        }

        // 직업마다 슬롯 0부터 3까지 한 번씩, 대표 기술은 하나만 있어야 한다
        private void ValidateClassSlots(List<string> errors, string owner)
        {
            SkillDefinition[] all = GetAllSkills();

            int classCount = Enum.GetValues(typeof(EPlayerClass)).Length;
            for (int c = 0; c < classCount; ++c)
            {
                var  playerClass    = (EPlayerClass)c;
                int  signatureCount = 0;
                int  skillCount     = 0;
                bool slotsComplete  = true;

                for (int slot = 0; slot < Constants.Player.SKILL_SLOT_COUNT; ++slot)
                {
                    int slotHits = 0;
                    for (int i = 0; i < all.Length; ++i)
                    {
                        SkillDefinition skill = all[i];
                        if ((skill != null) && (skill.PlayerClass == playerClass) && (skill.Slot == slot))
                        {
                            ++slotHits;
                        }
                    }

                    if (slotHits != 1)
                    {
                        slotsComplete = false;
                    }
                }

                for (int i = 0; i < all.Length; ++i)
                {
                    SkillDefinition skill = all[i];
                    if ((skill == null) || (skill.PlayerClass != playerClass))
                    {
                        continue;
                    }

                    ++skillCount;
                    if (skill.IsSignature)
                    {
                        ++signatureCount;
                    }
                }

                string field = $"{nameof(_skills)}[{playerClass}]";
                if ((skillCount != Constants.Combat.SKILLS_PER_CLASS) || (!slotsComplete))
                {
                    errors.Add($"{owner}.{field}: 기술 {skillCount}개, 슬롯마다 정확히 하나씩 {Constants.Combat.SKILLS_PER_CLASS}개여야 한다");
                }

                if (signatureCount != SIGNATURE_SKILLS_PER_CLASS)
                {
                    errors.Add($"{owner}.{field}: 대표 기술 수 {signatureCount} != {SIGNATURE_SKILLS_PER_CLASS}");
                }
            }
        }

        private void ValidateAscensions(List<string> errors, string owner)
        {
            if (_ascensions == null)
            {
                return;
            }

            for (int i = 0; i < _ascensions.Length; ++i)
            {
                AscensionDefinition ascension = _ascensions[i];
                if (ascension == null)
                {
                    continue;
                }

                string field = $"{nameof(_ascensions)}[{ascension.DefinitionId}]";

                DataValidation.RequireRange(errors, owner, field + ".Slot", ascension.Slot, 0f, Constants.Player.SUMMON_SLOT_COUNT - 1);
                DataValidation.RequirePositiveAtMost(errors, owner, field + ".ResonanceCost", ascension.ResonanceCost, _resonanceMax);
                DataValidation.RequirePositive(errors, owner, field + ".CastRange", ascension.CastRange);
                DataValidation.RequirePositive(errors, owner, field + ".Radius", ascension.Radius);
                DataValidation.RequireNonNegative(errors, owner, field + ".Windup", ascension.Windup);
                DataValidation.RequireNonNegative(errors, owner, field + ".Recovery", ascension.Recovery);
                DataValidation.RequirePositive(errors, owner, field + ".DamageCoefficient", ascension.DamageCoefficient);
                DataValidation.RequireNonNegative(errors, owner, field + ".RootDuration", ascension.RootDuration);

                for (int j = 0; j < i; ++j)
                {
                    if ((_ascensions[j] != null) && (_ascensions[j].Slot == ascension.Slot))
                    {
                        errors.Add($"{owner}.{field}.Slot: 슬롯 {ascension.Slot}이 '{_ascensions[j].DefinitionId}'와 겹친다");
                    }
                }
            }
        }

        private void InvalidateLookups()
        {
            _allSkills = null;
            _skillLookup.Invalidate();
            _ascensionLookup.Invalidate();
        }
    }
}
