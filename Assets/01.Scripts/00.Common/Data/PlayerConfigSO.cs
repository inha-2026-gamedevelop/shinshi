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

            public float DamageCoefficient => _damageCoefficient;
            public float Interval          => _interval;
            public float Range             => _range;
            public float ArcAngle          => _arcAngle;

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
        }

        /****************************************
        *              Constants
        ****************************************/

        private const float MAX_RATE            = 1f;
        private const float MIN_CRIT_MULTIPLIER = 1f;
        private const float MAX_ARC_ANGLE       = 360f;

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
        [SerializeField] private float _defenseReductionConstant = 1000f; // 감소율 = 방어 / (방어 + 이 값), 물리와 도술에 같이 쓴다

        // 생활 제작이 아니라 전투 중 사용 규칙이라 여기 둔다. 보스전 합산 사용 횟수 상한은 SessionConfigSO가 가진다
        [Header("물약 사용")]
        [SerializeField] private float _potionSharedCooldown    = 10f;   // 초, 소형과 대형이 함께 쓰는 재사용 대기, 부활해도 유지
        [SerializeField] private float _potionUseDisplaySeconds = 0.35f; // 초, 사용 표시 동작, 이동 가능하고 무적 없음

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
        public float PotionSharedCooldown     => _potionSharedCooldown;
        public float PotionUseDisplaySeconds  => _potionUseDisplaySeconds;
        public float InputBufferSeconds       => _inputBufferSeconds;
        public int   InputBufferDepth         => _inputBufferDepth;

        /****************************************
        *                Methods
        ****************************************/

        public BasicAttackSpec GetBasicAttack(EPlayerClass playerClass)
        {
            int index = (int)playerClass;
            if ((index < 0) || (index >= _basicAttacks.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(playerClass), "기본 공격 명세가 없는 직업이다");
            }

            return _basicAttacks[index];
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
            DataValidation.RequirePositive(errors, OWNER, nameof(_potionSharedCooldown), _potionSharedCooldown);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_potionUseDisplaySeconds), _potionUseDisplaySeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_inputBufferSeconds), _inputBufferSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_inputBufferDepth), _inputBufferDepth);

            int classCount = Enum.GetValues(typeof(EPlayerClass)).Length;
            if ((_basicAttacks == null) || (_basicAttacks.Length != classCount))
            {
                int count = (_basicAttacks == null) ? 0 : _basicAttacks.Length;
                errors.Add($"{OWNER}.{nameof(_basicAttacks)}: 기본 공격 명세 개수가 직업 수와 다르다 {count} != {classCount}");
                return;
            }

            for (int i = 0; i < _basicAttacks.Length; ++i)
            {
                BasicAttackSpec spec  = _basicAttacks[i];
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
            }
        }
    }
}
