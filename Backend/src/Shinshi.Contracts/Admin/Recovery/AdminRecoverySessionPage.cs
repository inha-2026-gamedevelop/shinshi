// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 복구 대상(또는 state로 고른) 세션 한 페이지. 생성 시각, sessionId 오름차순이고 nextCursor가 null이면 마지막이다.
    /// livenessDeferred가 true면 API가 막 시작했거나 DB 단절 직후라 서버 소실을 판정하지 않는 중이다. 이때 instanceLost는 모두
    /// false로 내리고, ServiceCancel과 ForceClose는 503으로 거절된다 </summary>
    [DataContract]
    public sealed class AdminRecoverySessionPage
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "sessions")]         private AdminRecoverySession[] _sessions;
        [DataMember(Name = "nextCursor")]       private string                 _nextCursor;
        [DataMember(Name = "livenessDeferred")] private bool                   _livenessDeferred;

        /****************************************
        *              Properties
        ****************************************/

        public AdminRecoverySession[] Sessions         => _sessions;
        public string                 NextCursor       => _nextCursor;
        public bool                   LivenessDeferred => _livenessDeferred;

        /****************************************
        *                Methods
        ****************************************/

        public AdminRecoverySessionPage(AdminRecoverySession[] sessions, string nextCursor)
            : this(sessions, nextCursor, false)
        {
        }

        public AdminRecoverySessionPage(AdminRecoverySession[] sessions, string nextCursor, bool livenessDeferred)
        {
            _sessions         = sessions;
            _nextCursor       = nextCursor;
            _livenessDeferred = livenessDeferred;
        }

        private AdminRecoverySessionPage() { }
    }
}
