// System
using System;
using System.Collections.Generic;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 계정은 연결 결합(TownMember)에서만 정하고 클라이언트 값은 operationId뿐이다. API는 그 계정의 접속 임대가 이 마을에 있는지
    // 다시 확인하고, 멱등 actor가 계정이라 재접속 뒤 같은 operationId 재전송은 처음 결과를 받는다. 참가자마다 하나만 보낸다
    /// <summary> 신시 안내 완료 기록을 계정 진행 API로 중계한다, 메인 스레드에서만 부른다 </summary>
    public sealed class IntroProgressRelay : IBackendResponseListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string GUID_FORMAT = "D";

        private readonly BackendGateway                 _gateway;
        private readonly ITownAudience                  _audience;
        private readonly Dictionary<int, Guid>          _inFlightByMember;
        private readonly Dictionary<int, PendingIntro>  _pendingByRequest;

        /****************************************
        *              Properties
        ****************************************/

        // 응답을 기다리는 API 호출 수
        public int PendingCount => _pendingByRequest.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gateway가 null이면 모든 요청을 BackendUnavailable로 거절한다 </summary>
        public IntroProgressRelay(BackendGateway gateway, ITownAudience audience)
        {
            if (audience == null)
            {
                throw new ArgumentNullException(nameof(audience), "안내 완료 중계에 필요한 참조가 없다");
            }

            _gateway          = gateway;
            _audience         = audience;
            _inFlightByMember = new Dictionary<int, Guid>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _pendingByRequest = new Dictionary<int, PendingIntro>(Constants.Session.MAX_SESSION_PARTICIPANTS);
        }

        /// <summary> 기록을 보낸다. None이면 결과가 IntroCompleteResultMessage로 따로 간다 </summary>
        public ERejectReason Request(TownMember member, in IntroCompleteRequestMessage message)
        {
            if (member == null)
            {
                return ERejectReason.InvalidTarget;
            }

            if (_gateway == null)
            {
                return ERejectReason.BackendUnavailable;
            }

            if ((!member.HasBackendIdentity) || (!Guid.TryParse(member.AccountId, out Guid accountId)))
            {
                return ERejectReason.SessionMismatch;
            }

            if (message.OperationId == Guid.Empty)
            {
                return ERejectReason.InvalidTarget;
            }

            if (_inFlightByMember.TryGetValue(member.ParticipantId, out Guid inFlight))
            {
                return (inFlight == message.OperationId) ? ERejectReason.None : ERejectReason.PendingCommit;
            }

            string operationId = message.OperationId.ToString(GUID_FORMAT);
            string path        = string.Format(CultureInfo.InvariantCulture, GrowthRoutes.INTRO_COMPLETE_FORMAT,
                accountId.ToString(GUID_FORMAT));
            BackendRequest request = BackendRequest.Post(path, new IntroCompleteRequest(operationId), typeof(OperationResult),
                operationId, Constants.Network.API_TIMEOUT_SECONDS);
            int requestId = _gateway.Send(request, this);
            _inFlightByMember[member.ParticipantId] = message.OperationId;
            _pendingByRequest[requestId]            = new PendingIntro(member.ParticipantId, message.OperationId);
            return ERejectReason.None;
        }

        /// <summary> 마을 연결이 끊겼다, 이미 보낸 요청의 응답은 버린다 </summary>
        public void RemoveMember(int participantId)
        {
            _inFlightByMember.Remove(participantId);
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_pendingByRequest.TryGetValue(response.RequestId, out PendingIntro pending))
            {
                return;
            }

            _pendingByRequest.Remove(response.RequestId);
            if ((!_inFlightByMember.TryGetValue(pending.ParticipantId, out Guid inFlight)) || (inFlight != pending.OperationId))
            {
                return;
            }

            _inFlightByMember.Remove(pending.ParticipantId);
            var result = response.Payload as OperationResult;
            ERejectReason reason;
            if ((response.IsSucceeded) && (result != null) && (result.Status == ContractCodes.OperationStatus.COMMITTED))
            {
                reason = ERejectReason.None;
            }
            else if ((TownRejectRules.IsDefiniteRejection(in response))
                || ((result != null) && (result.Status == ContractCodes.OperationStatus.REJECTED)))
            {
                reason = TownRejectRules.ReasonOf(in response, result, ERejectReason.SessionMismatch);
            }
            else
            {
                // 결과를 모른다. 같은 operationId로 다시 보내면 API가 처음 결과를 돌려준다
                reason = ERejectReason.BackendUnavailable;
            }

            _audience.Send(pending.ParticipantId, new IntroCompleteResultMessage(pending.OperationId, reason));
        }

        /****************************************
        *               Records
        ****************************************/

        private readonly struct PendingIntro
        {
            private readonly int  _participantId;
            private readonly Guid _operationId;

            public int  ParticipantId => _participantId;
            public Guid OperationId   => _operationId;

            public PendingIntro(int participantId, Guid operationId)
            {
                _participantId = participantId;
                _operationId   = operationId;
            }
        }
    }
}
