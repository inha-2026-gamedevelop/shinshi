// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 행위 캐릭터는 호스트가 인증 결합의 CharacterId에서 꺼내 넘긴다. 관리 요청은 마을 역할만 받고 던전은 스냅샷까지 WrongLocation이다.
    // 쓰기가 확정되면 결과 뒤에 스냅샷을 다시 받아 보낸다. 결과를 모르면(시간 초과, 5xx) BackendUnavailable을 먼저 보내고 스냅샷을 다시
    // 조회한다. 그 조회도 실패하면 (Snapshot, Guid.Empty, BackendUnavailable)을 보내 클라이언트가 쓰기 버튼을 잠그게 한다.
    // 소셜 쓰기는 상태 기준 멱등이라 operationId가 없고 게이트웨이도 다시 보내지 않는다
    /// <summary> 친구, 길드 관리 요청을 검증해 소셜 API로 중계하고 결과와 스냅샷을 소유자에게 돌려준다, 메인 스레드에서만 부른다 </summary>
    public sealed class SocialRelayServer : IBackendResponseListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BackendGateway               _gateway;
        private readonly ISocialAudience              _audience;
        private readonly EServerRole                  _role;
        private readonly Func<double>                 _clock;
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

        /// <summary> gateway가 null이면 마을 요청을 모두 BackendUnavailable로 거절한다 </summary>
        public SocialRelayServer(BackendGateway gateway, ISocialAudience audience, EServerRole role, Func<double> clock)
        {
            if (audience == null)
            {
                throw new ArgumentNullException(nameof(audience));
            }

            _gateway  = gateway;
            _audience = audience;
            _role     = role;
            _clock    = clock ?? BackendGateway.MonotonicSeconds;
            _states   = new Dictionary<int, MemberState>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _owners   = new Dictionary<int, int>(Constants.Session.MAX_SESSION_PARTICIPANTS);
        }

        /// <summary> 요청 한 건. characterId, characterName은 인증 결합의 값이다 </summary>
        public void Handle(int participantId, string characterId, string characterName, in SocialRequestMessage message)
        {
            ERejectReason location = SocialRequestGate.CheckLocation(_role);
            if (location != ERejectReason.None)
            {
                Reply(participantId, message.Action, message.RequestId, location);
                return;
            }

            if (string.IsNullOrEmpty(characterId))
            {
                Reply(participantId, message.Action, message.RequestId, ERejectReason.SessionMismatch);
                return;
            }

            MemberState state = GetOrCreate(participantId, characterId, characterName);
            ESocialGateVerdict verdict = state.Gate.Evaluate(message.Action, message.RequestId, _clock(), out ERejectReason gateReason);
            if (verdict == ESocialGateVerdict.IgnoreDuplicate)
            {
                return;
            }

            if (verdict == ESocialGateVerdict.Reject)
            {
                Reply(participantId, message.Action, message.RequestId, gateReason);
                return;
            }

            if (_gateway == null)
            {
                Reply(participantId, message.Action, message.RequestId, ERejectReason.BackendUnavailable);
                return;
            }

            if (!SocialRequestGate.IsWrite(message.Action))
            {
                RequestSnapshot(state);
                return;
            }

            ERejectReason formatReason = SocialRequestRules.TryBuild(in message, state.CharacterId, state.CharacterName, out string path,
                out object body);
            if (formatReason != ERejectReason.None)
            {
                Reply(participantId, message.Action, message.RequestId, formatReason);
                return;
            }

            BackendRequest request = (body == null)
                ? BackendRequest.PostWithoutBody(path, typeof(SocialActionResponse), null, Constants.Network.API_TIMEOUT_SECONDS)
                : BackendRequest.Post(path, body, typeof(SocialActionResponse), null, Constants.Network.API_TIMEOUT_SECONDS);
            state.Gate.BeginWrite(message.RequestId);
            state.WriteAction    = message.Action;
            state.WriteRequestId = _gateway.Send(request, this);
            _owners[state.WriteRequestId] = participantId;
        }

        /// <summary> 연결이 끊겼다, 그 참가자의 대기 상태를 지운다. 이미 보낸 요청의 응답은 버린다 </summary>
        public void RemoveMember(int participantId)
        {
            _states.Remove(participantId);
        }

        /// <summary> 이 참가자가 결과를 기다리는 쓰기가 있으면 참 </summary>
        public bool HasPendingWrite(int participantId)
        {
            return (_states.TryGetValue(participantId, out MemberState state)) && (state.Gate.HasInFlight);
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

            if (state.SnapshotRequestId == response.RequestId)
            {
                OnSnapshotResponse(state, in response);
                return;
            }

            if (state.WriteRequestId == response.RequestId)
            {
                OnWriteResponse(state, in response);
            }
        }

        // 이미 조회 중이면 끝난 뒤 한 번 더 받는다. 확정 직후의 변경이 앞선 조회 결과에 빠져 있을 수 있다
        private void RequestSnapshot(MemberState state)
        {
            if (state.SnapshotRequestId != 0)
            {
                state.SnapshotAgain = true;
                return;
            }

            state.SnapshotAgain = false;
            BackendRequest request = BackendRequest.PostWithoutBody(SocialRequestRules.SnapshotPath(state.CharacterId),
                typeof(SocialSnapshotResponse), null, Constants.Network.API_TIMEOUT_SECONDS);
            state.SnapshotRequestId = _gateway.Send(request, this);
            _owners[state.SnapshotRequestId] = state.ParticipantId;
        }

        private void OnSnapshotResponse(MemberState state, in BackendResponse response)
        {
            state.SnapshotRequestId = 0;
            if ((response.IsSucceeded) && (response.Payload is SocialSnapshotResponse snapshot))
            {
                _audience.Send(state.ParticipantId, SocialSnapshotConverter.ToMessage(snapshot));
                if (state.SnapshotAgain)
                {
                    RequestSnapshot(state);
                }

                return;
            }

            // 조회 실패는 결과 불명 뒤의 재조회든 창이 연 조회든 클라이언트가 같은 안내(목록을 받지 못함)를 띄운다
            state.SnapshotAgain = false;
            SocialApiRules.Classify(in response, null, out ERejectReason reason);
            if ((reason == ERejectReason.None) || (reason == ERejectReason.RateLimited))
            {
                reason = ERejectReason.BackendUnavailable;
            }

            Reply(state.ParticipantId, ESocialAction.Snapshot, Guid.Empty, reason);
        }

        private void OnWriteResponse(MemberState state, in BackendResponse response)
        {
            Guid          requestId = state.Gate.InFlightRequestId;
            ESocialAction action    = state.WriteAction;
            state.WriteRequestId = 0;
            state.Gate.EndWrite();

            var payload = response.Payload as SocialActionResponse;
            ESocialApiVerdict verdict = SocialApiRules.Classify(in response, payload, out ERejectReason reason);
            switch (verdict)
            {
                case ESocialApiVerdict.Committed:
                    Reply(state.ParticipantId, action, requestId, ERejectReason.None);
                    RequestSnapshot(state);
                    break;
                case ESocialApiVerdict.Unknown:
                    Reply(state.ParticipantId, action, requestId, ERejectReason.BackendUnavailable);
                    RequestSnapshot(state);
                    break;
                case ESocialApiVerdict.NotSent:
                    Reply(state.ParticipantId, action, requestId, ERejectReason.BackendUnavailable);
                    break;
                default:
                    Reply(state.ParticipantId, action, requestId, reason);
                    break;
            }
        }

        private void Reply(int participantId, ESocialAction action, Guid requestId, ERejectReason reason)
        {
            _audience.Send(participantId, new SocialResultMessage(action, requestId, reason));
        }

        private MemberState GetOrCreate(int participantId, string characterId, string characterName)
        {
            if (!_states.TryGetValue(participantId, out MemberState state))
            {
                state = new MemberState(participantId, characterId, characterName);
                _states.Add(participantId, state);
            }

            return state;
        }

        /****************************************
        *               Records
        ****************************************/

        // 참가자 한 명의 중계 상태. 연결 수명 동안 한 번 만들고 요청마다 고쳐 쓴다
        private sealed class MemberState
        {
            public int               ParticipantId     { get; }
            public string            CharacterId       { get; }
            public string            CharacterName     { get; }
            public SocialRequestGate Gate              { get; } = new SocialRequestGate();
            public ESocialAction     WriteAction       { get; set; }
            public int               WriteRequestId    { get; set; }
            public int               SnapshotRequestId { get; set; }
            public bool              SnapshotAgain     { get; set; }

            public MemberState(int participantId, string characterId, string characterName)
            {
                ParticipantId = participantId;
                CharacterId   = characterId;
                CharacterName = characterName;
            }
        }
    }
}
