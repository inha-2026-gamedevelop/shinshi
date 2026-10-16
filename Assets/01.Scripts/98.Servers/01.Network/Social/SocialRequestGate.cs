// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 쓰기는 연결당 동시에 1건만 API로 보낸다. 같은 캐릭터의 작업을 동시에 보내면 순서가 뒤섞이고 결과 불명 처리가 겹친다.
    // 같은 RequestId 재전송 검사를 속도 제한보다 먼저 한다. 재전송에 RateLimited로 답하면 클라이언트가 처리 중인 요청의 잠금을
    // 먼저 풀어 버린다. 스냅샷은 관리 창과 따로 2초 1건이다
    /// <summary> 연결 하나의 소셜 관리 요청 속도 제한과 쓰기 동시 1건 규칙, 엔진과 네트워크 상태를 갖지 않는다 </summary>
    public sealed class SocialRequestGate
    {
        /****************************************
        *                Fields
        ****************************************/

        private TownRequestThrottle _manage;
        private TownRequestThrottle _snapshot;
        private Guid                _inFlight = Guid.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public bool HasInFlight      => _inFlight != Guid.Empty;
        public Guid InFlightRequestId => _inFlight;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 관리 요청은 마을 역할만 받는다. 던전은 스냅샷까지 WrongLocation이다 </summary>
        public static ERejectReason CheckLocation(EServerRole role)
        {
            return (role == EServerRole.Town) ? ERejectReason.None : ERejectReason.WrongLocation;
        }

        /// <summary> 스냅샷만 읽기, 나머지는 API 상태를 바꾸는 쓰기다 </summary>
        public static bool IsWrite(ESocialAction action)
        {
            return action != ESocialAction.Snapshot;
        }

        /// <summary> 요청 한 건을 판정한다. Accept인 쓰기는 BeginWrite로 처리 중 표시를 한다 </summary>
        public ESocialGateVerdict Evaluate(ESocialAction action, Guid requestId, double now, out ERejectReason reason)
        {
            reason = ERejectReason.None;
            bool write = IsWrite(action);
            if ((write) && (_inFlight != Guid.Empty) && (requestId == _inFlight))
            {
                return ESocialGateVerdict.IgnoreDuplicate;
            }

            bool admitted = write
                ? _manage.TryAccept(now, Constants.Social.REQUESTS_PER_WINDOW, Constants.Social.REQUEST_WINDOW_SECONDS)
                : _snapshot.TryAccept(now, Constants.Social.SNAPSHOT_REQUESTS_PER_WINDOW, Constants.Social.SNAPSHOT_REQUEST_WINDOW_SECONDS);
            if (!admitted)
            {
                reason = ERejectReason.RateLimited;
                return ESocialGateVerdict.Reject;
            }

            if (!write)
            {
                return ESocialGateVerdict.Accept;
            }

            if (_inFlight != Guid.Empty)
            {
                reason = ERejectReason.PendingCommit;
                return ESocialGateVerdict.Reject;
            }

            if (requestId == Guid.Empty)
            {
                reason = ERejectReason.InvalidTarget;
                return ESocialGateVerdict.Reject;
            }

            return ESocialGateVerdict.Accept;
        }

        public void BeginWrite(Guid requestId)
        {
            _inFlight = requestId;
        }

        public void EndWrite()
        {
            _inFlight = Guid.Empty;
        }
    }
}
