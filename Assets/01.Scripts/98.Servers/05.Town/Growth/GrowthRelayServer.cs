// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 클라이언트 성장 요청을 검증해 성장 API로 중계하고 결과와 성장 상태를 소유자에게 돌려준다, 메인 스레드에서만 부른다 </summary>
    public sealed class GrowthRelayServer : IBackendResponseListener
    {
        // 연결마다 1초에 REQUESTS_PER_SECOND번까지 받고, 쓰기 작업은 한 번에 하나만 API로 보낸다. 같은 캐릭터의 작업을
        // 동시에 보내면 뒤 작업이 STALE_REVISION으로 거절되기만 한다. 처리 중인 operationId가 다시 오면 무시하고(결과는 곧 간다),
        // 다른 작업이 오면 PendingCommit으로 거절한다.
        // 확률 판정 값은 서버가 뽑아 operationId별로 기억한다. 응답을 잃은 클라이언트가 같은 operationId로 다시 보내면 같은 값을
        // 쓴다(API도 재전송에는 처음 결과를 돌려준다). 기대 revision은 서버가 받은 마지막 성장 상태 값이고, 아직 모르면
        // 조회를 먼저 보낸 뒤 작업을 보낸다. 같은 operationId 재전송은 처음 보낸 revision을 다시 쓴다(API 멱등 actor는 계정이고
        // 해시에 revision을 넣지 않으므로 다른 마을로 옮겨 revision이 달라져도 저장 결과를 받는다). 작업이 확정되면 파티 준비를 풀도록 characterChanged를 부른다

        /****************************************
        *                Fields
        ****************************************/

        private const string OPERATION_ID_FORMAT = "D";

        private readonly BackendGateway                 _gateway;
        private readonly ITownAudience                  _audience;
        private readonly Func<int>                      _roll;
        private readonly Func<double>                   _clock;
        private readonly Action<int>                    _characterChanged;
        private readonly Dictionary<int, MemberState>   _states;
        private readonly Dictionary<int, int>           _owners;
        private readonly int                            _trainingTotalCap;

        /****************************************
        *              Properties
        ****************************************/

        // 응답을 기다리는 API 호출 수
        public int PendingCount => _owners.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gateway가 null이면 모든 요청을 BackendUnavailable로 거절한다. roll은 0 이상 BASIS_POINTS 미만을 돌려준다.
        /// trainingTotalCap은 GrowthConfigSO.TrainingTotalCap이며 한 번에 이보다 많은 수련 단계는 API로 보내지 않는다 </summary>
        public GrowthRelayServer(BackendGateway gateway, ITownAudience audience, Func<int> roll, Func<double> clock,
            Action<int> characterChanged, int trainingTotalCap)
        {
            if (trainingTotalCap <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trainingTotalCap), "수련 총 상한은 1 이상이다");
            }

            if ((audience == null) || (roll == null))
            {
                throw new ArgumentNullException(nameof(audience), "성장 중계에 필요한 참조가 없다");
            }

            _gateway          = gateway;
            _audience         = audience;
            _roll             = roll;
            _clock            = clock ?? BackendGateway.MonotonicSeconds;
            _characterChanged = characterChanged;
            _states           = new Dictionary<int, MemberState>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _owners           = new Dictionary<int, int>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _trainingTotalCap = trainingTotalCap;
        }

        public void Handle(TownMember member, in GrowthRequestMessage message)
        {
            if (member == null)
            {
                return;
            }

            if (!member.TryAcceptRequest(ETownChannel.Growth, _clock()))
            {
                Reply(member.ParticipantId, in message, ERejectReason.RateLimited);
                return;
            }

            if (_gateway == null)
            {
                Reply(member.ParticipantId, in message, ERejectReason.BackendUnavailable);
                return;
            }

            if (string.IsNullOrEmpty(member.CharacterId))
            {
                Reply(member.ParticipantId, in message, ERejectReason.SessionMismatch);
                return;
            }

            MemberState state = GetOrCreate(member);
            if (message.Action == EGrowthAction.Snapshot)
            {
                if (state.SnapshotRequestId == 0)
                {
                    RequestSnapshot(state);
                }

                return;
            }

            if (message.OperationId == Guid.Empty)
            {
                Reply(member.ParticipantId, in message, ERejectReason.InvalidTarget);
                return;
            }

            // 같은 작업의 재전송은 결과가 곧 가므로 조용히 버린다
            if (state.HasInFlight)
            {
                if (state.InFlight.OperationId != message.OperationId)
                {
                    Reply(member.ParticipantId, in message, ERejectReason.PendingCommit);
                }

                return;
            }

            int roll = GrowthRequestRules.UsesRoll(message.Action) ? state.RollFor(message.OperationId, _roll) : 0;
            ERejectReason formatReason = GrowthRequestRules.TryBuild(in message, member.CharacterId, 0, roll,
                _trainingTotalCap, out _, out _);
            if (formatReason != ERejectReason.None)
            {
                Reply(member.ParticipantId, in message, formatReason);
                return;
            }

            state.InFlight    = message;
            state.HasInFlight = true;
            state.Roll        = roll;
            if (!state.HasRevision)
            {
                if (state.SnapshotRequestId == 0)
                {
                    RequestSnapshot(state);
                }

                return;
            }

            SendOperation(state);
        }

        /// <summary> 서버가 직접 성장 스냅숏을 조회한다, 속도 제한을 세지 않는다. 이미 조회 중이거나 연결이 없으면 무시한다 </summary>
        public void RequestSnapshot(TownMember member)
        {
            if ((member == null) || (_gateway == null) || (string.IsNullOrEmpty(member.CharacterId)))
            {
                return;
            }

            MemberState state = GetOrCreate(member);
            if (state.SnapshotRequestId == 0)
            {
                RequestSnapshot(state);
            }
        }

        /// <summary> 마을 연결이 끊겼다, 그 참가자의 대기 상태를 지운다. 이미 보낸 요청의 응답은 버린다 </summary>
        public void RemoveMember(int participantId)
        {
            _states.Remove(participantId);
        }

        /// <summary> 이 참가자가 operationId로 처음 보낸 기대 revision, 없으면 -1 </summary>
        public long RememberedRevision(int participantId, Guid operationId)
        {
            return (_states.TryGetValue(participantId, out MemberState state)) ? state.FindRevision(operationId) : -1;
        }

        /// <summary> 이 참가자가 operationId에 기억한 확률 판정 값, 없으면 -1 </summary>
        public int RememberedRoll(int participantId, Guid operationId)
        {
            return (_states.TryGetValue(participantId, out MemberState state)) ? state.FindRoll(operationId) : -1;
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

            if (state.OperationRequestId == response.RequestId)
            {
                OnOperationResponse(state, in response);
            }
        }

        private void RequestSnapshot(MemberState state)
        {
            BackendRequest request = BackendRequest.Get(GrowthRequestRules.SnapshotPath(state.CharacterId),
                typeof(CharacterGrowthSnapshot), null, Constants.Network.API_TIMEOUT_SECONDS);
            state.SnapshotRequestId = _gateway.Send(request, this);
            _owners[state.SnapshotRequestId] = state.ParticipantId;
        }

        private void SendOperation(MemberState state)
        {
            GrowthRequestMessage message = state.InFlight;
            long revision = state.RevisionFor(message.OperationId, state.Revision);
            ERejectReason reason = GrowthRequestRules.TryBuild(in message, state.CharacterId, revision, state.Roll,
                _trainingTotalCap, out string path, out object body);
            if (reason != ERejectReason.None)
            {
                state.HasInFlight = false;
                Reply(state.ParticipantId, in message, reason);
                return;
            }

            BackendRequest request = BackendRequest.Post(path, body, typeof(GrowthOperationResponse),
                message.OperationId.ToString(OPERATION_ID_FORMAT), Constants.Network.API_TIMEOUT_SECONDS);
            state.OperationRequestId = _gateway.Send(request, this);
            _owners[state.OperationRequestId] = state.ParticipantId;
        }

        private void OnSnapshotResponse(MemberState state, in BackendResponse response)
        {
            state.SnapshotRequestId = 0;
            if ((response.IsSucceeded) && (response.Payload is CharacterGrowthSnapshot snapshot))
            {
                ApplySnapshot(state, snapshot);
                if ((state.HasInFlight) && (state.OperationRequestId == 0))
                {
                    SendOperation(state);
                }

                return;
            }

            // 조회가 실패하면 기대 revision을 몰라 작업을 보낼 수 없다. 기다리던 작업을 실패로 돌려준다
            ERejectReason reason = TownRejectRules.IsDefiniteRejection(in response)
                ? TownRejectRules.FromApi(response.ReasonCode, ERejectReason.SessionMismatch)
                : ERejectReason.BackendUnavailable;
            if ((state.HasInFlight) && (state.OperationRequestId == 0))
            {
                GrowthRequestMessage message = state.InFlight;
                state.HasInFlight = false;
                Reply(state.ParticipantId, in message, reason);
                return;
            }

            _audience.Send(state.ParticipantId, new GrowthResultMessage(Guid.Empty, EGrowthAction.Snapshot, reason,
                GrowthCodes.Outcome.NONE, null));
        }

        private void OnOperationResponse(MemberState state, in BackendResponse response)
        {
            GrowthRequestMessage message = state.InFlight;
            state.OperationRequestId = 0;
            state.HasInFlight        = false;

            var             payload = response.Payload as GrowthOperationResponse;
            OperationResult nested  = payload?.Result;
            if ((response.IsSucceeded) && (nested != null) && (nested.Status == ContractCodes.OperationStatus.COMMITTED))
            {
                if (payload.Growth != null)
                {
                    ApplySnapshot(state, payload.Growth);
                }

                _audience.Send(state.ParticipantId, new GrowthResultMessage(message.OperationId, message.Action, ERejectReason.None,
                    payload.Outcome, payload.ItemId));
                _characterChanged?.Invoke(state.ParticipantId);
                return;
            }

            bool definite = (TownRejectRules.IsDefiniteRejection(in response))
                || ((nested != null) && (nested.Status == ContractCodes.OperationStatus.REJECTED));
            if (!definite)
            {
                // 결과를 모른다. 확률 판정 값은 기억해 두었으니 클라이언트가 같은 operationId로 다시 보내면 된다
                Reply(state.ParticipantId, in message, ERejectReason.BackendUnavailable);
                return;
            }

            ERejectReason reason = TownRejectRules.ReasonOf(in response, nested, ERejectReason.InvalidTarget);
            if ((payload != null) && (payload.Growth != null))
            {
                ApplySnapshot(state, payload.Growth);
            }
            else if (reason == ERejectReason.StaleRevision)
            {
                // 다른 경로(정산, 다른 작업)로 revision이 바뀌었다. 새 상태를 받아 다음 요청에 쓴다
                state.HasRevision = false;
                RequestSnapshot(state);
            }

            Reply(state.ParticipantId, in message, reason);
        }

        private void ApplySnapshot(MemberState state, CharacterGrowthSnapshot snapshot)
        {
            state.Revision    = snapshot.LoadoutRevision;
            state.HasRevision = true;
            _audience.Send(state.ParticipantId, TownSnapshotConverter.ToMessage(snapshot));
        }

        private void Reply(int participantId, in GrowthRequestMessage message, ERejectReason reason)
        {
            _audience.Send(participantId, new GrowthResultMessage(message.OperationId, message.Action, reason,
                GrowthCodes.Outcome.NONE, null));
        }

        private MemberState GetOrCreate(TownMember member)
        {
            if (!_states.TryGetValue(member.ParticipantId, out MemberState state))
            {
                state = new MemberState(member.ParticipantId, member.CharacterId);
                _states.Add(member.ParticipantId, state);
            }

            return state;
        }

        /****************************************
        *               Records
        ****************************************/

        // 참가자 한 명의 성장 중계 상태. 연결 수명 동안 한 번 만들고 요청마다 고쳐 쓴다
        private sealed class MemberState
        {
            private readonly Guid[] _rollIds    = new Guid[Constants.Town.GROWTH_ROLL_MEMORY];
            private readonly int[]  _rollValues = new int[Constants.Town.GROWTH_ROLL_MEMORY];

            private readonly Guid[] _revisionIds    = new Guid[Constants.Town.GROWTH_ROLL_MEMORY];
            private readonly long[] _revisionValues = new long[Constants.Town.GROWTH_ROLL_MEMORY];

            private int _rollNext     = 0;
            private int _revisionNext = 0;

            public int                  ParticipantId      { get; }
            public string               CharacterId        { get; }
            public long                 Revision           { get; set; }
            public bool                 HasRevision        { get; set; }
            public int                  SnapshotRequestId  { get; set; }
            public int                  OperationRequestId { get; set; }
            public GrowthRequestMessage InFlight           { get; set; }
            public bool                 HasInFlight        { get; set; }
            public int                  Roll               { get; set; }

            public MemberState(int participantId, string characterId)
            {
                ParticipantId = participantId;
                CharacterId   = characterId;
            }

            public int FindRoll(Guid operationId)
            {
                for (int i = 0; i < _rollIds.Length; ++i)
                {
                    if (_rollIds[i] == operationId)
                    {
                        return _rollValues[i];
                    }
                }

                return -1;
            }

            public long FindRevision(Guid operationId)
            {
                for (int i = 0; i < _revisionIds.Length; ++i)
                {
                    if (_revisionIds[i] == operationId)
                    {
                        return _revisionValues[i];
                    }
                }

                return -1;
            }

            // 처음 보낸 revision이 있으면 그대로, 없으면 지금 값을 가장 오래된 칸에 기억한다
            public long RevisionFor(Guid operationId, long current)
            {
                long remembered = FindRevision(operationId);
                if (remembered >= 0)
                {
                    return remembered;
                }

                _revisionIds[_revisionNext]    = operationId;
                _revisionValues[_revisionNext] = current;
                _revisionNext = (_revisionNext + 1) % _revisionIds.Length;
                return current;
            }

            // 기억한 값이 있으면 그대로, 없으면 새로 뽑아 가장 오래된 칸에 덮어쓴다
            public int RollFor(Guid operationId, Func<int> roll)
            {
                int remembered = FindRoll(operationId);
                if (remembered >= 0)
                {
                    return remembered;
                }

                int value = roll();
                _rollIds[_rollNext]    = operationId;
                _rollValues[_rollNext] = value;
                _rollNext = (_rollNext + 1) % _rollIds.Length;
                return value;
            }
        }
    }
}
