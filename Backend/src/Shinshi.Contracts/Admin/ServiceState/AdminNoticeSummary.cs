// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 어드민 공지 목록 한 줄. 상태는 조회 시각 기준으로 계산한다 </summary>
    [DataContract]
    public sealed class AdminNoticeSummary
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string STATUS_SCHEDULED = "Scheduled";
        public const string STATUS_ACTIVE    = "Active";
        public const string STATUS_ENDED     = "Ended";

        [DataMember(Name = "noticeId")]        private string _noticeId;
        [DataMember(Name = "title")]           private string _title;
        [DataMember(Name = "body")]            private string _body;
        [DataMember(Name = "target")]          private string _target;
        [DataMember(Name = "severity")]        private int    _severity;
        [DataMember(Name = "startsAtUnixMs")]  private long   _startsAtUnixMs;
        [DataMember(Name = "endsAtUnixMs")]    private long   _endsAtUnixMs;
        [DataMember(Name = "createdBy")]       private string _createdBy;
        [DataMember(Name = "createdAtUnixMs")] private long   _createdAtUnixMs;

        // 운영자가 일찍 내렸을 때만 값이 있다
        [DataMember(Name = "endedAtUnixMs")]   private long?  _endedAtUnixMs;
        [DataMember(Name = "endedBy")]         private string _endedBy;
        [DataMember(Name = "status")]          private string _status;

        /****************************************
        *              Properties
        ****************************************/

        public string NoticeId        => _noticeId;
        public string Title           => _title;
        public string Body            => _body;
        public string Target          => _target;
        public int    Severity        => _severity;
        public long   StartsAtUnixMs  => _startsAtUnixMs;
        public long   EndsAtUnixMs    => _endsAtUnixMs;
        public string CreatedBy       => _createdBy;
        public long   CreatedAtUnixMs => _createdAtUnixMs;
        public long?  EndedAtUnixMs   => _endedAtUnixMs;
        public string EndedBy         => _endedBy;
        public string Status          => _status;

        /****************************************
        *                Methods
        ****************************************/

        public AdminNoticeSummary(string noticeId, string title, string body, string target, int severity,
            long startsAtUnixMs, long endsAtUnixMs, string createdBy, long createdAtUnixMs, long? endedAtUnixMs,
            string endedBy, string status)
        {
            _noticeId        = noticeId;
            _title           = title;
            _body            = body;
            _target          = target;
            _severity        = severity;
            _startsAtUnixMs  = startsAtUnixMs;
            _endsAtUnixMs    = endsAtUnixMs;
            _createdBy       = createdBy;
            _createdAtUnixMs = createdAtUnixMs;
            _endedAtUnixMs   = endedAtUnixMs;
            _endedBy         = endedBy;
            _status          = status;
        }

        private AdminNoticeSummary() { }
    }
}
