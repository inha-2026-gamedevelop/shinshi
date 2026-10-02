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

        [DataMember(Name = "noticeId")]       private string _noticeId;
        [DataMember(Name = "title")]          private string _title;
        [DataMember(Name = "body")]           private string _body;
        [DataMember(Name = "target")]         private string _target;
        [DataMember(Name = "severity")]       private int    _severity;
        [DataMember(Name = "startsAtUnixMs")] private long   _startsAtUnixMs;
        [DataMember(Name = "endsAtUnixMs")]   private long   _endsAtUnixMs;

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

        /****************************************
        *                Methods
        ****************************************/

        public ServiceNotice(string noticeId, string title, string body, string target, int severity,
            long startsAtUnixMs, long endsAtUnixMs)
        {
            _noticeId       = noticeId;
            _title          = title;
            _body           = body;
            _target         = target;
            _severity       = severity;
            _startsAtUnixMs = startsAtUnixMs;
            _endsAtUnixMs   = endsAtUnixMs;
        }

        private ServiceNotice() { }
    }
}
