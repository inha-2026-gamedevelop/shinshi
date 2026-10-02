// System
using System;
using System.Globalization;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 자산 표기 kind[:assetId[:binding]]와 보상 표기 kind[:assetId[:binding]]=qty를 읽는다.
    /// 서버도 검사하지만 오타를 보내기 전에 막아 확인 요약이 실제 요청과 같게 한다 </summary>
    internal static class AssetSyntax
    {
        /****************************************
        *                Fields
        ****************************************/

        // 계정 귀속 아이템의 기본 귀속. 원장과 지급이 쓰는 계정 아이템 귀속 값이 이것 하나다
        public const string DEFAULT_BINDING_KIND = "Account";

        private const char PART_SEPARATOR     = ':';
        private const char QUANTITY_SEPARATOR = '=';
        private const int  MAX_PARTS          = 3;
        private const int  ASSET_ID_PART      = 1;
        private const int  BINDING_PART       = 2;

        private static readonly string[] G_AssetKinds =
        {
            AdminGrantCodes.ASSET_CURRENCY, AdminGrantCodes.ASSET_TICKETS, AdminGrantCodes.ASSET_ESSENCE,
            AdminGrantCodes.ASSET_SHARDS, AdminGrantCodes.ASSET_ACCOUNT_ITEM
        };

        /****************************************
        *                Methods
        ****************************************/

        public static AssetSpec ParseAsset(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new CliUsageException("자산 표기가 비어 있습니다. 예: Currency, AccountItem:item_herb");
            }

            string[] parts = text.Trim().Split(PART_SEPARATOR);
            if (parts.Length > MAX_PARTS)
            {
                throw new CliUsageException("자산 표기는 kind[:assetId[:binding]] 입니다: " + text);
            }

            string kind = NormalizeKind(parts[0], text);
            if (!string.Equals(kind, AdminGrantCodes.ASSET_ACCOUNT_ITEM, StringComparison.Ordinal))
            {
                if (parts.Length > 1)
                {
                    throw new CliUsageException(kind + "는 assetId와 binding을 받지 않습니다: " + text);
                }

                return new AssetSpec(kind, string.Empty, string.Empty);
            }

            string assetId = parts.Length > ASSET_ID_PART ? parts[ASSET_ID_PART].Trim() : string.Empty;
            if (assetId.Length == 0)
            {
                throw new CliUsageException("AccountItem은 아이템 정의 ID가 필요합니다. 예: AccountItem:item_herb");
            }

            string binding = parts.Length > BINDING_PART ? parts[BINDING_PART].Trim() : DEFAULT_BINDING_KIND;
            if (binding.Length == 0)
            {
                throw new CliUsageException("binding이 비어 있습니다: " + text);
            }

            return new AssetSpec(kind, assetId, binding);
        }

        /// <summary> 수량은 1 이상 AdminGrantCodes.MAX_REWARD_QUANTITY 이하 </summary>
        public static AdminGrantReward ParseReward(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new CliUsageException("보상 표기가 비어 있습니다. 예: Currency=100");
            }

            int separator = text.LastIndexOf(QUANTITY_SEPARATOR);
            if (separator < 0)
            {
                throw new CliUsageException("보상 표기는 kind[:assetId[:binding]]=수량 입니다: " + text);
            }

            AssetSpec asset = ParseAsset(text.Substring(0, separator));
            string quantityText = text.Substring(separator + 1).Trim();
            if ((!long.TryParse(quantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long quantity))
                || (quantity <= 0) || (quantity > AdminGrantCodes.MAX_REWARD_QUANTITY))
            {
                throw new CliUsageException("보상 수량은 1에서 " + AdminGrantCodes.MAX_REWARD_QUANTITY.ToString(
                    CultureInfo.InvariantCulture) + " 사이의 정수입니다: " + text);
            }

            return new AdminGrantReward(asset.Kind, asset.AssetId, asset.BindingKind, quantity);
        }

        public static string Describe(AdminGrantReward reward)
        {
            string asset = string.IsNullOrEmpty(reward.AssetId)
                ? reward.AssetKind
                : reward.AssetKind + ":" + reward.AssetId + ":" + reward.BindingKind;
            return asset + "=" + reward.Quantity.ToString(CultureInfo.InvariantCulture);
        }

        private static string NormalizeKind(string kind, string text)
        {
            string trimmed = kind.Trim();
            foreach (string known in G_AssetKinds)
            {
                if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }

            throw new CliUsageException("알 수 없는 자산 종류입니다(" + string.Join(", ", G_AssetKinds) + "): " + text);
        }
    }
}
