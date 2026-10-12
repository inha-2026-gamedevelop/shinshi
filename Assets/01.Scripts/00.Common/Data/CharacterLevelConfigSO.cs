// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Data
{
    [CreateAssetMenu(fileName = "CharacterLevelConfig", menuName = "Shinshi/CharacterLevelConfigSO")]
    public sealed class CharacterLevelConfigSO : GameDataSO
    {

        /****************************************
        *                Fields
        ****************************************/
        [SerializeField] private int _maxLevel = 30;
        [SerializeField] private long _firstLevelCost = 300;
        [SerializeField] private long _levelCostStep = 275;
        [SerializeField] private int _trialStartLevel = 5;
        [SerializeField] private int _attackPerLevel = 1;
        [SerializeField] private int _healthPerLevel = 15;
        [SerializeField] private RegionExperience[] _regions =
        {
            new RegionExperience("region_forest", 5, 25, 30),
            new RegionExperience("region_ruins", 5, 30, 40),
            new RegionExperience("region_altar", 5, 35, 50)
        };

        public CharacterLevelSettings ToSettings()
        {
            CharacterExperienceRegion[] regions = new CharacterExperienceRegion[_regions == null ? 0 : _regions.Length];
            for (int i = 0; i < regions.Length; ++i)
            {
                RegionExperience source = _regions[i];
                if (source != null)
                {
                    regions[i] = new CharacterExperienceRegion(source.RegionId)
                    {
                        RegionId = source.RegionId, NormalEnemy = source.NormalEnemy,
                        EliteEnemy = source.EliteEnemy, WaveCleared = source.WaveCleared
                    };
                }
            }

            return new CharacterLevelSettings(_maxLevel)
            {
                MaxLevel = _maxLevel, FirstLevelCost = _firstLevelCost, LevelCostStep = _levelCostStep,
                TrialStartLevel = _trialStartLevel, AttackPerLevel = _attackPerLevel,
                HealthPerLevel = _healthPerLevel, Regions = regions
            };
        }

        protected override void ValidateValues(List<string> errors)
        {
            if (!ToSettings().IsValid())
            {
                errors.Add("CharacterLevelConfigSO: 레벨 수치나 지역 경험치가 유효하지 않다");
            }
        }

        public override void ValidateReferences(GameDatabaseSO root, List<string> errors)
        {
            for (int i = 0; i < _regions.Length; ++i)
            {
                bool found = false;
                for (int j = 0; j < root.Progression.Regions.Count; ++j)
                {
                    if (root.Progression.Regions[j].DefinitionId == _regions[i].RegionId)
                    {
                        found = root.Progression.Regions[j].HasWaveBattle;
                        break;
                    }
                }
                if (!found)
                {
                    errors.Add("CharacterLevelConfigSO: 일반전 지역이 없다, " + _regions[i].RegionId);
                }
            }
        }

        [System.Serializable]
        private sealed class RegionExperience
        {
            [SerializeField] private string _regionId;
            [SerializeField] private long _normalEnemy;
            [SerializeField] private long _eliteEnemy;
            [SerializeField] private long _waveCleared;

            public string RegionId => _regionId;
            public long NormalEnemy => _normalEnemy;
            public long EliteEnemy => _eliteEnemy;
            public long WaveCleared => _waveCleared;

            public RegionExperience(string regionId, long normalEnemy, long eliteEnemy, long waveCleared)
            {
                _regionId = regionId;
                _normalEnemy = normalEnemy;
                _eliteEnemy = eliteEnemy;
                _waveCleared = waveCleared;
            }
        }
    }
}
