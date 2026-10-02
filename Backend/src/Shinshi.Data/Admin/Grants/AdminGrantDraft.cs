// System
using System;
using System.Collections.Generic;
using System.Linq;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Inbox;

namespace Shinshi.Data.Admin.Grants
{
    /// <summary> 검증을 마친 지급 초안. TryCreate만 만들 수 있다 </summary>
    public sealed class AdminGrantDraft
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid                      GrantId          { get; }
        public IReadOnlyList<Guid>       TargetAccountIds { get; }
        public IReadOnlyList<RewardLine> Rewards          { get; }
        public int                       ClaimDays        { get; }
        public string                    SourceKind       { get; }
        public string?                   SourceRef        { get; }

        /****************************************
        *                Methods
        ****************************************/

        private AdminGrantDraft(Guid grantId, IReadOnlyList<Guid> targetAccountIds, IReadOnlyList<RewardLine> rewards,
            int claimDays, string sourceKind, string? sourceRef)
        {
            GrantId          = grantId;
            TargetAccountIds = targetAccountIds;
            Rewards          = rewards;
            ClaimDays        = claimDays;
            SourceKind       = sourceKind;
            SourceRef        = sourceRef;
        }

        /// <summary> 대상 1..1000(중복, 빈 ID 거절), 보상 규칙, 기한 1..365(없으면 30), 출처와 참조를 검사한다. 하나라도 어긋나면 null </summary>
        public static AdminGrantDraft? TryCreate(Guid grantId, IReadOnlyList<Guid>? targetAccountIds,
            IReadOnlyList<RewardLine>? rewards, int? claimDays, string? sourceKind, string? sourceRef)
        {
            if ((grantId == Guid.Empty) || (targetAccountIds == null) || (targetAccountIds.Count == 0)
                || (targetAccountIds.Count > AdminGrantCodes.MAX_TARGET_ACCOUNTS)
                || targetAccountIds.Contains(Guid.Empty)
                || (targetAccountIds.Distinct().Count() != targetAccountIds.Count)
                || !RewardLines.IsValid(rewards))
            {
                return null;
            }

            int days = claimDays ?? AdminGrantCodes.DEFAULT_CLAIM_DAYS;
            if ((days < AdminGrantCodes.MIN_CLAIM_DAYS) || (days > AdminGrantCodes.MAX_CLAIM_DAYS))
            {
                return null;
            }

            string kind = string.IsNullOrEmpty(sourceKind) ? AdminGrantCodes.SOURCE_ADMIN_GRANT : sourceKind;
            string? reference = string.IsNullOrEmpty(sourceRef) ? null : sourceRef;
            if ((kind != AdminGrantCodes.SOURCE_ADMIN_GRANT) && (kind != AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION))
            {
                return null;
            }
            if ((kind == AdminGrantCodes.SOURCE_RECOVERY_COMPENSATION) && string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }
            if ((reference != null) && (reference.Length > AdminGrantCodes.MAX_SOURCE_REF_LENGTH))
            {
                return null;
            }

            return new AdminGrantDraft(grantId, targetAccountIds.ToArray(), rewards!.ToArray(), days, kind, reference);
        }
    }
}
