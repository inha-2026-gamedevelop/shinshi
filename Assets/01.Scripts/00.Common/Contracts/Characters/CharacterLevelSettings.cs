// System
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 서버와 API가 함께 읽는 경험치 수치, 원본은 GameDB다 </summary>
    [DataContract]
    public sealed class CharacterLevelSettings
    {

        /****************************************
        *                Fields
        ****************************************/
        public const int MAX_SUPPORTED_LEVEL = 10000;

        [DataMember(Name = "maxLevel")] private int _maxLevel;
        [DataMember(Name = "firstLevelCost")] private long _firstLevelCost;
        [DataMember(Name = "levelCostStep")] private long _levelCostStep;
        [DataMember(Name = "trialStartLevel")] private int _trialStartLevel;
        [DataMember(Name = "attackPerLevel")] private int _attackPerLevel;
        [DataMember(Name = "healthPerLevel")] private int _healthPerLevel;
        [DataMember(Name = "regions")] private CharacterExperienceRegion[] _regions;

        /****************************************
        *              Properties
        ****************************************/

        public int MaxLevel { get => _maxLevel; set => _maxLevel = value; }
        public long FirstLevelCost { get => _firstLevelCost; set => _firstLevelCost = value; }
        public long LevelCostStep { get => _levelCostStep; set => _levelCostStep = value; }
        public int TrialStartLevel { get => _trialStartLevel; set => _trialStartLevel = value; }
        public int AttackPerLevel { get => _attackPerLevel; set => _attackPerLevel = value; }
        public int HealthPerLevel { get => _healthPerLevel; set => _healthPerLevel = value; }
        public CharacterExperienceRegion[] Regions { get => _regions; set => _regions = value; }

        /****************************************
        *              Constructors
        ****************************************/

        private CharacterLevelSettings()
        {
        }

        public CharacterLevelSettings(int maxLevel)
        {
            _maxLevel = maxLevel;
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool IsValid()
        {
            if ((MaxLevel < 1) || (MaxLevel > MAX_SUPPORTED_LEVEL) || (FirstLevelCost <= 0) || (LevelCostStep < 0)
                || (TrialStartLevel < 1) || (TrialStartLevel > MaxLevel) || (AttackPerLevel < 0)
                || (HealthPerLevel < 0) || (Regions == null) || (Regions.Length == 0))
            {
                return false;
            }

            try
            {
                CharacterLevelRules.RequiredExperience(this, MaxLevel);
            }
            catch (OverflowException)
            {
                return false;
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Regions.Length; ++i)
            {
                CharacterExperienceRegion region = Regions[i];
                if ((region == null) || string.IsNullOrWhiteSpace(region.RegionId) || !ids.Add(region.RegionId)
                    || (region.NormalEnemy <= 0) || (region.EliteEnemy <= 0) || (region.WaveCleared <= 0))
                {
                    return false;
                }
            }

            return true;
        }

        public long GetGrant(string regionId, int sourceKind)
        {
            for (int i = 0; i < Regions.Length; ++i)
            {
                CharacterExperienceRegion region = Regions[i];
                if (!string.Equals(region.RegionId, regionId, StringComparison.Ordinal))
                {
                    continue;
                }

                switch (sourceKind)
                {
                    case CharacterExperienceSource.NORMAL_ENEMY: return region.NormalEnemy;
                    case CharacterExperienceSource.ELITE_ENEMY: return region.EliteEnemy;
                    case CharacterExperienceSource.WAVE_CLEARED: return region.WaveCleared;
                    default: return 0;
                }
            }

            return 0;
        }
    }

    [DataContract]
    public sealed class CharacterExperienceRegion
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "regionId")] private string _regionId;
        [DataMember(Name = "normalEnemy")] private long _normalEnemy;
        [DataMember(Name = "eliteEnemy")] private long _eliteEnemy;
        [DataMember(Name = "waveCleared")] private long _waveCleared;

        /****************************************
        *              Properties
        ****************************************/

        public string RegionId { get => _regionId; set => _regionId = value; }
        public long NormalEnemy { get => _normalEnemy; set => _normalEnemy = value; }
        public long EliteEnemy { get => _eliteEnemy; set => _eliteEnemy = value; }
        public long WaveCleared { get => _waveCleared; set => _waveCleared = value; }

        /****************************************
        *              Constructors
        ****************************************/

        private CharacterExperienceRegion()
        {
        }

        public CharacterExperienceRegion(string regionId)
        {
            _regionId = regionId;
        }

    }

    public static class CharacterExperienceSource
    {
        public const int NORMAL_ENEMY = 1;
        public const int ELITE_ENEMY = 2;
        public const int WAVE_CLEARED = 3;
    }
}
