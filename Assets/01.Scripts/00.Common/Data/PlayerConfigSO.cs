// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 플레이어의 이동, 회피, 기력, 기본 능력치, 전투 공통 상한, 물약 사용, 입력 보관, 직업별 기본 공격 </summary>
    [CreateAssetMenu(fileName = "PlayerConfigSO", menuName = "Shinshi/PlayerConfigSO")]
    public class PlayerConfigSO : GameDataSO
    {
        /// <summary> 직업별 기본 공격 명세 </summary>
        [Serializable]
        public class BasicAttackSpec
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private float _damageCoefficient; // 공격력 배율
            [SerializeField] private float _interval;          // 초, 공격 주기
            [SerializeField] private float _range;             // m
            [SerializeField] private float _arcAngle;          // 도, 0이면 부채꼴이 아닌 투사체
            [SerializeField] private float _windup;            // 초, 입력 승인부터 타격 발생까지. 0이면 승인 틱에 바로 판정
            [SerializeField] private float _projectileSpeed;   // m/초, 0보다 크면 원거리 기본 공격이 이 속도로 날아가는 투사체
            [SerializeField] private float _projectileWidth;   // m, 투사체 폭
            [SerializeField] private bool  _cancelOnMove;      // 준비 중 이동, 회피 입력이 오면 발사하지 않고 취소한다

            // 단계 명세가 2개 이상일 때만 서버 콤보다. 아니면 단일 공격 값(위 계수, 주기, 준비 시간)을 매번 쓴다
            [SerializeField] private float           _comboResetSeconds;              // 초, 다음 공격이 가능해진 뒤 이만큼 쉬면 1단으로 돌아간다
            [SerializeField] private ComboStepSpec[] _comboSteps = Array.Empty<ComboStepSpec>();

            public float DamageCoefficient => _damageCoefficient;
            public float Interval          => _interval;
            public float Range             => _range;
            public float ArcAngle          => _arcAngle;
            public float Windup            => _windup;
            public float ProjectileSpeed   => _projectileSpeed;
            public float ProjectileWidth   => _projectileWidth;
            public bool  CancelOnMove      => _cancelOnMove;

            /****************************************
            *                Methods
            ****************************************/

            public BasicAttackSpec(float damageCoefficient, float interval, float range, float arcAngle)
            {
                _damageCoefficient = damageCoefficient;
                _interval          = interval;
                _range             = range;
                _arcAngle          = arcAngle;
            }

            public BasicAttackSpec(float damageCoefficient, float interval, float range, float arcAngle, float windup,
                float projectileSpeed, float projectileWidth, bool cancelOnMove)
                : this(damageCoefficient, interval, range, arcAngle)
            {
                _windup          = windup;
                _projectileSpeed = projectileSpeed;
                _projectileWidth = projectileWidth;
                _cancelOnMove    = cancelOnMove;
            }

            public BasicAttackSpec(float damageCoefficient, float interval, float range, float arcAngle, float windup,
                float projectileSpeed, float projectileWidth, bool cancelOnMove, float comboResetSeconds, ComboStepSpec[] comboSteps)
                : this(damageCoefficient, interval, range, arcAngle, windup, projectileSpeed, projectileWidth, cancelOnMove)
            {
                _comboResetSeconds = comboResetSeconds;
                _comboSteps        = comboSteps;
            }

            public bool  HasCombo          => (_comboSteps != null) && (_comboSteps.Length > 1);
            public int   ComboStepCount    => HasCombo ? _comboSteps.Length : 1;
            public float ComboResetSeconds => _comboResetSeconds;

            public float GetDamageCoefficient(int step) { return HasStep(step) ? _comboSteps[step].DamageCoefficient : _damageCoefficient; }
            public float GetInterval(int step)          { return HasStep(step) ? _comboSteps[step].Interval : _interval; }
            public float GetWindup(int step)            { return HasStep(step) ? _comboSteps[step].Windup : _windup; }
            public bool  IsHitImmune(int step)          { return HasStep(step) && _comboSteps[step].HitImmune; }
            public int   GetShotCount(int step)        { return HasStep(step) ? _comboSteps[step].ShotCount : 1; }
            public float GetShotInterval(int step)     { return HasStep(step) ? _comboSteps[step].ShotInterval : 0f; }
            public int   GetMotionStep(int step)       { return HasStep(step) && (_comboSteps[step].ShotCount > 1) ? _comboSteps[step].MotionStep : step; }
            public float GetMotionSpeedScale(int step) { return HasStep(step) ? _comboSteps[step].MotionSpeedScale : 1f; }
            public bool  IsHeavy(int step)             { return HasStep(step) && _comboSteps[step].Heavy; }

            public ComboStepSpec GetComboStep(int index) { return _comboSteps[index]; }

            /// <summary> 이 단계에서 실제로 나가는 발수. 부채꼴 판정은 한 번, 투사체만 연발한다 </summary>
            public int GetFiredShotCount(int step)
            {
                return ((_arcAngle <= 0f) && (_projectileSpeed > 0f)) ? Mathf.Clamp(GetShotCount(step), 1, MAX_BASIC_SHOTS) : 1;
            }

            /// <summary>
            /// 기본 공격을 시작한 뒤 서버가 이동을 보관하는 시간. 마지막 발사까지만 막고 그 뒤 이동은 남은 동작을 끊는다.
            /// 이동으로 준비가 끊기는 공격은 막지 않는다
            /// </summary>
            public float GetMoveHoldSeconds(int step)
            {
                if (_cancelOnMove)
                {
                    return 0f;
                }

                int shots = GetFiredShotCount(step);
                return GetWindup(step) + ((shots - 1) * GetShotInterval(step));
            }

            /// <summary> 단계를 아직 모를 때 쓰는 가장 긴 이동 보관 시간 </summary>
            public float GetMaxMoveHoldSeconds()
            {
                float max = 0f;
                for (int step = 0; step < ComboStepCount; ++step)
                {
                    max = Mathf.Max(max, GetMoveHoldSeconds(step));
                }

                return max;
            }

            private bool HasStep(int step)
            {
                return HasCombo && (step >= 0) && (step < _comboSteps.Length);
            }
        }

        /// <summary> 서버 콤보 기본 공격의 한 단계, 사거리와 투사체는 직업 명세를 같이 쓴다 </summary>
        [Serializable]
        public class ComboStepSpec
        {
            [SerializeField] private float _damageCoefficient; // 공격력 배율
            [SerializeField] private float _interval;          // 초, 이 단계 시작부터 다음 공격까지
            [SerializeField] private float _windup;            // 초, 이 단계 승인부터 판정까지
            [SerializeField] private bool  _hitImmune;         // 이 단계 동작 동안(주기만큼) 기절, 속박, 밀림을 받지 않는다. 피해는 받는다
            [SerializeField, Range(1, MAX_BASIC_SHOTS)] private int _shotCount = 1;
            [SerializeField, Min(0f)] private float _shotInterval;
            [SerializeField, Min(0)] private int _motionStep;
            [SerializeField, Min(1f)] private float _motionSpeedScale = 1f;
            [SerializeField] private bool _heavy;

            public float DamageCoefficient => _damageCoefficient;
            public float Interval          => _interval;
            public float Windup            => _windup;
            public bool  HitImmune         => _hitImmune;
            public int   ShotCount         => _shotCount;
            public float ShotInterval      => _shotInterval;
            public int   MotionStep        => _motionStep;
            public float MotionSpeedScale  => (_motionSpeedScale > 0f) ? _motionSpeedScale : 1f;
            public bool  Heavy             => _heavy;

            public ComboStepSpec(float damageCoefficient, float interval, float windup, bool hitImmune,
                int shotCount = 1, float shotInterval = 0f, int motionStep = 0, float motionSpeedScale = 1f, bool heavy = false)
            {
                _damageCoefficient = damageCoefficient;
                _interval          = interval;
                _windup            = windup;
                _hitImmune         = hitImmune;
                _shotCount         = shotCount;
                _shotInterval      = shotInterval;
                _motionStep        = motionStep;
                _motionSpeedScale  = motionSpeedScale;
                _heavy             = heavy;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const float MAX_RATE            = 1f;
        private const float MIN_CRIT_MULTIPLIER = 1f;
        private const float MAX_ARC_ANGLE       = 360f;
        private const int   LEGACY_CLASS_COUNT  = 3;
        public const int MAX_BASIC_SHOTS = 8;

        /****************************************
        *                Fields
        ****************************************/

        [Header("이동과 회피")]
        [SerializeField] private float _moveSpeed       = 5f;     // m/s
        [SerializeField] private float _dodgeDistance   = 3f;     // m
        [SerializeField] private float _dodgeDuration   = 0.25f;  // 초
        [SerializeField] private float _dodgeCooldown   = 5f;     // 초
        [SerializeField] private float _dodgeEnergyCost = 0f;

        [Header("기력")]
        [SerializeField] private float _baseMaxEnergy        = 100f;
        [SerializeField] private float _energyRegenPerSecond = 10f;

        [Header("기본 능력치")]
        [SerializeField] private float _baseAttack           = 100f;
        [SerializeField] private float _baseHealth           = 1500f;
        [SerializeField] private float _baseDefense          = 100f;
        [SerializeField] private float _baseCritChance       = 0.05f; // 0에서 1 사이 비율
        [SerializeField] private float _critDamageMultiplier = 2f;

        // 성장 투자와 버프로 오르는 값을 한곳에서 막아야 직업, 부적 조합마다 상한이 갈리지 않는다
        [Header("전투 공통 상한")]
        [SerializeField] private float _critChanceCap            = 0.6f;  // 0에서 1 사이 비율, 버프까지 더한 최종 치명 확률 상한
        [SerializeField] private float _damageReductionCap       = 0.6f;  // 0에서 1 사이 비율, 방어로 얻는 피해 감소율 상한
        [SerializeField] private float _defenseReductionConstant = 1000f; // 감소율 = 방어 / (방어 + 이 값), 적이 맞을 때 쓴다. 물리와 도술에 같이 쓴다

        // 적 쪽 상수와 나눠 둔다. 하나를 같이 낮추면 적 방어의 감소율도 올라 플레이어 피해와 처치 시간이 함께 바뀐다
        [SerializeField] private float _playerDefenseReductionConstant = 750f; // 플레이어와 소환물이 맞을 때 쓰는 값

        // 흡수율은 장비에서 오고 여기는 상한만 둔다. 초당 상한이 없으면 범위 타격으로 대상 수만큼 회복이 늘어난다
        [Header("생명력 흡수")]
        [SerializeField] private float _lifeStealRateCap      = 0.1f;  // 0에서 1 사이 비율, 최종 흡수율 상한
        [SerializeField] private float _lifeStealCapPerSecond = 0.02f; // 1초 구간마다 회복할 수 있는 최대 생명 비율

        // 표시용 종합 수치다. 판정에는 쓰지 않는다. 환산 공격과 환산 생존을 곱으로 묶어 한쪽만 올려 부풀리지 못하게 한다
        [Header("전투력")]
        [SerializeField] private float _combatPowerScale           = 10f;  // 최종 수치에 곱하는 배율, 성장 한 단계가 숫자로 보이게 한다
        [SerializeField] private float _combatPowerOffenseWeight   = 0.7f; // 0에서 1 사이, 환산 공격의 지수. 환산 생존은 1에서 뺀 값
        [SerializeField] private float _combatPowerLifeStealFactor = 20f;  // 흡수율 1당 환산 생존에 더하는 배수

        // 생활 제작이 아니라 전투 중 사용 규칙이라 여기 둔다. 보스전 합산 사용 횟수 상한은 SessionConfigSO가 가진다
        [Header("물약 사용")]
        [SerializeField] private float _potionSharedCooldown    = 10f;   // 초, 소형과 대형이 함께 쓰는 재사용 대기, 부활해도 유지
        [SerializeField] private float _potionUseDisplaySeconds = 0.35f; // 초, 사용 표시 동작, 이동 가능하고 무적 없음

        [Header("제어 면역")]
        [SerializeField] private float _controlImmunitySeconds = 1f; // 초, 기절과 속박이 모두 끝난 뒤 새 제어를 받지 않고 피해만 받는 시간

        [Header("입력 보관")]
        [SerializeField] private float _inputBufferSeconds   = 0.15f;
        [SerializeField] private int   _inputBufferDepth     = 1;     // 다음 행동 하나만 보관

        // EPlayerClass 값을 인덱스로 쓴다: 무사, 도술사, 신령사
        // TODO(민성): 타격 발생 시점(준비 시간)과 원거리 투사체 속도, 폭은 기획 결정 뒤에 추가
        [Header("직업별 기본 공격 (EPlayerClass 순서)")]
        [SerializeField] private BasicAttackSpec[] _basicAttacks =
        {
            new BasicAttackSpec(1.0f, 0.9f, 3f, 90f),
            new BasicAttackSpec(0.7f, 0.8f, 9f, 0f),
            new BasicAttackSpec(0.6f, 0.8f, 8f, 0f)
        };

        // 검사, 마검사, 궁수 순서(EPlayerClass 3, 4, 5). 기존 배열의 길이는 이미 저장된 자산이 가지므로 새 직업은 별도 배열로 둔다
        [Header("신규 직업별 기본 공격 (검사, 마검사, 궁수)")]
        [SerializeField] private BasicAttackSpec[] _classBasicAttacks =
        {
            new BasicAttackSpec(1.0f, 0.9f, 3f, 90f, 0.2f, 0f, 0f, false),
            new BasicAttackSpec(0.9f, 0.85f, 2.5f, 90f, 0f, 0f, 0f, false, 1.2f, new[]
            {
                // 단계마다 피해는 같고 모션과 베기 효과만 단계를 따른다
                new ComboStepSpec(0.9f, 0.85f, 0f, false),
                new ComboStepSpec(0.9f, 0.85f, 0f, false),
                new ComboStepSpec(0.9f, 0.85f, 0f, false)
            }),
            new BasicAttackSpec(0.9f, 0.95f, 10f, 0f, 0.35f, 24f, 0.3f, true, 1.2f, new[]
            {
                new ComboStepSpec(0.65f, 0.6f, 0.25f, false),
                new ComboStepSpec(0.75f, 0.75f, 0.22f, false, 3, 0.15f, 0, 2f),
                new ComboStepSpec(1.1f, 1.3f, 0.33f, true, heavy: true)
            })
        };

        public float MoveSpeed                => _moveSpeed;
        public float DodgeDistance            => _dodgeDistance;
        public float DodgeDuration            => _dodgeDuration;
        public float DodgeCooldown            => _dodgeCooldown;
        public float DodgeEnergyCost          => _dodgeEnergyCost;
        public float BaseMaxEnergy            => _baseMaxEnergy;
        public float EnergyRegenPerSecond     => _energyRegenPerSecond;
        public float BaseAttack               => _baseAttack;
        public float BaseHealth               => _baseHealth;
        public float BaseDefense              => _baseDefense;
        public float BaseCritChance           => _baseCritChance;
        public float CritDamageMultiplier     => _critDamageMultiplier;
        public float CritChanceCap            => _critChanceCap;
        public float DamageReductionCap       => _damageReductionCap;
        public float DefenseReductionConstant => _defenseReductionConstant;
        public float PlayerDefenseReductionConstant => _playerDefenseReductionConstant;
        public float LifeStealRateCap         => _lifeStealRateCap;
        public float LifeStealCapPerSecond    => _lifeStealCapPerSecond;
        public float CombatPowerScale           => _combatPowerScale;
        public float CombatPowerOffenseWeight   => _combatPowerOffenseWeight;
        public float CombatPowerLifeStealFactor => _combatPowerLifeStealFactor;
        public float PotionSharedCooldown     => _potionSharedCooldown;
        public float PotionUseDisplaySeconds  => _potionUseDisplaySeconds;
        public float ControlImmunitySeconds   => _controlImmunitySeconds;
        public float InputBufferSeconds       => _inputBufferSeconds;
        public int   InputBufferDepth         => _inputBufferDepth;

        /****************************************
        *                Methods
        ****************************************/

        public BasicAttackSpec GetBasicAttack(EPlayerClass playerClass)
        {
            int index = (int)playerClass;
            if ((index >= 0) && (index < _basicAttacks.Length) && (index < LEGACY_CLASS_COUNT))
            {
                return _basicAttacks[index];
            }

            int classIndex = index - LEGACY_CLASS_COUNT;
            if ((classIndex < 0) || (_classBasicAttacks == null) || (classIndex >= _classBasicAttacks.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(playerClass), "기본 공격 명세가 없는 직업이다");
            }

            return _classBasicAttacks[classIndex];
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(PlayerConfigSO);

            DataValidation.RequirePositive(errors, OWNER, nameof(_moveSpeed), _moveSpeed);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_dodgeDistance), _dodgeDistance);
            DataValidation.RequirePositive(errors, OWNER, nameof(_dodgeDuration), _dodgeDuration);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_dodgeCooldown), _dodgeCooldown);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_dodgeEnergyCost), _dodgeEnergyCost);
            DataValidation.RequirePositive(errors, OWNER, nameof(_baseMaxEnergy), _baseMaxEnergy);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_energyRegenPerSecond), _energyRegenPerSecond);
            DataValidation.RequirePositive(errors, OWNER, nameof(_baseAttack), _baseAttack);
            DataValidation.RequirePositive(errors, OWNER, nameof(_baseHealth), _baseHealth);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_baseDefense), _baseDefense);
            DataValidation.RequireRange(errors, OWNER, nameof(_critDamageMultiplier), _critDamageMultiplier, MIN_CRIT_MULTIPLIER, float.MaxValue);
            DataValidation.RequirePositiveAtMost(errors, OWNER, nameof(_critChanceCap), _critChanceCap, MAX_RATE);
            DataValidation.RequireRange(errors, OWNER, nameof(_baseCritChance), _baseCritChance, 0f, Math.Min(_critChanceCap, MAX_RATE));
            DataValidation.RequireRange(errors, OWNER, nameof(_damageReductionCap), _damageReductionCap, 0f, MAX_RATE);
            DataValidation.RequirePositive(errors, OWNER, nameof(_defenseReductionConstant), _defenseReductionConstant);
            DataValidation.RequirePositive(errors, OWNER, nameof(_playerDefenseReductionConstant), _playerDefenseReductionConstant);
            DataValidation.RequireRange(errors, OWNER, nameof(_lifeStealRateCap), _lifeStealRateCap, 0f, MAX_RATE);
            DataValidation.RequireRange(errors, OWNER, nameof(_lifeStealCapPerSecond), _lifeStealCapPerSecond, 0f, MAX_RATE);
            DataValidation.RequirePositive(errors, OWNER, nameof(_combatPowerScale), _combatPowerScale);
            DataValidation.RequireRange(errors, OWNER, nameof(_combatPowerOffenseWeight), _combatPowerOffenseWeight, 0f, MAX_RATE);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_combatPowerLifeStealFactor), _combatPowerLifeStealFactor);
            DataValidation.RequirePositive(errors, OWNER, nameof(_potionSharedCooldown), _potionSharedCooldown);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_potionUseDisplaySeconds), _potionUseDisplaySeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_controlImmunitySeconds), _controlImmunitySeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_inputBufferSeconds), _inputBufferSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_inputBufferDepth), _inputBufferDepth);

            int classCount = Enum.GetValues(typeof(EPlayerClass)).Length;
            int newCount   = classCount - LEGACY_CLASS_COUNT;
            if ((_basicAttacks == null) || (_basicAttacks.Length != LEGACY_CLASS_COUNT))
            {
                int count = (_basicAttacks == null) ? 0 : _basicAttacks.Length;
                errors.Add($"{OWNER}.{nameof(_basicAttacks)}: 기존 직업 기본 공격 명세 개수가 다르다 {count} != {LEGACY_CLASS_COUNT}");
                return;
            }

            if ((_classBasicAttacks == null) || (_classBasicAttacks.Length != newCount))
            {
                int count = (_classBasicAttacks == null) ? 0 : _classBasicAttacks.Length;
                errors.Add($"{OWNER}.{nameof(_classBasicAttacks)}: 신규 직업 기본 공격 명세 개수가 다르다 {count} != {newCount}");
                return;
            }

            for (int i = 0; i < classCount; ++i)
            {
                BasicAttackSpec spec  = (i < LEGACY_CLASS_COUNT) ? _basicAttacks[i] : _classBasicAttacks[i - LEGACY_CLASS_COUNT];
                string          field = $"{nameof(_basicAttacks)}[{(EPlayerClass)i}]";

                if (spec == null)
                {
                    errors.Add($"{OWNER}.{field}: 명세가 비어 있다");
                    continue;
                }

                DataValidation.RequirePositive(errors, OWNER, field + ".DamageCoefficient", spec.DamageCoefficient);
                DataValidation.RequirePositive(errors, OWNER, field + ".Interval", spec.Interval);
                DataValidation.RequirePositive(errors, OWNER, field + ".Range", spec.Range);
                DataValidation.RequireRange(errors, OWNER, field + ".ArcAngle", spec.ArcAngle, 0f, MAX_ARC_ANGLE);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".Windup", spec.Windup);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".ProjectileSpeed", spec.ProjectileSpeed);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".ProjectileWidth", spec.ProjectileWidth);
                if ((spec.ArcAngle <= 0f) && (spec.ProjectileSpeed > 0f))
                {
                    DataValidation.RequirePositive(errors, OWNER, field + ".ProjectileWidth", spec.ProjectileWidth);
                }

                if (!spec.HasCombo)
                {
                    continue;
                }

                DataValidation.RequirePositive(errors, OWNER, field + ".ComboResetSeconds", spec.ComboResetSeconds);
                for (int s = 0; s < spec.ComboStepCount; ++s)
                {
                    ComboStepSpec step      = spec.GetComboStep(s);
                    string        stepField = $"{field}.ComboSteps[{s}]";
                    if (step == null)
                    {
                        errors.Add($"{OWNER}.{stepField}: 단계 명세가 비어 있다");
                        continue;
                    }

                    DataValidation.RequirePositive(errors, OWNER, stepField + ".DamageCoefficient", step.DamageCoefficient);
                    DataValidation.RequirePositive(errors, OWNER, stepField + ".Interval", step.Interval);
                    DataValidation.RequireRange(errors, OWNER, stepField + ".Windup", step.Windup, 0f, step.Interval);
                    DataValidation.RequireRange(errors, OWNER, stepField + ".ShotCount", step.ShotCount, 1, MAX_BASIC_SHOTS);
                    DataValidation.RequireNonNegative(errors, OWNER, stepField + ".ShotInterval", step.ShotInterval);
                    DataValidation.RequireRange(errors, OWNER, stepField + ".MotionStep", step.MotionStep, 0, spec.ComboStepCount - 1);
                    DataValidation.RequireRange(errors, OWNER, stepField + ".MotionSpeedScale", step.MotionSpeedScale, 1f, float.MaxValue);
                    if (step.ShotCount > 1)
                    {
                        DataValidation.RequirePositive(errors, OWNER, stepField + ".ShotInterval", step.ShotInterval);
                        DataValidation.RequireRange(errors, OWNER, stepField + ".LastShotAt",
                            step.Windup + ((step.ShotCount - 1) * step.ShotInterval), 0f, step.Interval);
                        if ((spec.ArcAngle > 0f) || (spec.ProjectileSpeed <= 0f))
                        {
                            errors.Add($"{OWNER}.{stepField}: 연발은 투사체 기본 공격에만 지정한다");
                        }
                    }
                }
            }
        }
    }
}
