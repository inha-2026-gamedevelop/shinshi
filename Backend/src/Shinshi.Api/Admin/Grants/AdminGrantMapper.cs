// System
using System;
using System.Collections.Generic;
using System.Linq;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin.Grants;
using Shinshi.Data.Inbox;

namespace Shinshi.Backend.Admin
{
    /// <summary> 지급 요청 DTO와 데이터 계층 모델 사이의 변환. 형식 오류는 null로 알리고 호출자가 400으로 거절한다 </summary>
    public static class AdminGrantMapper
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 대상 ID 문자열이 모두 Guid이고 보상 줄이 모두 있으면 초안 검증까지 한다 </summary>
        public static AdminGrantDraft? ToDraft(Guid grantId, AdminGrantCreateRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if ((request.TargetAccountIds == null) || (request.Rewards == null))
            {
                return null;
            }

            List<Guid> targets = new List<Guid>(request.TargetAccountIds.Length);
            foreach (string value in request.TargetAccountIds)
            {
                if (!Guid.TryParse(value, out Guid accountId))
                {
                    return null;
                }
                targets.Add(accountId);
            }

            if (request.Rewards.Any(reward => reward == null))
            {
                return null;
            }

            List<RewardLine> rewards = request.Rewards.Select(reward => new RewardLine(reward.AssetKind ?? string.Empty,
                reward.AssetId, reward.BindingKind, reward.Quantity)).ToList();
            return AdminGrantDraft.TryCreate(grantId, targets, rewards, request.ClaimDays, request.SourceKind,
                request.SourceRef);
        }

        /// <summary> 빈 문자열이나 null은 캐릭터 없음이다. 형식이 틀리면 false </summary>
        public static bool TryParseOptionalId(string? value, out Guid? id)
        {
            id = null;
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }
            if (!Guid.TryParse(value, out Guid parsed))
            {
                return false;
            }

            id = parsed;
            return true;
        }

        public static AdminGrantDetail ToDetail(AdminGrantRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            return new AdminGrantDetail(record.GrantId.ToString("D"), record.State, record.SourceKind, record.SourceRef,
                record.Reason, record.CreatedBy.ToString("D"), record.CreatedAt.ToUnixTimeMilliseconds(),
                record.ExecutedAt?.ToUnixTimeMilliseconds(), record.CancelledAt?.ToUnixTimeMilliseconds(),
                record.ClaimDays, record.Rewards.Select(ToContract).ToArray(), record.TargetCount, record.DeliveredCount,
                record.ClaimedCount, record.OpenCount, record.ExpiredCount, record.CancelledCount);
        }

        private static AdminGrantReward ToContract(RewardLine line)
        {
            return new AdminGrantReward(line.AssetKind, line.AssetId, line.BindingKind, line.Quantity);
        }
    }
}
