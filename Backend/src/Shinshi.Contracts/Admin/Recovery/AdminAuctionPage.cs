// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 경매 단계 출정 한 페이지. state가 없으면 멈춘 출정(overdue 또는 서버 소실)만, state가 Open이나 Closing이면 그 상태의
    /// 진행 중 품목이 있는 모든 경매 단계 출정이다. 경매 단계 기한, sessionId 오름차순이고 nextCursor가 null이면 마지막이다 </summary>
    [DataContract]
    public sealed class AdminAuctionPage
    {
        /****************************************
        *                Fields
        ****************************************/

        // GET /admin/v1/auctions의 state 값
        public const string STATE_OPEN    = "Open";
        public const string STATE_CLOSING = "Closing";

        [DataMember(Name = "sessions")]         private AdminAuctionSession[] _sessions;
        [DataMember(Name = "nextCursor")]       private string                _nextCursor;
        [DataMember(Name = "livenessDeferred")] private bool                  _livenessDeferred;

        /****************************************
        *              Properties
        ****************************************/

        public AdminAuctionSession[] Sessions         => _sessions;
        public string                NextCursor       => _nextCursor;
        public bool                  LivenessDeferred => _livenessDeferred;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAuctionPage(AdminAuctionSession[] sessions, string nextCursor, bool livenessDeferred)
        {
            _sessions         = sessions;
            _nextCursor       = nextCursor;
            _livenessDeferred = livenessDeferred;
        }

        private AdminAuctionPage() { }
    }
}
