// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 공지 작성(Operator). 성공 결과의 receiptId가 새 noticeId다 </summary>
    [DataContract]
    public sealed class AdminNoticeCreateRequest : IAdminWriteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // service_notices CHECK와 같은 한도
        public const int MAX_TITLE_LENGTH = 100;
        public const int MAX_BODY_LENGTH  = 4000;

        [DataMember(Name = "operationId")]    private string _operationId;
        [DataMember(Name = "reason")]         private string _reason;
        [DataMember(Name = "title")]          private string _title;
        [DataMember(Name = "body")]           private string _body;

        // ServiceNotice.TARGET_LAUNCHER, TARGET_GAME, TARGET_BOTH
        [DataMember(Name = "target")]         private string _target;

        // ServiceNotice.SEVERITY_INFO ~ SEVERITY_CRITICAL
        [DataMember(Name = "severity")]       private int    _severity;

        // null이면 저장 시각부터 노출한다. 끝 시각은 시작과 현재 시각보다 뒤여야 한다
        [DataMember(Name = "startsAtUnixMs")] private long?  _startsAtUnixMs;
        [DataMember(Name = "endsAtUnixMs")]   private long?  _endsAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId    => _operationId;
        public string Reason         => _reason;
        public string Title          => _title;
        public string Body           => _body;
        public string Target         => _target;
        public int    Severity       => _severity;
        public long?  StartsAtUnixMs => _startsAtUnixMs;
        public long?  EndsAtUnixMs   => _endsAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminNoticeCreateRequest(string operationId, string reason, string title, string body, string target,
            int severity, long? startsAtUnixMs, long? endsAtUnixMs)
        {
            _operationId    = operationId;
            _reason         = reason;
            _title          = title;
            _body           = body;
            _target         = target;
            _severity       = severity;
            _startsAtUnixMs = startsAtUnixMs;
            _endsAtUnixMs   = endsAtUnixMs;
        }

        private AdminNoticeCreateRequest() { }
    }
}
