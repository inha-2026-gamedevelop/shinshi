// Project
using Shinshi.Contracts;

namespace Shinshi.UI
{
    /// <summary> 현재 레벨 안의 경험치 진행과 만렙 표시 값 </summary>
    public readonly struct HudExperienceProgress
    {
        /****************************************
        *              Properties
        ****************************************/

        public int Level { get; }
        public long Current { get; }
        public long Required { get; }
        public bool IsMaximum { get; }
        public float Ratio => IsMaximum ? 1f : (Required > 0 ? (float)((double)Current / Required) : 0f);

        public HudExperienceProgress(int level, long current, long required, bool maximum)
        {
            Level = level;
            Current = current;
            Required = required;
            IsMaximum = maximum;
        }
    }

    /// <summary> 확정 누적 경험치를 HUD 진행 값으로 변환하며 경험치를 지급하지 않는다 </summary>
    public static class HudExperienceRules
    {
        /****************************************
        *                Methods
        ****************************************/

        public static bool TryRead(CharacterLevelSettings settings, long total, out HudExperienceProgress progress)
        {
            progress = default;
            if ((settings == null) || (total < 0) || (settings.MaxLevel < 1)
                || (settings.MaxLevel > CharacterLevelSettings.MAX_SUPPORTED_LEVEL)
                || (settings.FirstLevelCost <= 0) || (settings.LevelCostStep < 0))
            {
                return false;
            }
            long steps = settings.MaxLevel - 1L;
            long pairs = steps * (steps - 1L) / 2L;
            if ((steps > 0) && (settings.FirstLevelCost > long.MaxValue / steps))
            {
                return false;
            }
            if ((pairs > 0) && (settings.LevelCostStep > (long.MaxValue - settings.FirstLevelCost * steps) / pairs))
            {
                return false;
            }
            int level = CharacterLevelRules.Level(settings, total);
            bool maximum = level == settings.MaxLevel;
            long start = CharacterLevelRules.RequiredExperience(settings, level);
            progress = new HudExperienceProgress(level, maximum ? 0 : total - start,
                maximum ? 0 : CharacterLevelRules.RequiredExperience(settings, level + 1) - start, maximum);
            return true;
        }
    }
}
