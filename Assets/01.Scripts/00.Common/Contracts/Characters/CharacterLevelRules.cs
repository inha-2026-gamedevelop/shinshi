// System
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Shinshi.Contracts
{
    /// <summary> 경험치 상한과 레벨 계산, 중간 곱셈도 오버플로를 허용하지 않는다 </summary>
    public static class CharacterLevelRules
    {

        /****************************************
        *                Methods
        ****************************************/
        public static string SettingsHash(CharacterLevelSettings settings)
        {
            StringBuilder text = new StringBuilder();
            text.AppendFormat(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}", settings.MaxLevel,
                settings.FirstLevelCost, settings.LevelCostStep, settings.TrialStartLevel,
                settings.AttackPerLevel, settings.HealthPerLevel);
            for (int i = 0; i < settings.Regions.Length; ++i)
            {
                CharacterExperienceRegion region = settings.Regions[i];
                text.AppendFormat(CultureInfo.InvariantCulture, "|{0}:{1}:{2}:{3}", region.RegionId,
                    region.NormalEnemy, region.EliteEnemy, region.WaveCleared);
            }
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                StringBuilder result = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; ++i)
                {
                    result.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return result.ToString();
            }
        }

        public static long RequiredExperience(CharacterLevelSettings settings, int level)
        {
            if ((settings == null) || (level < 1) || (level > settings.MaxLevel))
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            long steps = level - 1L;
            return checked((settings.FirstLevelCost * steps) + (settings.LevelCostStep * (steps * (steps - 1) / 2)));
        }

        public static int Level(CharacterLevelSettings settings, long totalExperience)
        {
            if ((settings == null) || (totalExperience < 0))
            {
                throw new ArgumentOutOfRangeException(nameof(totalExperience));
            }

            int low = 1;
            int high = settings.MaxLevel;
            while (low < high)
            {
                int middle = low + ((high - low + 1) / 2);
                if (RequiredExperience(settings, middle) <= totalExperience)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }

        public static long Add(CharacterLevelSettings settings, long current, long amount)
        {
            if ((current < 0) || (amount < 0))
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            long cap = RequiredExperience(settings, settings.MaxLevel);
            return current >= cap ? cap : current + Math.Min(amount, cap - current);
        }
    }
}
