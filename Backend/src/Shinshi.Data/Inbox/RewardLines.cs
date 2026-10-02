// System
using System;
using System.Collections.Generic;

// Third Party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Inbox
{
    /// <summary> 지급 보상 목록의 검증과 JSON 변환. 작성 정책(IsValid)은 지급 작성과 개인 조정만 쓰고,
    /// 저장값 읽기(Deserialize)는 구조만 다시 본다 </summary>
    public static class RewardLines
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly JsonSerializerSettings G_Settings = new JsonSerializerSettings
        {
            TypeNameHandling  = TypeNameHandling.None,
            DateParseHandling = DateParseHandling.None,
            NullValueHandling = NullValueHandling.Include
        };

        private static readonly string[] G_UpdateOrder =
        {
            EconomyAsset.CURRENCY, EconomyAsset.TICKETS, EconomyAsset.ESSENCE, EconomyAsset.SHARDS,
            EconomyAsset.ACCOUNT_ITEM
        };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지원하는 자산 종류이고 assetId, bindingKind가 그 종류에 맞는지 본다. 수량은 보지 않는다.
        /// 아이템은 정산과 같은 허용 규칙(item_ 접두 ID 또는 부적 효과 ID, 계정 귀속)만 받는다 </summary>
        public static bool IsValidAsset(string? assetKind, string? assetId, string? bindingKind)
        {
            string id = assetId ?? string.Empty;
            string binding = bindingKind ?? string.Empty;
            switch (assetKind)
            {
                case EconomyAsset.CURRENCY:
                case EconomyAsset.TICKETS:
                case EconomyAsset.ESSENCE:
                case EconomyAsset.SHARDS:
                    return (id.Length == 0) && (binding.Length == 0);
                case EconomyAsset.ACCOUNT_ITEM:
                    // 정기, 정련편, 티켓, 엽전의 아이템 ID는 IsAllowedItem이 거절한다. 지갑 잔액 경로를 우회하지 못하게 한다
                    return (SettlementSanityLimits.IsAllowedItem(id) || SettlementSanityLimits.IsTalismanEffectId(id))
                        && SettlementSanityLimits.IsAllowedBinding(binding);
                default:
                    return false;
            }
        }

        /// <summary> 작성 정책: 줄 수 1..50, 각 줄의 자산과 수량(1..상한), 같은 자산 키 중복을 검사한다 </summary>
        public static bool IsValid(IReadOnlyList<RewardLine>? lines)
        {
            if ((lines == null) || (lines.Count == 0) || (lines.Count > AdminGrantCodes.MAX_REWARD_LINES))
            {
                return false;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RewardLine line in lines)
            {
                if ((line == null) || !IsValidAsset(line.AssetKind, line.AssetId, line.BindingKind)
                    || (line.Quantity <= 0) || (line.Quantity > AdminGrantCodes.MAX_REWARD_QUANTITY)
                    || !keys.Add(line.Key))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool RequiresCharacter(IReadOnlyList<RewardLine> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);
            foreach (RewardLine line in lines)
            {
                if (line.IsCharacterAsset)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 원장과 잔액 갱신에 쓰는 자산 식별자. 캐릭터 자산이면 characterId가 있어야 한다 </summary>
        public static EconomyAsset ToAsset(Guid accountId, Guid? characterId, string assetKind, string assetId,
            string bindingKind)
        {
            return assetKind switch
            {
                EconomyAsset.CURRENCY     => EconomyAsset.Currency(accountId),
                EconomyAsset.TICKETS      => EconomyAsset.Tickets(accountId),
                EconomyAsset.ESSENCE      => EconomyAsset.Essence(accountId, RequireCharacter(characterId)),
                EconomyAsset.SHARDS       => EconomyAsset.Shards(accountId, RequireCharacter(characterId)),
                EconomyAsset.ACCOUNT_ITEM => EconomyAsset.AccountItem(accountId, assetId, bindingKind),
                _ => throw new ArgumentException("Unsupported asset kind.", nameof(assetKind))
            };
        }

        /// <summary> 잔액 갱신 순서. 정산과 같은 지갑 -> 캐릭터 -> 아이템이고 아이템은 ID 순서다 </summary>
        public static List<RewardLine> OrderForUpdate(IReadOnlyList<RewardLine> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);
            List<RewardLine> ordered = new List<RewardLine>(lines);
            ordered.Sort((left, right) =>
            {
                int byKind = KindRank(left.AssetKind).CompareTo(KindRank(right.AssetKind));
                if (byKind != 0)
                {
                    return byKind;
                }

                int byId = string.CompareOrdinal(left.AssetId, right.AssetId);
                return byId != 0 ? byId : string.CompareOrdinal(left.BindingKind, right.BindingKind);
            });
            return ordered;
        }

        public static string Serialize(IReadOnlyList<RewardLine> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);
            return JsonConvert.SerializeObject(lines, Formatting.None, G_Settings);
        }

        /// <summary> 저장된 JSON을 읽는다. 구조(알려진 자산 종류와 그 종류에 맞는 ID 형식, 수량 1 이상, 같은 자산 키 중복 없음)만
        /// 다시 보고 어긋나면 손상으로 보고 예외를 낸다. 작성 정책(수량 상한, 허용 아이템, 줄 수)은 IsValid로 작성 때만 본다 </summary>
        public static List<RewardLine> Deserialize(string json)
        {
            if (!TryDeserialize(json, out List<RewardLine>? lines))
            {
                throw new InvalidOperationException("Stored reward lines are corrupt.");
            }

            return lines!;
        }

        /// <summary> Deserialize와 같은 구조 검사로 읽고 손상이면 false다. 목록이 손상 행 하나 때문에 전체 실패하지 않게 쓴다 </summary>
        public static bool TryDeserialize(string? json, out List<RewardLine>? lines)
        {
            lines = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            List<RewardLine>? parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<List<RewardLine>>(json, G_Settings);
            }
            catch (JsonException)
            {
                return false;
            }
            if (!IsStructurallyValid(parsed))
            {
                return false;
            }

            lines = parsed;
            return true;
        }

        // 저장값 읽기 검사. 작성 정책을 여기서 다시 걸면 정책을 바꾼 뒤 이미 지급한 우편의 목록, 수령, 지급 조회가 모두 실패하고
        // 특히 실행, 취소를 커밋한 뒤 응답 조회가 500이 되어 운영자가 실패로 오인한다. 그래서 잔액 갱신과 원장이 받는 형식만 본다
        private static bool IsStructurallyValid(IReadOnlyList<RewardLine>? lines)
        {
            if ((lines == null) || (lines.Count == 0))
            {
                return false;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RewardLine line in lines)
            {
                if ((line == null) || (line.Quantity <= 0) || (!HasAssetShape(line)) || (!keys.Add(line.Key)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasAssetShape(RewardLine line)
        {
            string id = line.AssetId ?? string.Empty;
            string binding = line.BindingKind ?? string.Empty;
            switch (line.AssetKind)
            {
                case EconomyAsset.CURRENCY:
                case EconomyAsset.TICKETS:
                case EconomyAsset.ESSENCE:
                case EconomyAsset.SHARDS:
                    return (id.Length == 0) && (binding.Length == 0);
                case EconomyAsset.ACCOUNT_ITEM:
                    return (!string.IsNullOrWhiteSpace(id)) && (!string.IsNullOrWhiteSpace(binding));
                default:
                    return false;
            }
        }

        private static Guid RequireCharacter(Guid? characterId)
        {
            return characterId ?? throw new ArgumentException("Character assets require a character.", nameof(characterId));
        }

        private static int KindRank(string assetKind)
        {
            int rank = Array.IndexOf(G_UpdateOrder, assetKind);
            return rank >= 0 ? rank : G_UpdateOrder.Length;
        }
    }
}
