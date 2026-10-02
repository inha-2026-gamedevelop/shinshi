// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 출정 결과와 확보 기록으로 참가자별 정산안을 만든다, API는 다시 계산하지 않는다. 같은 입력은 같은 결과다 </summary>
    public static class SettlementRules
    {
        // 정산안이 API 상한(SettlementSanityLimits)이나 아이템 규칙을 하나라도 어기면 SessionEnded 배치 전체가 확정 거절돼
        // 저장 큐가 멈춘다. 종료 이벤트가 저장되지 않으니 정상으로 끝난 출정도 API 장애 취소로 바뀌어 결과 전체를 잃는다.
        // 그래서 서버가 먼저 상한으로 자르고 지급 금지 ID를 빼서 정산이 통과하게 하고, 고친 내역은 경고로 남긴다.
        // 종료 때 한 번만 도는 경로라 리스트와 배열 할당을 허용한다

        /****************************************
        *                Fields
        ****************************************/

        // TalismanCatalogSO.EFFECT_ID_PATTERN(^T\d{2}-\d[A-Z]$)과 같은 형식이다. 백엔드 SettlementSanityLimits도 같은 규칙을 쓴다
        private const int  TALISMAN_ID_LENGTH       = 6;
        private const char TALISMAN_ID_PREFIX       = 'T';
        private const char TALISMAN_ID_SEPARATOR    = '-';
        private const int  TALISMAN_SEPARATOR_INDEX = 3;
        private const int  TALISMAN_CHOICE_INDEX    = 5;

        private static readonly Comparison<SettlementItemGrant> _itemOrder = CompareItems;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 추가 정기 지급률(%), 성공만 전액이고 실패, 포기, 유예 만료, 전멸, 장애 취소는 실패 비율이다 </summary>
        public static long GetExtraEssencePercent(ESessionEndReason reason)
        {
            return (reason == ESessionEndReason.Success)
                ? Constants.Session.SUCCESS_EXTRA_ESSENCE_PERCENT
                : Constants.Session.FAILURE_EXTRA_ESSENCE_PERCENT;
        }

        /// <summary> 적립한 추가 정기 중 지급할 양, 정수 나눗셈으로 내림한다 </summary>
        public static long CalculatePaidExtra(long accruedExtra, ESessionEndReason reason)
        {
            if (accruedExtra <= 0)
            {
                return 0;
            }

            // 적립은 출정 상한 이하라 곱해도 long을 넘지 않는다
            return accruedExtra * GetExtraEssencePercent(reason) / Constants.Session.ESSENCE_PERCENT_BASE;
        }

        /// <summary> 기본 정기와 지급 추가 정기, 보스 정기를 합쳐 상한으로 자른다. 보스 정기는 처치 성공일 때만 더한다 </summary>
        public static SettlementBreakdown CalculateEssence(ESessionEndReason reason, long baseEssence, long accruedExtra,
            long bossEssence)
        {
            long baseValue = Math.Max(0L, baseEssence);
            long accrued   = Math.Max(0L, accruedExtra);
            long paid      = CalculatePaidExtra(accrued, reason);
            long boss      = (reason == ESessionEndReason.Success) ? Math.Max(0L, bossEssence) : 0L;
            long total     = SaturatingAdd(SaturatingAdd(baseValue, paid), boss);
            bool clamped   = total > Constants.Session.SETTLEMENT_MAX_SESSION_ESSENCE;
            if (clamped)
            {
                total = Constants.Session.SETTLEMENT_MAX_SESSION_ESSENCE;
            }

            return new SettlementBreakdown(baseValue, accrued, paid, boss, total, clamped);
        }

        /// <summary> API가 정산 아이템으로 받는 ID인지 본다, 형식은 item_ 접두와 소문자, 숫자, 밑줄이고 지갑 자산은 거절한다 </summary>
        public static bool IsAllowedItem(string definitionId)
        {
            return (IsItemIdFormat(definitionId)) && (!IsForbiddenItem(definitionId));
        }

        /// <summary> 정산안에서 빼야 하는 지갑 자산 ID인지 본다 </summary>
        public static bool IsForbiddenItem(string definitionId)
        {
            return (definitionId == Constants.Session.SETTLEMENT_FORBIDDEN_ESSENCE_ID)
                || (definitionId == Constants.Session.SETTLEMENT_FORBIDDEN_SHARD_ID)
                || (definitionId == Constants.Session.SETTLEMENT_FORBIDDEN_TICKET_ID)
                || (definitionId == Constants.Session.SETTLEMENT_FORBIDDEN_CURRENCY_ID);
        }

        /// <summary> 부적 효과 ID 형식인지 본다, T 두 자리 숫자 - 단계 숫자 선택 대문자(예 T01-1A)다 </summary>
        public static bool IsTalismanEffectId(string effectId)
        {
            if ((effectId == null) || (effectId.Length != TALISMAN_ID_LENGTH) || (effectId[0] != TALISMAN_ID_PREFIX)
                || (effectId[TALISMAN_SEPARATOR_INDEX] != TALISMAN_ID_SEPARATOR))
            {
                return false;
            }

            for (int i = 1; i < TALISMAN_CHOICE_INDEX; ++i)
            {
                if ((i != TALISMAN_SEPARATOR_INDEX) && ((effectId[i] < '0') || (effectId[i] > '9')))
                {
                    return false;
                }
            }

            char choice = effectId[TALISMAN_CHOICE_INDEX];
            return (choice >= 'A') && (choice <= 'Z');
        }

        /// <summary> 후보 줄을 API 규칙에 맞춘다, 지급 금지와 형식 오류는 빼고 같은 줄은 합치고 수량과 줄 수를 상한으로 자른다 </summary>
        public static SettlementItemGrant[] SanitizeItems(IReadOnlyList<SettlementItemGrant> candidates,
            ref SettlementSanityReport report)
        {
            return SanitizeLines(candidates, false, ref report);
        }

        /// <summary> 부적 후보 줄을 API 규칙에 맞춘다, 효과 ID 형식 오류는 빼고 같은 효과는 합치고 수량과 줄 수를 상한으로 자른다 </summary>
        public static SettlementItemGrant[] SanitizeTalismans(IReadOnlyList<SettlementItemGrant> candidates,
            ref SettlementSanityReport report)
        {
            return SanitizeLines(candidates, true, ref report);
        }

        /// <summary> 정련편, 티켓, 엽전 칸 하나를 0 이상 상한 이하로 맞춘다 </summary>
        public static long ClampWallet(long value, long max, ref SettlementSanityReport report)
        {
            if (value <= 0)
            {
                return 0;
            }

            if (value > max)
            {
                report.AddClampedWallet();
                return max;
            }

            return value;
        }

        /// <summary> 참가자 한 명의 정기 내역, 원장 인덱스는 명단 순서다 </summary>
        public static SettlementBreakdown CalculateBreakdown(in SettlementParticipant participant, SettlementLedger ledger,
            int index)
        {
            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            return CalculateEssence(participant.Reason, ledger.GetBaseEssence(index), ledger.GetExtraEssence(index),
                ledger.GetBossEssence(index));
        }

        /// <summary> 원래 참가자 전원의 정산안, 순서는 participants 순서다. bossMaterialItemId는 보스 성공 재료 ID이고 없으면 null.
        /// 정련편, 티켓, 부적은 확보 보상이라 결과와 무관하게 보존하고, 보스 엽전과 재료는 개인 원인이 성공일 때만 넣는다 </summary>
        public static SettlementGrant[] CreateGrants(SettlementParticipant[] participants, int count, SettlementLedger ledger,
            string bossMaterialItemId, out SettlementSanityReport report)
        {
            report = default;
            if ((participants == null) || (ledger == null))
            {
                throw new ArgumentNullException((participants == null) ? nameof(participants) : nameof(ledger));
            }

            if ((count < 0) || (count > participants.Length) || (count > ledger.Capacity))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var grants     = new SettlementGrant[count];
            var candidates = new List<SettlementItemGrant>();
            for (int i = 0; i < count; ++i)
            {
                SettlementBreakdown breakdown = CalculateBreakdown(in participants[i], ledger, i);
                if (breakdown.IsEssenceClamped)
                {
                    report.AddClampedEssence();
                }

                bool success = participants[i].Reason == ESessionEndReason.Success;
                candidates.Clear();
                if (success)
                {
                    AddCandidate(candidates, bossMaterialItemId, Constants.Session.SETTLEMENT_ITEM_BINDING_KIND,
                        ledger.GetBossMaterials(i));
                }

                SettlementItemGrant[] items = SanitizeItems(candidates, ref report);

                candidates.Clear();
                CollectTalismans(candidates, ledger, i);
                SettlementItemGrant[] talismans = SanitizeTalismans(candidates, ref report);

                long shards   = ClampWallet(ledger.GetRefineShards(i), Constants.Session.SETTLEMENT_MAX_SESSION_SHARDS,
                    ref report);
                long tickets  = ClampWallet(ledger.GetTickets(i), Constants.Session.SETTLEMENT_MAX_SESSION_TICKETS,
                    ref report);
                long currency = success
                    ? ClampWallet(ledger.GetBossCurrency(i), Constants.Session.SETTLEMENT_MAX_SESSION_CURRENCY, ref report)
                    : 0L;
                grants[i] = new SettlementGrant(participants[i].ParticipantId, breakdown.Essence, items, shards, tickets,
                    currency, talismans);
            }

            return grants;
        }

        // 확보 부적은 결과와 무관하게 보존하므로 지급률을 곱하지 않는다. 효과 순서는 SetEffects에 넘긴 정렬 순서다
        private static void CollectTalismans(List<SettlementItemGrant> candidates, SettlementLedger ledger, int index)
        {
            string binding = Constants.Session.SETTLEMENT_ITEM_BINDING_KIND;
            int effectCount = ledger.GetEffectCount(index);
            for (int e = 0; e < effectCount; ++e)
            {
                AddCandidate(candidates, ledger.GetEffectId(index, e), binding, ledger.GetTalismans(index, e));
            }
        }

        // 아이템 줄과 부적 줄의 공통 정리. 형식 검사와 상한, 보고 칸만 다르다
        private static SettlementItemGrant[] SanitizeLines(IReadOnlyList<SettlementItemGrant> candidates, bool talisman,
            ref SettlementSanityReport report)
        {
            if ((candidates == null) || (candidates.Count == 0))
            {
                return Array.Empty<SettlementItemGrant>();
            }

            var merged = new List<SettlementItemGrant>(candidates.Count);
            for (int i = 0; i < candidates.Count; ++i)
            {
                SettlementItemGrant item = candidates[i];
                if ((item == null) || (item.Quantity <= 0) || (string.IsNullOrWhiteSpace(item.BindingKind)))
                {
                    continue;
                }

                if (!IsAcceptedLine(item.DefinitionId, talisman, ref report))
                {
                    continue;
                }

                // API는 같은 ID를 여러 줄로 나눠도 합계로 상한을 본다. 합쳐서 한 줄로 보낸다
                int existing = IndexOf(merged, item.DefinitionId, item.BindingKind);
                if (existing < 0)
                {
                    merged.Add(item);
                }
                else
                {
                    SettlementItemGrant before = merged[existing];
                    merged[existing] = new SettlementItemGrant(before.DefinitionId, before.BindingKind,
                        SaturatingAdd(before.Quantity, item.Quantity));
                }
            }

            merged.Sort(_itemOrder);
            int maxLines = talisman
                ? Constants.Session.SETTLEMENT_MAX_GRANT_TALISMANS
                : Constants.Session.SETTLEMENT_MAX_GRANT_ITEMS;
            long maxQuantity = talisman
                ? Constants.Session.SETTLEMENT_MAX_TALISMAN_QUANTITY
                : Constants.Session.SETTLEMENT_MAX_ITEM_QUANTITY;
            int count = merged.Count;
            if (count > maxLines)
            {
                if (talisman)
                {
                    report.AddDroppedTalismanOverflow(count - maxLines);
                }
                else
                {
                    report.AddDroppedOverflow(count - maxLines);
                }

                count = maxLines;
            }

            var result = new SettlementItemGrant[count];
            for (int i = 0; i < count; ++i)
            {
                SettlementItemGrant item = merged[i];
                if (item.Quantity > maxQuantity)
                {
                    if (talisman)
                    {
                        report.AddClampedTalisman();
                    }
                    else
                    {
                        report.AddClampedQuantity();
                    }

                    item = new SettlementItemGrant(item.DefinitionId, item.BindingKind, maxQuantity);
                }

                result[i] = item;
            }

            return result;
        }

        // 부적 줄은 효과 ID 형식만, 아이템 줄은 아이템 ID 형식과 지급 금지 목록을 본다. 지갑 ID는 아이템 줄에서만 막는다
        private static bool IsAcceptedLine(string definitionId, bool talisman, ref SettlementSanityReport report)
        {
            if (talisman)
            {
                if (IsTalismanEffectId(definitionId))
                {
                    return true;
                }

                report.AddDroppedInvalidTalisman();
                return false;
            }

            if (!IsItemIdFormat(definitionId))
            {
                report.AddDroppedInvalidId();
                return false;
            }

            if (IsForbiddenItem(definitionId))
            {
                report.AddDroppedForbidden();
                return false;
            }

            return true;
        }

        private static void AddCandidate(List<SettlementItemGrant> candidates, string definitionId, string binding,
            long quantity)
        {
            if ((quantity <= 0) || (string.IsNullOrEmpty(definitionId)))
            {
                return;
            }

            candidates.Add(new SettlementItemGrant(definitionId, binding, quantity));
        }

        private static bool IsItemIdFormat(string definitionId)
        {
            string prefix = Constants.Session.SETTLEMENT_ITEM_DEFINITION_PREFIX;
            if ((string.IsNullOrEmpty(definitionId))
                || (definitionId.Length > Constants.Session.SETTLEMENT_MAX_DEFINITION_ID_LENGTH)
                || (definitionId.Length <= prefix.Length)
                || (!definitionId.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return false;
            }

            for (int i = 0; i < definitionId.Length; ++i)
            {
                char character = definitionId[i];
                bool allowed = ((character >= 'a') && (character <= 'z')) || ((character >= '0') && (character <= '9'))
                    || (character == '_');
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private static int IndexOf(List<SettlementItemGrant> items, string definitionId, string bindingKind)
        {
            for (int i = 0; i < items.Count; ++i)
            {
                if ((string.Equals(items[i].DefinitionId, definitionId, StringComparison.Ordinal))
                    && (string.Equals(items[i].BindingKind, bindingKind, StringComparison.Ordinal)))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CompareItems(SettlementItemGrant left, SettlementItemGrant right)
        {
            int order = string.CompareOrdinal(left.DefinitionId, right.DefinitionId);
            return (order != 0) ? order : string.CompareOrdinal(left.BindingKind, right.BindingKind);
        }

        private static long SaturatingAdd(long left, long right)
        {
            return (left > (long.MaxValue - right)) ? long.MaxValue : left + right;
        }
    }
}
