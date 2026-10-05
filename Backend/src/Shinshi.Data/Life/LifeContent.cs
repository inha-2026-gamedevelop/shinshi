// System
using System;
using System.Collections.Generic;

namespace Shinshi.Data.Life
{
    /// <summary> 생활 규칙 수치. 원본은 Unity LifeConfigSO 에셋이며 값이 바뀌면 여기도 같이 바꾼다 </summary>
    public static class LifeContent
    {
        // TODO(민성): 성장 콘텐츠 JSON처럼 내보내기 파일로 옮기고 EditMode 테스트로 Assets/09.Data/Life/LifeConfig.asset 값과
        // 대조한다. 지금은 에셋 값을 그대로 옮긴 상수다

        /****************************************
        *                Fields
        ****************************************/

        // 계정 공동 보관 아이템은 거래 불가 계정 귀속 하나만 쓴다(출정 지참 물약과 같은 귀속)
        public const string BINDING_KIND = Sessions.SettlementSanityLimits.ACCOUNT_BINDING_KIND;

        // 약초. LifeConfig _herbs, _gatherHerbId, _herbsPerGather
        public const string HERB_ID          = "item_herb";
        public const long   HERB_STACK       = 9999;
        public const long   HERBS_PER_GATHER = 2;

        // 물약 완제품. LifeConfig _potions(소형 약초 5, 대형 약초 12, 각 1개 산출, 상한 999)
        public const string POTION_SMALL_ID   = "item_potion_small";
        public const string POTION_LARGE_ID   = "item_potion_large";
        public const long   POTION_SMALL_COST = 5;
        public const long   POTION_LARGE_COST = 12;
        public const long   POTION_OUTPUT     = 1;
        public const long   POTION_STACK      = 999;

        // 행동력. LifeConfig _maxActionEnergy, _actionCostPerGather, _actionRecoverInterval(초), _actionRecoverAmount
        public const long ACTION_MAX                      = 100;
        public const long ACTION_COST_PER_GATHER          = 2;
        public const int  ACTION_RECOVER_INTERVAL_SECONDS = 300;
        public const long ACTION_RECOVER_AMOUNT           = 1;

        private static readonly string[] G_PotionIds = { POTION_SMALL_ID, POTION_LARGE_ID };

        /****************************************
        *              Properties
        ****************************************/

        public static TimeSpan ActionRecoverInterval => TimeSpan.FromSeconds(ACTION_RECOVER_INTERVAL_SECONDS);

        // EPotionSize 순서(소형, 대형)
        public static IReadOnlyList<string> PotionIds => G_PotionIds;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 물약 정의의 제작 비용(약초 수). 모르는 정의면 false다 </summary>
        public static bool TryGetPotionCost(string? potionDefinitionId, out long herbCost)
        {
            switch (potionDefinitionId)
            {
                case POTION_SMALL_ID:
                    herbCost = POTION_SMALL_COST;
                    return true;
                case POTION_LARGE_ID:
                    herbCost = POTION_LARGE_COST;
                    return true;
                default:
                    herbCost = 0;
                    return false;
            }
        }
    }
}
