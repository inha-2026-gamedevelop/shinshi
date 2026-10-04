// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 행동력 회복은 API가 DB 시각으로 계산하므로 마을은 값을 들고 있지 않고 요청 때마다 조회해 그대로 보낸다.
    // 참가자마다 조회는 한 번에 하나만 보내고, 기다리는 중에 온 요청은 같은 응답으로 대신한다
    /// <summary> 계정 생활 재고(행동력, 약초, 물약)를 조회해 소유자에게 보낸다, 메인 스레드에서만 부른다 </summary>
    public sealed class LifeSnapshotRelay : IBackendResponseListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BackendGateway       _gateway;
        private readonly ITownAudience        _audience;
        private readonly Dictionary<int, int> _pendingByMember;
        private readonly Dictionary<int, int> _memberByRequest;

        /****************************************
        *                Methods
        ****************************************/

        public LifeSnapshotRelay(BackendGateway gateway, ITownAudience audience)
        {
            _gateway         = gateway;
            _audience        = audience ?? throw new ArgumentNullException(nameof(audience));
            _pendingByMember = new Dictionary<int, int>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _memberByRequest = new Dictionary<int, int>(Constants.Session.MAX_SESSION_PARTICIPANTS);
        }

        /// <summary> 조회를 보낸다, 보낼 수 없으면 거절 사유 </summary>
        public ERejectReason Request(TownMember member)
        {
            if (member == null)
            {
                return ERejectReason.InvalidTarget;
            }

            if (_gateway == null)
            {
                return ERejectReason.BackendUnavailable;
            }

            if (!member.HasBackendIdentity)
            {
                return ERejectReason.SessionMismatch;
            }

            if (_pendingByMember.ContainsKey(member.ParticipantId))
            {
                return ERejectReason.None;
            }

            BackendRequest request = BackendRequest.Post(LifeRoutes.SNAPSHOT,
                new LifeSnapshotRequest(member.AccountId, member.AccountLeaseId), typeof(LifeSnapshotResponse), null,
                Constants.Network.API_TIMEOUT_SECONDS);
            int requestId = _gateway.Send(request, this);
            _pendingByMember[member.ParticipantId] = requestId;
            _memberByRequest[requestId]            = member.ParticipantId;
            return ERejectReason.None;
        }

        public void RemoveMember(int participantId)
        {
            _pendingByMember.Remove(participantId);
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_memberByRequest.TryGetValue(response.RequestId, out int participantId))
            {
                return;
            }

            _memberByRequest.Remove(response.RequestId);
            if ((!_pendingByMember.TryGetValue(participantId, out int pending)) || (pending != response.RequestId))
            {
                return;
            }

            _pendingByMember.Remove(participantId);
            if ((response.IsSucceeded) && (response.Payload is LifeSnapshotResponse snapshot))
            {
                _audience.Send(participantId, TownSnapshotConverter.ToMessage(snapshot));
                return;
            }

            ERejectReason reason = TownRejectRules.IsDefiniteRejection(in response)
                ? TownRejectRules.FromApi(response.ReasonCode, ERejectReason.SessionMismatch)
                : ERejectReason.BackendUnavailable;
            _audience.Send(participantId, new LifeResultMessage(Guid.Empty, ELifeAction.Snapshot, reason, 0, 0));
        }
    }
}
