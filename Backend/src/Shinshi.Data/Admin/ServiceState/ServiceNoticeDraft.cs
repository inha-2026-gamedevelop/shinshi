// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 검증을 마친 새 공지. StartsAt이 없으면 작업 트랜잭션의 DB 시각부터 노출한다 </summary>
    public sealed class ServiceNoticeDraft
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid NoticeId { get; }
        public string Title { get; }
        public string Body { get; }
        public string Target { get; }
        public int Severity { get; }
        public DateTimeOffset? StartsAt { get; }
        public DateTimeOffset EndsAt { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServiceNoticeDraft(Guid noticeId, string title, string body, string target, int severity,
            DateTimeOffset? startsAt, DateTimeOffset endsAt)
        {
            if (noticeId == Guid.Empty)
            {
                throw new ArgumentException("Notice ID is required.", nameof(noticeId));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            ArgumentNullException.ThrowIfNull(body);
            ArgumentException.ThrowIfNullOrWhiteSpace(target);

            NoticeId = noticeId;
            Title    = title;
            Body     = body;
            Target   = target;
            Severity = severity;
            StartsAt = startsAt;
            EndsAt   = endsAt;
        }
    }
}
