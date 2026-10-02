// System
using System;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Admin.Grants
{
    /// <summary> 검증을 마친 개인 재화 조정 한 건. TryCreate만 만들 수 있다 </summary>
    public sealed class AdminAdjustment
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid   AccountId   { get; }
        public string AssetKind   { get; }
        public string AssetId     { get; }
        public string BindingKind { get; }
        public Guid?  CharacterId { get; }
        public long   Delta       { get; }

        /****************************************
        *                Methods
        ****************************************/

        private AdminAdjustment(Guid accountId, string assetKind, string assetId, string bindingKind, Guid? characterId,
            long delta)
        {
            AccountId   = accountId;
            AssetKind   = assetKind;
            AssetId     = assetId;
            BindingKind = bindingKind;
            CharacterId = characterId;
            Delta       = delta;
        }

        /// <summary> 자산 규칙은 지급과 같고, delta는 0이 아니며 절대값이 상한 이하다. 캐릭터는 Essence, Shards에만 있어야 한다 </summary>
        public static AdminAdjustment? TryCreate(Guid accountId, string? assetKind, string? assetId, string? bindingKind,
            Guid? characterId, long delta)
        {
            if ((accountId == Guid.Empty) || (characterId == Guid.Empty)
                || !RewardLines.IsValidAsset(assetKind, assetId, bindingKind)
                || (delta == 0) || (delta < -AdminGrantCodes.MAX_ADJUSTMENT) || (delta > AdminGrantCodes.MAX_ADJUSTMENT))
            {
                return null;
            }

            bool characterAsset = (assetKind == EconomyAsset.ESSENCE) || (assetKind == EconomyAsset.SHARDS);
            if (characterAsset != (characterId != null))
            {
                return null;
            }

            return new AdminAdjustment(accountId, assetKind!, assetId ?? string.Empty, bindingKind ?? string.Empty,
                characterId, delta);
        }
    }
}
