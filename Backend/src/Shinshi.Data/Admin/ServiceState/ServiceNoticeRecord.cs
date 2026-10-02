// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> service_notices 한 행. 노출 여부는 읽는 쪽이 자기 시각으로 starts_at, ends_at, ended_at을 비교해 정한다 </summary>
    public sealed class ServiceNoticeRecord
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid NoticeId { get; }
        public string Title { get; }
        public string Body { get; }
        public string Target { get; }
        public int Severity { get; }
        public DateTimeOffset StartsAt { get; }
        public DateTimeOffset EndsAt { get; }
        public Guid CreatedBy { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset? EndedAt { get; }
        public Guid? EndedBy { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServiceNoticeRecord(Guid noticeId, string title, string body, string target, int severity,
            DateTimeOffset startsAt, DateTimeOffset endsAt, Guid createdBy, DateTimeOffset createdAt,
            DateTimeOffset? endedAt, Guid? endedBy)
        {
            NoticeId  = noticeId;
            Title     = title;
            Body      = body;
            Target    = target;
            Severity  = severity;
            StartsAt  = startsAt;
            EndsAt    = endsAt;
            CreatedBy = createdBy;
            CreatedAt = createdAt;
            EndedAt   = endedAt;
            EndedBy   = endedBy;
        }

        /// <summary> 운영자가 내리지 않았고 now가 노출 기간 안이면 true </summary>
        public bool IsVisibleAt(DateTimeOffset now)
        {
            return (EndedAt == null) && (StartsAt <= now) && (now < EndsAt);
        }
    }
}
