// System
using System;
using System.Collections.Generic;

namespace Shinshi.Data.Inbox
{
    /// <summary> 목록 조회 결과와 그 조회의 DB 시각. 만료 여부는 이 시각으로 갈랐다.
    /// CorruptInboxIds는 보상 JSON이 손상돼 목록에서 뺀 우편이다(호출자가 ID만 경고로 남긴다) </summary>
    public sealed class InboxListing
    {
        /****************************************
        *              Properties
        ****************************************/

        public DateTimeOffset             ServerTime      { get; }
        public IReadOnlyList<InboxRecord> Entries         { get; }
        public IReadOnlyList<Guid>        CorruptInboxIds { get; }

        /****************************************
        *                Methods
        ****************************************/

        public InboxListing(DateTimeOffset serverTime, IReadOnlyList<InboxRecord> entries)
            : this(serverTime, entries, Array.Empty<Guid>())
        {
        }

        public InboxListing(DateTimeOffset serverTime, IReadOnlyList<InboxRecord> entries,
            IReadOnlyList<Guid> corruptInboxIds)
        {
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(corruptInboxIds);
            ServerTime      = serverTime;
            Entries         = entries;
            CorruptInboxIds = corruptInboxIds;
        }
    }
}
