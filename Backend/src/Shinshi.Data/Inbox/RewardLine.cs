// System
using System;

// Third Party
using Newtonsoft.Json;

// Project
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Inbox
{
    /// <summary> admin_grants.rewards와 reward_inbox.rewards JSON 배열의 한 줄. 두 표가 같은 형식을 쓴다.
    /// 예: {"assetKind":"AccountItem","assetId":"item_herb","bindingKind":"Account","quantity":3} </summary>
    public sealed class RewardLine
    {
        /****************************************
        *              Properties
        ****************************************/

        // 자산 종류 문자열은 EconomyAsset 상수(원장 asset_kind)와 같다
        [JsonProperty("assetKind")]   public string AssetKind   { get; set; } = string.Empty;
        [JsonProperty("assetId")]     public string AssetId     { get; set; } = string.Empty;
        [JsonProperty("bindingKind")] public string BindingKind { get; set; } = string.Empty;
        [JsonProperty("quantity")]    public long   Quantity    { get; set; }

        [JsonIgnore]
        public bool IsCharacterAsset => (AssetKind == EconomyAsset.ESSENCE) || (AssetKind == EconomyAsset.SHARDS);

        // 같은 지급 안에서 겹치면 안 되는 키. 원장 entry_key에도 쓴다
        [JsonIgnore]
        public string Key => AssetKind + ":" + AssetId + ":" + BindingKind;

        /****************************************
        *                Methods
        ****************************************/

        public RewardLine()
        {
        }

        public RewardLine(string assetKind, string? assetId, string? bindingKind, long quantity)
        {
            ArgumentNullException.ThrowIfNull(assetKind);
            AssetKind   = assetKind;
            AssetId     = assetId ?? string.Empty;
            BindingKind = bindingKind ?? string.Empty;
            Quantity    = quantity;
        }
    }
}
