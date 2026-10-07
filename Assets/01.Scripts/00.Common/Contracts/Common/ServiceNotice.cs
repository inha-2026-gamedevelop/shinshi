// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 서비스 상태에 실리는 노출 중인 공지 한 건. 런처와 마을은 target으로 자기 대상만 골라 보여 준다 </summary>
    [DataContract]
    public sealed class ServiceNotice
    {
        /****************************************
        *                Fields
        ****************************************/

        // target 값. 어드민 공지 생성이 같은 문자열을 저장한다
        public const string TARGET_LAUNCHER = "Launcher";
        public const string TARGET_GAME     = "Game";
        public const string TARGET_BOTH     = "Both";

        // severity 값. 클수록 먼저 보여 준다
        public const int SEVERITY_INFO     = 0;
        public const int SEVERITY_WARNING  = 1;
        public const int SEVERITY_CRITICAL = 2;

        // category 값. 런처 소식 탭의 분류이며 Update만 패치노트 탭에 나온다. 값이 없으면 Notice로 본다
        public const string CATEGORY_NOTICE      = "Notice";
        public const string CATEGORY_UPDATE      = "Update";
        public const string CATEGORY_EVENT       = "Event";
        public const string CATEGORY_MAINTENANCE = "Maintenance";

        [DataMember(Name = "noticeId")]       private string _noticeId;
        [DataMember(Name = "title")]          private string _title;
        [DataMember(Name = "body")]           private string _body;
        [DataMember(Name = "target")]         private string _target;
        [DataMember(Name = "severity")]       private int    _severity;
        [DataMember(Name = "startsAtUnixMs")] private long   _startsAtUnixMs;
        [DataMember(Name = "endsAtUnixMs")]   private long   _endsAtUnixMs;
        [DataMember(Name = "category")]       private string _category;

        /****************************************
        *              Properties
        ****************************************/

        public string NoticeId       => _noticeId;
        public string Title          => _title;
        public string Body           => _body;
        public string Target         => _target;
        public int    Severity       => _severity;
        public long   StartsAtUnixMs => _startsAtUnixMs;
        public long   EndsAtUnixMs   => _endsAtUnixMs;

        // 이전 서버가 보낸 응답에는 키가 없어 비어 있을 수 있다
        public string Category       => string.IsNullOrEmpty(_category) ? CATEGORY_NOTICE : _category;

        /****************************************
        *                Methods
        ****************************************/

        public ServiceNotice(string noticeId, string title, string body, string target, int severity,
            long startsAtUnixMs, long endsAtUnixMs)
            : this(noticeId, title, body, target, severity, startsAtUnixMs, endsAtUnixMs, CATEGORY_NOTICE)
        {
        }

        public ServiceNotice(string noticeId, string title, string body, string target, int severity,
            long startsAtUnixMs, long endsAtUnixMs, string category)
        {
            _noticeId       = noticeId;
            _title          = title;
            _body           = body;
            _target         = target;
            _severity       = severity;
            _startsAtUnixMs = startsAtUnixMs;
            _endsAtUnixMs   = endsAtUnixMs;
            _category       = category;
        }

        private ServiceNotice() { }
    }
}
