// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 출정 상태를 결과 창 값으로 옮기는 규칙 </summary>
    public static class SortieResultRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SHARD_TITLE    = "정련편";
        private const string CURRENCY_TITLE = "엽전";
        private const string TICKET_TITLE   = "보스 입장권";

        private static readonly IReadOnlyList<ItemSlotData> G_NoItems = Array.Empty<ItemSlotData>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 출정이 끝났거나 나만 먼저 빠졌으면 결과 창을 띄운다 </summary>
        public static bool ShouldShow(in SortieStatusMessage status)
        {
            return (status.HasPersonalClose) || (status.Phase == ESortiePhase.Ended) || (status.Phase == ESortiePhase.Auction)
                || (status.Phase == ESortiePhase.Closed);
        }

        /// <summary> 내 종료 사유가 있으면 그것을, 없으면 출정 종료 사유를 쓴다 </summary>
        public static ESessionEndReason ResolveReason(in SortieStatusMessage status)
        {
            return status.HasPersonalClose ? status.PersonalCloseReason : status.EndReason;
        }

        /// <summary> 정산 복구로 넘어간 출정은 다시 확인 중으로 보여 준다 </summary>
        public static ESaveStatus ToSaveStatus(ESortieSettlementState state)
        {
            switch (state)
            {
                case ESortieSettlementState.Settled:
                    return ESaveStatus.Saved;
                case ESortieSettlementState.Deferred:
                    return ESaveStatus.Retrying;
                case ESortieSettlementState.Pending:
                    return ESaveStatus.Saving;
                default:
                    return ESaveStatus.Idle;
            }
        }

        /// <summary> 보상 내역을 아직 받지 못했을 때의 결과 창, 정기와 아이템을 비워 둔다 </summary>
        public static ResultScreenData Build(in SortieStatusMessage status, string regionName)
        {
            return new ResultScreenData(ResolveReason(in status), regionName ?? string.Empty, 0, 0, 0, G_NoItems, G_NoItems);
        }

        // 기본 줄은 결과와 무관하게 확보한 웨이브 정기와 보스 정기, 추가 줄은 지급률을 곱해 받은 적립 정기다.
        // 확정 칸은 정련편, 엽전, 보스 재료이고 확률 칸은 처치 판정으로 얻는 보스 입장권과 부적이다
        /// <summary> 서버가 보낸 정산 보상 내역으로 결과 창 값을 만든다, rewards가 없으면 등급을 일반으로 둔다 </summary>
        public static ResultScreenData Build(in SortieStatusMessage status, string regionName, in SortieRewardMessage reward,
            RewardCatalogSO rewards)
        {
            var confirmed = new List<ItemSlotData>();
            AddWallet(confirmed, Constants.Session.SETTLEMENT_FORBIDDEN_SHARD_ID, SHARD_TITLE, reward.RefineShards, rewards);
            AddWallet(confirmed, Constants.Session.SETTLEMENT_FORBIDDEN_CURRENCY_ID, CURRENCY_TITLE, reward.Currency, rewards);
            AddLines(confirmed, reward.Items, rewards);

            var chance = new List<ItemSlotData>();
            AddWallet(chance, Constants.Session.SETTLEMENT_FORBIDDEN_TICKET_ID, TICKET_TITLE, reward.Tickets, rewards);
            AddLines(chance, reward.Talismans, rewards);

            int baseEssence = ToCount(SaturatingAdd(reward.BaseEssence, reward.BossEssence));
            return new ResultScreenData(ResolveReason(in status), regionName ?? string.Empty, baseEssence,
                ToCount(reward.PaidExtraEssence), ToCount(reward.LostExtraEssence), confirmed, chance);
        }

        private static void AddWallet(List<ItemSlotData> into, string id, string title, long quantity, RewardCatalogSO rewards)
        {
            if (quantity <= 0)
            {
                return;
            }

            into.Add(new ItemSlotData(id, FindGrade(rewards, id), ToCount(quantity), title, string.Empty));
        }

        // 아이템 이름 표가 아직 없어 인벤토리처럼 정의 ID를 이름으로 쓴다
        private static void AddLines(List<ItemSlotData> into, SortieRewardLine[] lines, RewardCatalogSO rewards)
        {
            for (int i = 0; i < lines.Length; ++i)
            {
                string id = lines[i].DefinitionId;
                if ((string.IsNullOrEmpty(id)) || (lines[i].Quantity <= 0))
                {
                    continue;
                }

                into.Add(new ItemSlotData(id, FindGrade(rewards, id), ToCount(lines[i].Quantity), id, string.Empty));
            }
        }

        private static EItemGrade FindGrade(RewardCatalogSO rewards, string id)
        {
            if ((rewards != null) && (rewards.TryGetItem(id, out RewardCatalogSO.ItemDefinition definition)))
            {
                return definition.Grade;
            }

            return EItemGrade.Common;
        }

        private static long SaturatingAdd(long a, long b)
        {
            long sum = a + b;
            return (sum < a) ? long.MaxValue : sum;
        }

        // 화면 칸은 int라 넘치는 값은 최대값으로, 음수는 0으로 둔다
        private static int ToCount(long value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return (value > int.MaxValue) ? int.MaxValue : (int)value;
        }
    }
}
