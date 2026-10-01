// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 수련 5항목 비용과 상한, 부적 등록과 강화 보정 </summary>
    [CreateAssetMenu(fileName = "GrowthConfig", menuName = "Shinshi/GrowthConfigSO")]
    public class GrowthConfigSO : GameDataSO
    {
        /// <summary> 수련 항목 하나의 단계당 증가량 </summary>
        [Serializable]
        public class TrainingStatSpec
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private ETrainingStat _stat;

            // 공격, 생명, 방어는 수련 전 값에 곱할 비율, 치명은 확률에 더하는 값, 기력은 최대 기력에 더하는 값
            [SerializeField] private float _gainPerStep;

            public ETrainingStat Stat        => _stat;
            public float         GainPerStep => _gainPerStep;

            /****************************************
            *                Methods
            ****************************************/

            public TrainingStatSpec(ETrainingStat stat, float gainPerStep)
            {
                _stat        = stat;
                _gainPerStep = gainPerStep;
            }
        }

        /// <summary> 등록한 부적 효과를 한 레벨 올리는 확정 강화 비용 </summary>
        [Serializable]
        public class EffectUpgradeCost
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private int _cardCost;    // 같은 효과 ID의 중복 부적 장수
            [SerializeField] private int _essenceCost; // 정기

            public int CardCost    => _cardCost;
            public int EssenceCost => _essenceCost;

            /****************************************
            *                Methods
            ****************************************/

            public EffectUpgradeCost(int cardCost, int essenceCost)
            {
                _cardCost    = cardCost;
                _essenceCost = essenceCost;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const int   TRAINING_STAT_COUNT = 5;
        private const int   MAX_EFFECT_LEVEL    = 5;
        private const int   FIRST_UPGRADE_LEVEL = 2;  // 등록 성공이 레벨 1이므로 강화 표는 레벨 2부터
        private const float MAX_RATE            = 1f;

        // 확정된 수련 비용 규칙으로 총 상한까지 투자한 누적값이다. 비용이나 총 상한을 바꾸면 이 값도 함께 바꿔야 한다
        private const long EXPECTED_TRAINING_TOTAL_COST = 59500;

        /****************************************
        *                Fields
        ****************************************/

        [Header("정기 수련")]
        [SerializeField] private int _trainingTotalCap = 100; // 캐릭터당 다섯 항목 투자 단계 합
        [SerializeField] private int _trainingStatCap  = 40;  // 항목당 투자 단계
        [SerializeField] private int _trainingBaseCost = 100; // 정기, 첫 번째 단계
        [SerializeField] private int _trainingCostStep = 10;  // 정기, 단계마다 더한다. 항목별로 다시 시작하지 않고 총 투자 순번을 따른다

        // ETrainingStat 값을 인덱스로 쓴다
        [Header("수련 항목별 증가량 (ETrainingStat 순서)")]
        [SerializeField] private TrainingStatSpec[] _trainingStats =
        {
            new TrainingStatSpec(ETrainingStat.Attack, 0.005f),
            new TrainingStatSpec(ETrainingStat.Health, 0.01f),
            new TrainingStatSpec(ETrainingStat.Defense, 0.02f),
            new TrainingStatSpec(ETrainingStat.CritChance, 0.005f),
            new TrainingStatSpec(ETrainingStat.MaxEnergy, 1f)
        };

        // 총 투자를 유지한 채 배분만 바꾼다. 투자 0이거나 배분 변화가 없으면 비용을 받지 않는다
        [Header("수련 재분배")]
        [SerializeField] private int _respecBaseCost    = 200; // 정기
        [SerializeField] private int _respecCostPerStep = 20;  // 정기, 현재 총 투자 단계마다

        // 실패 보정은 같은 캐릭터와 같은 효과의 연속 시도에만 쌓이고 성공하면 끝난다
        [Header("부적 등록")]
        [SerializeField] private int     _registerCardCost     = 1;   // 같은 효과 ID 부적, 시도마다
        [SerializeField] private int     _registerEssenceCost  = 150; // 정기, 시도마다
        [SerializeField] private float[] _registerSuccessRates = { 0.5f, 0.65f, 0.8f, 1f }; // 연속 시도 순서, 마지막은 확정 성공

        // 성공률 100% 확정 강화다
        [Header("부적 효과 강화 (목표 레벨 2부터 5 순서)")]
        [SerializeField] private EffectUpgradeCost[] _effectUpgradeCosts =
        {
            new EffectUpgradeCost(1, 300),
            new EffectUpgradeCost(1, 600),
            new EffectUpgradeCost(2, 1000),
            new EffectUpgradeCost(3, 1600)
        };

        [Header("부적 분해")]
        [SerializeField] private int _cardDisassembleEssence = 50; // 정기, 분해한 캐릭터에게 확정 지급

        // 부적은 효과 ID마다 따로 쌓이고 아이템 카탈로그에 항목이 없어 상한을 여기 둔다. 등록 진도는 캐릭터별이다
        [Header("부적 보관")]
        [SerializeField] private int _unregisteredCardStackLimit = 999; // 장, 계정 공동, 미등록 부적의 효과 ID별 합계 상한

        public int                              TrainingTotalCap           => _trainingTotalCap;
        public int                              TrainingStatCap            => _trainingStatCap;
        public int                              TrainingBaseCost           => _trainingBaseCost;
        public int                              TrainingCostStep           => _trainingCostStep;
        public int                              RespecBaseCost             => _respecBaseCost;
        public int                              RespecCostPerStep          => _respecCostPerStep;
        public int                              RegisterCardCost           => _registerCardCost;
        public int                              RegisterEssenceCost        => _registerEssenceCost;
        public IReadOnlyList<float>             RegisterSuccessRates       => _registerSuccessRates;
        public IReadOnlyList<EffectUpgradeCost> EffectUpgradeCosts         => _effectUpgradeCosts;
        public int                              MaxEffectLevel             => MAX_EFFECT_LEVEL;
        public int                              CardDisassembleEssence     => _cardDisassembleEssence;
        public int                              UnregisteredCardStackLimit => _unregisteredCardStackLimit;

        /****************************************
        *                Methods
        ****************************************/

        public TrainingStatSpec GetTrainingStat(ETrainingStat stat)
        {
            int index = (int)stat;
            if ((index < 0) || (index >= _trainingStats.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(stat), "증가량이 없는 수련 항목이다");
            }

            return _trainingStats[index];
        }

        /// <summary> 캐릭터의 총 투자 순번 stepNumber번째 수련 비용, 1부터 TrainingTotalCap까지 </summary>
        public int GetTrainingStepCost(int stepNumber)
        {
            if ((stepNumber < 1) || (stepNumber > _trainingTotalCap))
            {
                throw new ArgumentOutOfRangeException(nameof(stepNumber), "수련 순번이 범위를 벗어났다");
            }

            return _trainingBaseCost + (_trainingCostStep * (stepNumber - 1));
        }

        /// <summary> 현재 총 투자 단계로 재분배할 때의 비용, 실제 배분 변화가 없으면 호출 쪽이 0으로 처리한다 </summary>
        public int GetRespecCost(int totalInvested)
        {
            if ((totalInvested < 0) || (totalInvested > _trainingTotalCap))
            {
                throw new ArgumentOutOfRangeException(nameof(totalInvested), "총 투자 단계가 범위를 벗어났다");
            }

            if (totalInvested == 0)
            {
                return 0;
            }

            return _respecBaseCost + (_respecCostPerStep * totalInvested);
        }

        /// <summary> 같은 캐릭터와 효과에 쌓인 등록 실패 횟수로 본 이번 시도 성공률 </summary>
        public float GetRegisterSuccessRate(int failCount)
        {
            if (failCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failCount), "실패 횟수는 0 이상이어야 한다");
            }

            int index = Mathf.Min(failCount, _registerSuccessRates.Length - 1);
            return _registerSuccessRates[index];
        }

        /// <summary> 목표 효과 레벨(2부터 MaxEffectLevel까지)로 올리는 확정 강화 비용 </summary>
        public EffectUpgradeCost GetEffectUpgradeCost(int targetLevel)
        {
            if ((targetLevel < FIRST_UPGRADE_LEVEL) || (targetLevel > MAX_EFFECT_LEVEL))
            {
                throw new ArgumentOutOfRangeException(nameof(targetLevel), "부적 효과 목표 레벨이 범위를 벗어났다");
            }

            return _effectUpgradeCosts[targetLevel - FIRST_UPGRADE_LEVEL];
        }

        protected override void ValidateValues(List<string> errors)
        {
            ValidateTraining(errors);
            ValidateTrainingStats(errors);
            ValidateRegister(errors);
            ValidateEffectUpgrade(errors);
        }

        // 강화 비용 표와 부적 레벨 배율 표의 최고 레벨이 다르면 비용만 있거나 배율만 있는 레벨이 생긴다
        public override void ValidateReferences(GameDatabaseSO root, List<string> errors)
        {
            int talismanMaxLevel = root.Talismans.MaxEffectLevel;
            if (talismanMaxLevel != MAX_EFFECT_LEVEL)
            {
                errors.Add($"{nameof(GrowthConfigSO)}: 부적 최고 효과 레벨 {talismanMaxLevel} != 강화 표 최고 레벨 {MAX_EFFECT_LEVEL}");
            }
        }

        private void ValidateTraining(List<string> errors)
        {
            const string OWNER = nameof(GrowthConfigSO);

            DataValidation.RequirePositive(errors, OWNER, nameof(_trainingTotalCap), _trainingTotalCap);
            DataValidation.RequirePositive(errors, OWNER, nameof(_trainingStatCap), _trainingStatCap);
            DataValidation.RequirePositive(errors, OWNER, nameof(_trainingBaseCost), _trainingBaseCost);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_trainingCostStep), _trainingCostStep);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_respecBaseCost), _respecBaseCost);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_respecCostPerStep), _respecCostPerStep);

            if (_trainingStatCap > _trainingTotalCap)
            {
                errors.Add($"{OWNER}.{nameof(_trainingStatCap)}: 항목 상한이 총 상한 {_trainingTotalCap}보다 크다");
            }

            if ((_trainingStatCap * TRAINING_STAT_COUNT) < _trainingTotalCap)
            {
                errors.Add($"{OWNER}.{nameof(_trainingStatCap)}: 다섯 항목을 모두 채워도 총 상한 {_trainingTotalCap}에 닿지 않는다");
            }

            if (_trainingTotalCap <= 0)
            {
                return;
            }

            long total = 0;
            for (int step = 1; step <= _trainingTotalCap; ++step)
            {
                total += GetTrainingStepCost(step);
            }

            if (total != EXPECTED_TRAINING_TOTAL_COST)
            {
                errors.Add($"{OWNER}: 수련 {_trainingTotalCap}단계 누적 비용 {total} != {EXPECTED_TRAINING_TOTAL_COST}");
            }
        }

        private void ValidateTrainingStats(List<string> errors)
        {
            const string OWNER = nameof(GrowthConfigSO);

            DataValidation.RequireCount(errors, OWNER, nameof(_trainingStats), _trainingStats, TRAINING_STAT_COUNT);
            if ((_trainingStats == null) || (_trainingStats.Length != TRAINING_STAT_COUNT))
            {
                return;
            }

            for (int i = 0; i < _trainingStats.Length; ++i)
            {
                TrainingStatSpec spec  = _trainingStats[i];
                string           field = $"{nameof(_trainingStats)}[{(ETrainingStat)i}]";

                if (spec == null)
                {
                    errors.Add($"{OWNER}.{field}: 항목이 비어 있다");
                    continue;
                }

                if ((int)spec.Stat != i)
                {
                    errors.Add($"{OWNER}.{field}: 항목 순서가 ETrainingStat과 다르다 {spec.Stat}");
                }

                DataValidation.RequirePositive(errors, OWNER, field + ".GainPerStep", spec.GainPerStep);
            }
        }

        private void ValidateRegister(List<string> errors)
        {
            const string OWNER = nameof(GrowthConfigSO);

            DataValidation.RequirePositive(errors, OWNER, nameof(_registerCardCost), _registerCardCost);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_registerEssenceCost), _registerEssenceCost);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_cardDisassembleEssence), _cardDisassembleEssence);
            DataValidation.RequirePositive(errors, OWNER, nameof(_unregisteredCardStackLimit), _unregisteredCardStackLimit);

            if ((_registerSuccessRates == null) || (_registerSuccessRates.Length == 0))
            {
                errors.Add($"{OWNER}.{nameof(_registerSuccessRates)}: 등록 성공률이 없다");
                return;
            }

            for (int i = 0; i < _registerSuccessRates.Length; ++i)
            {
                string field = $"{nameof(_registerSuccessRates)}[{i}]";
                DataValidation.RequirePositiveAtMost(errors, OWNER, field, _registerSuccessRates[i], MAX_RATE);

                if ((i > 0) && (_registerSuccessRates[i] < _registerSuccessRates[i - 1]))
                {
                    errors.Add($"{OWNER}.{field}: 실패 뒤 성공률이 앞 시도보다 낮다");
                }
            }

            // 마지막 칸이 확정이어야 보정이 끝없이 이어지지 않는다
            if (_registerSuccessRates[_registerSuccessRates.Length - 1] < MAX_RATE)
            {
                errors.Add($"{OWNER}.{nameof(_registerSuccessRates)}: 마지막 시도 성공률이 1이어야 한다");
            }
        }

        private void ValidateEffectUpgrade(List<string> errors)
        {
            const string OWNER = nameof(GrowthConfigSO);

            int expected = (MAX_EFFECT_LEVEL - FIRST_UPGRADE_LEVEL) + 1;
            DataValidation.RequireCount(errors, OWNER, nameof(_effectUpgradeCosts), _effectUpgradeCosts, expected);
            if (_effectUpgradeCosts == null)
            {
                return;
            }

            for (int i = 0; i < _effectUpgradeCosts.Length; ++i)
            {
                EffectUpgradeCost cost  = _effectUpgradeCosts[i];
                string            field = $"{nameof(_effectUpgradeCosts)}[Lv{i + FIRST_UPGRADE_LEVEL}]";

                if (cost == null)
                {
                    errors.Add($"{OWNER}.{field}: 비용이 비어 있다");
                    continue;
                }

                DataValidation.RequirePositive(errors, OWNER, field + ".CardCost", cost.CardCost);
                DataValidation.RequireNonNegative(errors, OWNER, field + ".EssenceCost", cost.EssenceCost);
            }
        }
    }
}
