// System
using System;
using System.Collections.Generic;

// Project
using Shinshi.Data.Inbox;

namespace Shinshi.Data.Admin.Grants
{
    /// <summary> 지급 한 건과 우편 집계. 집계의 만료 구분은 조회 시점 DB 시각 기준이다 </summary>
    public sealed class AdminGrantRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid                      GrantId        { get; init; }
        public string                    State          { get; init; } = string.Empty;
        public string                    SourceKind     { get; init; } = string.Empty;
        public string?                   SourceRef      { get; init; }
        public string                    Reason         { get; init; } = string.Empty;
        public Guid                      CreatedBy      { get; init; }
        public DateTimeOffset            CreatedAt      { get; init; }
        public DateTimeOffset?           ExecutedAt     { get; init; }
        public DateTimeOffset?           CancelledAt    { get; init; }
        public int                       ClaimDays      { get; init; }
        public IReadOnlyList<RewardLine> Rewards        { get; init; } = Array.Empty<RewardLine>();
        public int                       TargetCount    { get; init; }
        public int                       DeliveredCount { get; init; }
        public int                       ClaimedCount   { get; init; }
        public int                       OpenCount      { get; init; }
        public int                       ExpiredCount   { get; init; }
        public int                       CancelledCount { get; init; }
    }
}
