// System
using System;
using System.Collections.Generic;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 운영자가 끌 수 있는 기능 키. service_state.disabled_features와 공개 서비스 상태의 disabledFeatures에 이 문자열이 그대로 실린다.
    /// 목록 밖의 키는 어드민 저장에서 거절하므로 새 기능을 막으려면 여기에 먼저 추가한다 </summary>
    public static class ServiceFeatureKeys
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string AUCTION       = "auction";
        public const string CRAFTING      = "crafting";
        public const string ENHANCE       = "enhance";
        public const string INHERIT       = "inherit";
        public const string GATHERING     = "gathering";
        public const string BOSS_ENTRY    = "boss_entry";
        public const string INBOX_CLAIM   = "inbox_claim";
        public const string DUNGEON_ENTRY = "dungeon_entry";

        private static readonly string[] G_All =
        {
            AUCTION, CRAFTING, ENHANCE, INHERIT, GATHERING, BOSS_ENTRY, INBOX_CLAIM, DUNGEON_ENTRY
        };

        /****************************************
        *              Properties
        ****************************************/

        public static IReadOnlyList<string> All => G_All;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 대소문자까지 같은 허용 키인지 본다. 저장 값과 비교가 순서 비교라 정규화하지 않는다 </summary>
        public static bool IsKnown(string featureKey)
        {
            return (featureKey != null) && (Array.IndexOf(G_All, featureKey) >= 0);
        }
    }
}
