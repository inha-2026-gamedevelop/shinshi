namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> enum과 배열 크기에 묶이는 전투, 성장 개수 </summary>
        public static class Combat
        {
            /****************************************
            *                Fields
            ****************************************/

            // 직업마다 고정 일반 기술 4개, 기술마다 부적 선택 단계 3개(보조 2, 핵심 1)
            public const int SKILLS_PER_CLASS         = 4;
            public const int TALISMAN_TIERS_PER_SKILL = 3;

            // 신령사의 강림 종류
            public const int SUMMON_ENHANCEMENT_COUNT = 3;
        }
    }
}
