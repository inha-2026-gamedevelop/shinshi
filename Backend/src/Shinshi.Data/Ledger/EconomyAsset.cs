// System
using System;

namespace Shinshi.Data.Ledger
{
    /// <summary> 계정과 캐릭터, 아이템 귀속까지 포함한 대사 단위. 문자열은 내부 DB 식별자다 </summary>
    public sealed class EconomyAsset
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CURRENCY     = "Currency";
        public const string TICKETS      = "Tickets";
        public const string ESSENCE      = "Essence";
        public const string SHARDS       = "Shards";
        public const string ACCOUNT_ITEM = "AccountItem";

        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId { get; }
        public Guid? CharacterId { get; }
        public string Kind { get; }
        public string AssetId { get; }
        public string BindingKind { get; }

        /****************************************
        *                Methods
        ****************************************/

        private EconomyAsset(Guid accountId, Guid? characterId, string kind, string assetId, string bindingKind)
        {
            if (accountId == Guid.Empty)
            {
                throw new ArgumentException("Account ID is required.", nameof(accountId));
            }
            if (characterId == Guid.Empty)
            {
                throw new ArgumentException("Character ID is required.", nameof(characterId));
            }

            AccountId   = accountId;
            CharacterId = characterId;
            Kind        = kind;
            AssetId     = assetId;
            BindingKind = bindingKind;
        }

        public static EconomyAsset Currency(Guid accountId)
        {
            return new EconomyAsset(accountId, null, CURRENCY, string.Empty, string.Empty);
        }

        public static EconomyAsset Tickets(Guid accountId)
        {
            return new EconomyAsset(accountId, null, TICKETS, string.Empty, string.Empty);
        }

        public static EconomyAsset Essence(Guid accountId, Guid characterId)
        {
            return new EconomyAsset(accountId, characterId, ESSENCE, string.Empty, string.Empty);
        }

        public static EconomyAsset Shards(Guid accountId, Guid characterId)
        {
            return new EconomyAsset(accountId, characterId, SHARDS, string.Empty, string.Empty);
        }

        public static EconomyAsset AccountItem(Guid accountId, string definitionId, string bindingKind)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(bindingKind);
            return new EconomyAsset(accountId, null, ACCOUNT_ITEM, definitionId, bindingKind);
        }
    }
}
