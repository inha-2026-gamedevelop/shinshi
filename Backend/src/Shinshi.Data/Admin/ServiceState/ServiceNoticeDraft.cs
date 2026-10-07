// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 검증을 마친 새 공지. StartsAt이 없으면 작업 트랜잭션의 DB 시각부터 노출한다 </summary>
    public sealed class ServiceNoticeDraft
    {
        /****************************************
        *                Fields
        ****************************************/

        // service_notices.category 기본값과 같다
        private const string DEFAULT_CATEGORY = "Notice";

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
        public string Category { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ServiceNoticeDraft(Guid noticeId, string title, string body, string target, int severity,
            DateTimeOffset? startsAt, DateTimeOffset endsAt, string category = DEFAULT_CATEGORY)
        {
            if (noticeId == Guid.Empty)
            {
                throw new ArgumentException("Notice ID is required.", nameof(noticeId));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            ArgumentNullException.ThrowIfNull(body);
            ArgumentException.ThrowIfNullOrWhiteSpace(target);
            ArgumentException.ThrowIfNullOrWhiteSpace(category);

            NoticeId = noticeId;
            Title    = title;
            Body     = body;
            Target   = target;
            Severity = severity;
            StartsAt = startsAt;
            EndsAt   = endsAt;
            Category = category;
        }
    }
}
