// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 클라이언트 경매장 요청을 검증해 경매장 API로 중계하고 결과를 소유자에게 돌려준다, 메인 스레드에서만 부른다 </summary>
    public sealed class MarketRelayServer : IBackendResponseListener
    {
        // 연결마다 1초에 REQUESTS_PER_SECOND번까지 받는다. 쓰기(등록, 구매, 취소, 확인)는 연결당 한 번에 하나만 API로 보낸다.
        // 처리 중인 requestId가 다시 오면 무시하고(결과는 곧 간다), 다른 쓰기가 오면 PendingCommit으로 거절한다.
        // 읽기(검색, 등록 상세, 내 등록, 기록)는 종류마다 하나씩 동시에 진행할 수 있고, 같은 종류가 진행 중이면 PendingCommit이다.
        // 결과를 모르는 쓰기는 BackendUnavailable로 알리고 진행 표를 지운다. 클라이언트가 같은 requestId로 다시 보내면 API가 처음 결과를 돌려준다.
        // 신원은 TownMember에서만 꺼낸다. 연결이 끊기면 진행 표를 지우고 이미 보낸 요청의 응답은 버린다

        /****************************************
        *                Fields
        ****************************************/

        private const int ACTION_SLOTS = 9;

        private readonly BackendGateway               _gateway;
        private readonly ITownAudience                _audience;
        private readonly Func<double>                 _clock;
        private readonly Action<int>                  _walletChanged;
        private readonly Dictionary<int, MemberState> _states;
        private readonly Dictionary<int, int>         _owners;

        /****************************************
        *              Properties
        ****************************************/

        // 응답을 기다리는 API 호출 수
        public int PendingCount => _owners.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gateway가 null이면 모든 요청을 BackendUnavailable로 거절한다. walletChanged는 쓰기가 확정된 참가자 번호를 받는다 </summary>
        public MarketRelayServer(BackendGateway gateway, ITownAudience audience, Func<double> clock, Action<int> walletChanged)
        {
            _gateway       = gateway;
            _audience      = audience ?? throw new ArgumentNullException(nameof(audience));
            _clock         = clock ?? BackendGateway.MonotonicSeconds;
            _walletChanged = walletChanged;
            _states        = new Dictionary<int, MemberState>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _owners        = new Dictionary<int, int>(Constants.Session.MAX_SESSION_PARTICIPANTS);
        }

        public void Handle(TownMember member, in MarketRequestMessage message)
        {
            if (member == null)
            {
                return;
            }

            if (!member.TryAcceptRequest(ETownChannel.Market, _clock()))
            {
                Reply(member.ParticipantId, in message, ERejectReason.RateLimited);
                return;
            }

            if (_gateway == null)
            {
                Reply(member.ParticipantId, in message, ERejectReason.BackendUnavailable);
                return;
            }

            if (!member.HasBackendIdentity)
            {
                Reply(member.ParticipantId, in message, ERejectReason.SessionMismatch);
                return;
            }

            int slot = (int)message.Action;
            if ((slot <= 0) || (slot >= ACTION_SLOTS))
            {
                Reply(member.ParticipantId, in message, ERejectReason.InvalidTarget);
                return;
            }

            MemberState state = GetOrCreate(member);
            if (message.IsWrite)
            {
                HandleWrite(member, state, in message);
                return;
            }

            if (state.ReadRequestIds[slot] != 0)
            {
                Reply(member.ParticipantId, in message, ERejectReason.PendingCommit);
                return;
            }

            ERejectReason reason = MarketRequestRules.TryBuild(in message, member.CharacterId, out BackendRequest request);
            if (reason != ERejectReason.None)
            {
                Reply(member.ParticipantId, in message, reason);
                return;
            }

            int requestId = _gateway.Send(request, this);
            state.ReadRequestIds[slot]   = requestId;
            state.ReadMessages[slot]     = message;
            _owners[requestId]           = member.ParticipantId;
        }

        /// <summary> 마을 연결이 끊겼다, 그 참가자의 진행 표를 지운다. 이미 보낸 요청의 응답은 버린다 </summary>
        public void RemoveMember(int participantId)
        {
            _states.Remove(participantId);
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_owners.TryGetValue(response.RequestId, out int participantId))
            {
                return;
            }

            _owners.Remove(response.RequestId);
            if (!_states.TryGetValue(participantId, out MemberState state))
            {
                return;
            }

            if ((state.HasWrite) && (state.WriteBackendId == response.RequestId))
            {
                OnWriteResponse(state, in response);
                return;
            }

            for (int slot = 1; slot < ACTION_SLOTS; ++slot)
            {
                if (state.ReadRequestIds[slot] == response.RequestId)
                {
                    MarketRequestMessage message = state.ReadMessages[slot];
                    state.ReadRequestIds[slot] = 0;
                    OnReadResponse(state.ParticipantId, in message, in response);
                    return;
                }
            }
        }

        private void HandleWrite(TownMember member, MemberState state, in MarketRequestMessage message)
        {
            // 같은 쓰기의 재전송은 결과가 곧 가므로 조용히 버린다
            if (state.HasWrite)
            {
                if (state.WriteMessage.RequestId != message.RequestId)
                {
                    Reply(member.ParticipantId, in message, ERejectReason.PendingCommit);
                }

                return;
            }

            ERejectReason reason = MarketRequestRules.TryBuild(in message, member.CharacterId, out BackendRequest request);
            if (reason != ERejectReason.None)
            {
                Reply(member.ParticipantId, in message, reason);
                return;
            }

            state.HasWrite       = true;
            state.WriteMessage   = message;
            state.WriteBackendId = _gateway.Send(request, this);
            _owners[state.WriteBackendId] = member.ParticipantId;
        }

        private void OnWriteResponse(MemberState state, in BackendResponse response)
        {
            MarketRequestMessage message = state.WriteMessage;
            state.HasWrite       = false;
            state.WriteBackendId = 0;

            var             payload = response.Payload as MarketOperationResponse;
            OperationResult nested  = payload?.Result;
            bool committed = (response.IsSucceeded) && (nested != null) && (nested.Status == ContractCodes.OperationStatus.COMMITTED);

            // 확인(Ack)은 경제 효과가 없어 본문이 없어도 2xx면 확정으로 본다
            if ((!committed) && (message.Action == EMarketAction.Ack) && (response.IsSucceeded) && (nested == null))
            {
                committed = true;
            }

            if (committed)
            {
                _audience.Send(state.ParticipantId, MarketResultConverter.FromWrite(message.RequestId, message.Action, payload));
                if (message.Action != EMarketAction.Ack)
                {
                    _walletChanged?.Invoke(state.ParticipantId);
                }

                return;
            }

            bool definite = (TownRejectRules.IsDefiniteRejection(in response))
                || ((nested != null) && (nested.Status == ContractCodes.OperationStatus.REJECTED));
            if (!definite)
            {
                // 결과를 모른다. 클라이언트가 같은 requestId로 다시 보내면 효과는 한 번이다
                Reply(state.ParticipantId, in message, ERejectReason.BackendUnavailable);
                return;
            }

            Reply(state.ParticipantId, in message, TownRejectRules.ReasonOf(in response, nested, ERejectReason.InvalidTarget));
        }

        private void OnReadResponse(int participantId, in MarketRequestMessage message, in BackendResponse response)
        {
            if (!response.IsSucceeded)
            {
                ERejectReason reason = TownRejectRules.IsDefiniteRejection(in response)
                    ? TownRejectRules.FromApi(response.ReasonCode, ERejectReason.InvalidTarget)
                    : ERejectReason.BackendUnavailable;
                Reply(participantId, in message, reason);
                return;
            }

            switch (message.Action)
            {
                case EMarketAction.Search when (response.Payload is MarketSearchResponse search):
                    _audience.Send(participantId, MarketResultConverter.FromSearch(message.RequestId, search));
                    return;
                case EMarketAction.Listings when (response.Payload is MarketListingsResponse listings):
                    _audience.Send(participantId, MarketResultConverter.FromListings(message.RequestId, listings));
                    return;
                case EMarketAction.Mine when (response.Payload is MarketMineResponse mine):
                    _audience.Send(participantId, MarketResultConverter.FromMine(message.RequestId, mine));
                    return;
                case EMarketAction.History when (response.Payload is MarketHistoryResponse history):
                    _audience.Send(participantId, MarketResultConverter.FromHistory(message.RequestId, history));
                    return;
                default:
                    Reply(participantId, in message, ERejectReason.BackendUnavailable);
                    return;
            }
        }

        private void Reply(int participantId, in MarketRequestMessage message, ERejectReason reason)
        {
            _audience.Send(participantId, MarketResultMessage.Rejected(message.RequestId, message.Action, reason));
        }

        private MemberState GetOrCreate(TownMember member)
        {
            if (!_states.TryGetValue(member.ParticipantId, out MemberState state))
            {
                state = new MemberState(member.ParticipantId);
                _states.Add(member.ParticipantId, state);
            }

            return state;
        }

        /****************************************
        *               Records
        ****************************************/

        // 참가자 한 명의 경매장 중계 상태. 연결 수명 동안 한 번 만들고 요청마다 고쳐 쓴다
        private sealed class MemberState
        {
            public int                  ParticipantId   { get; }
            public bool                 HasWrite        { get; set; }
            public int                  WriteBackendId  { get; set; }
            public MarketRequestMessage WriteMessage    { get; set; }

            // 읽기는 EMarketAction 번호로 칸을 나눈다
            public int[]                  ReadRequestIds { get; } = new int[ACTION_SLOTS];
            public MarketRequestMessage[] ReadMessages   { get; } = new MarketRequestMessage[ACTION_SLOTS];

            public MemberState(int participantId)
            {
                ParticipantId = participantId;
            }
        }
    }
}
