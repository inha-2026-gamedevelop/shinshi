// System
using System;
using System.Collections.Generic;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Network
{
    /// <summary> 마을 파티 방, 초대, 준비 확인, 출정 예약과 대기열을 맡는 단일 진행자, 서버 메인 스레드에서만 부른다 </summary>
    public sealed class TownPartyDirector : IBackendResponseListener, ISortieRoomLookup
    {
        // 방 상태를 바꾸는 곳은 이 클래스 하나다(요청 처리, Tick, 백엔드 응답 모두 메인 스레드). 비동기 응답은 보낼 때의 방 번호와
        // revision을 들고 있다가, 그 사이 방이 바뀌었으면 쓰지 않는다. 바뀐 방의 예약이 확정돼 돌아오면 바로 취소를 보낸다.
        // 결과를 모르는 채 끝난 예약(시간 초과)은 같은 operationId로 다시 보내고, 끝내 모르면 API가 만료 시각에 풀어 준다.
        // 흐름: 준비(readiness 확인) -> 방장 시작 -> 카운트다운 -> 예약 -> (NO_CAPACITY면 대기열에서 5초마다 새 operationId로
        // 다시 예약) -> 확정되면 참가자별 입장권을 받아 본인에게만 보내고 방을 닫는다

        /****************************************
        *                Fields
        ****************************************/

        private const string OPERATION_ID_FORMAT = "D";

        private readonly BackendGateway            _gateway;
        private readonly ITownAudience             _audience;
        private readonly ITownMemberDirectory      _directory;
        private readonly ProgressionCatalogSO      _progression;
        private readonly string                    _smallPotionId;
        private readonly string                    _largePotionId;
        private readonly double                    _countdownSeconds;
        private readonly string                    _contentVersion;
        private readonly bool                      _allowUnverifiedProgress;
        private readonly Func<double>              _clock;
        private readonly Action<string>            _warn;
        private readonly List<PartyRoom>           _rooms;
        private readonly Dictionary<int, PartyRoom> _roomsById;
        private readonly Dictionary<int, int>      _roomOfMember;
        private readonly Dictionary<int, PendingCall> _calls;
        private readonly PartyInviteTable          _invites;
        private readonly SortieQueue               _queue;

        private int _lastRoomId = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int              RoomCount => _rooms.Count;
        public SortieQueue      Queue     => _queue;
        public PartyInviteTable Invites   => _invites;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gateway가 null이면(개발 빌드, 서비스 자격 없음) 준비와 출정을 BackendUnavailable로 거절한다 </summary>
        public TownPartyDirector(BackendGateway gateway, ITownAudience audience, ITownMemberDirectory directory,
            ProgressionCatalogSO progression, LifeConfigSO life, float countdownSeconds, string contentVersion,
            bool allowUnverifiedProgress, Func<double> clock, Action<string> warn)
        {
            if ((audience == null) || (directory == null) || (progression == null) || (life == null))
            {
                throw new ArgumentNullException(nameof(audience), "파티 진행자에 필요한 참조가 없다");
            }

            _gateway                 = gateway;
            _audience                = audience;
            _directory               = directory;
            _progression             = progression;
            _smallPotionId           = FindPotionId(life, EPotionSize.Small);
            _largePotionId           = FindPotionId(life, EPotionSize.Large);
            _countdownSeconds        = Math.Max(0f, countdownSeconds);
            _contentVersion          = contentVersion;
            _allowUnverifiedProgress = allowUnverifiedProgress;
            _clock                   = clock ?? BackendGateway.MonotonicSeconds;
            _warn                    = warn;
            _rooms                   = new List<PartyRoom>(Constants.Town.MAX_PARTY_ROOMS);
            _roomsById               = new Dictionary<int, PartyRoom>(Constants.Town.MAX_PARTY_ROOMS);
            _roomOfMember            = new Dictionary<int, int>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _calls                   = new Dictionary<int, PendingCall>(Constants.Town.MAX_PARTY_ROOMS);
            _invites                 = new PartyInviteTable(Constants.Town.MAX_PENDING_INVITES, Constants.Town.INVITE_EXPIRE_SECONDS,
                Constants.Town.MAX_INVITES_PER_INVITER);
            _queue                   = new SortieQueue(Constants.Town.MAX_PARTY_ROOMS);
        }

        public bool TryGetRoom(int roomId, out PartyRoom room)
        {
            return _roomsById.TryGetValue(roomId, out room);
        }

        /// <summary> 참가자가 들어 있는 방, 없으면 null </summary>
        public PartyRoom RoomOf(int participantId)
        {
            if ((_roomOfMember.TryGetValue(participantId, out int roomId)) && (_roomsById.TryGetValue(roomId, out PartyRoom room)))
            {
                return room;
            }

            return null;
        }

        public bool IsQueuedAt(int roomId, long revision)
        {
            return (_roomsById.TryGetValue(roomId, out PartyRoom room)) && (room.Phase == EPartyPhase.Queued)
                && (room.Revision == revision);
        }

        /// <summary> 요청 하나를 처리하고 요청자에게 결과를 보낸다 </summary>
        public void Handle(TownMember member, in PartyRequestMessage message)
        {
            if (member == null)
            {
                return;
            }

            ERejectReason reason = Dispatch(member, in message);
            _audience.Send(member.ParticipantId, new PartyResultMessage(message.Action, reason));
        }

        /// <summary> 매 프레임 부른다. 초대 만료, 카운트다운 끝, 대기열 재예약, 응답 유실 재전송을 처리한다 </summary>
        public void Tick()
        {
            double now = _clock();
            _invites.Prune(now);
            _queue.PruneStale(this);

            for (int i = 0; i < _rooms.Count; ++i)
            {
                PartyRoom room = _rooms[i];
                switch (room.Phase)
                {
                    case EPartyPhase.Countdown:
                        if (now >= room.CountdownEndsAt)
                        {
                            BeginReservation(room);
                        }
                        break;
                    case EPartyPhase.Reserving:
                        if ((room.PendingRequestId == 0) && (now >= room.NextAttemptAt))
                        {
                            SendReservation(room);
                        }
                        break;
                    case EPartyPhase.Departing:
                        if ((room.PendingRequestId == 0) && (now >= room.NextAttemptAt))
                        {
                            SendJoinTickets(room);
                        }
                        break;
                    default:
                        break;
                }
            }

            // 대기열은 맨 앞 방만 다시 예약한다. 뒤 방은 앞 방이 빠질 때까지 기다린다
            if ((_queue.TryPeek(out int headId, out long _)) && (_roomsById.TryGetValue(headId, out PartyRoom head))
                && (head.Phase == EPartyPhase.Queued) && (head.PendingRequestId == 0) && (now >= head.NextAttemptAt))
            {
                head.ReservationOperationId = NewOperationId();
                head.Retries                = 0;
                SendReservation(head);
            }
        }

        /// <summary> 마을 연결이 끊겼다. 방에서 빼고 초대를 지운다 </summary>
        public void OnMemberDisconnected(int participantId)
        {
            _invites.RemoveAll(participantId, 0);
            RemoveFromRoom(participantId, false);
        }

        /// <summary> 캐릭터의 장비, 수련, 부적이 바뀌었다. 준비 확인 값이 낡았으므로 준비를 푼다 </summary>
        public void OnCharacterChanged(int participantId)
        {
            PartyRoom room = RoomOf(participantId);
            if (room == null)
            {
                return;
            }

            if (!room.ClearReady(participantId))
            {
                return;
            }

            if ((room.Phase == EPartyPhase.Countdown) || (room.Phase == EPartyPhase.Queued))
            {
                AbortProgress(room);
            }

            BroadcastRoom(room);
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_calls.TryGetValue(response.RequestId, out PendingCall call))
            {
                return;
            }

            _calls.Remove(response.RequestId);
            switch (call.Kind)
            {
                case ECallKind.Readiness:
                    OnReadinessResponse(in call, in response);
                    break;
                case ECallKind.Reservation:
                    OnReservationResponse(in call, in response);
                    break;
                case ECallKind.JoinTickets:
                    OnJoinTicketsResponse(in call, in response);
                    break;
                case ECallKind.Cancel:
                    if (!response.IsSucceeded)
                    {
                        _warn?.Invoke($"[TownParty] 출정 예약 취소 실패, 만료 시각에 API가 푼다, outcome={response.Outcome}, " +
                            $"status={response.StatusCode}");
                    }
                    break;
                default:
                    break;
            }
        }

        private ERejectReason Dispatch(TownMember member, in PartyRequestMessage message)
        {
            switch (message.Action)
            {
                case EPartyAction.ListRooms:
                    SendRoomList(member);
                    return ERejectReason.None;
                case EPartyAction.CreateRoom:
                    return CreateRoom(member, in message);
                case EPartyAction.JoinRoom:
                    return Join(member, message.RoomId, message.Password, false);
                case EPartyAction.LeaveRoom:
                    return RemoveFromRoom(member.ParticipantId, true) ? ERejectReason.None : ERejectReason.RoomNotFound;
                case EPartyAction.SetReady:
                    return SetReady(member, in message);
                case EPartyAction.Invite:
                    return Invite(member, message.Name);
                case EPartyAction.AcceptInvite:
                    if (!_invites.TryConsume(member.ParticipantId, message.RoomId, _clock()))
                    {
                        return ERejectReason.InviteExpired;
                    }

                    return Join(member, message.RoomId, null, true);
                case EPartyAction.DeclineInvite:
                    _invites.Remove(member.ParticipantId, message.RoomId);
                    return ERejectReason.None;
                case EPartyAction.StartSortie:
                    return StartSortie(member);
                case EPartyAction.CancelSortie:
                    return CancelSortie(member);
                default:
                    return ERejectReason.InvalidTarget;
            }
        }

        private ERejectReason CreateRoom(TownMember member, in PartyRequestMessage message)
        {
            if (RoomOf(member.ParticipantId) != null)
            {
                return ERejectReason.WrongPhase;
            }

            string name = message.Name?.Trim();
            if ((string.IsNullOrEmpty(name)) || (name.Length > Constants.Town.MAX_ROOM_NAME_LENGTH))
            {
                return ERejectReason.InvalidTarget;
            }

            if ((message.Password != null) && (message.Password.Length > Constants.Town.MAX_ROOM_PASSWORD_LENGTH))
            {
                return ERejectReason.InvalidTarget;
            }

            ERejectReason contentReason = ReadyRules.CheckContent(_progression, message.Mode, message.ContentId,
                out PartyContent content);
            if (contentReason != ERejectReason.None)
            {
                return contentReason;
            }

            if (_rooms.Count >= Constants.Town.MAX_PARTY_ROOMS)
            {
                return ERejectReason.NoCapacity;
            }

            var room = new PartyRoom(NextRoomId(), name, message.Mode, message.ContentId, content.Difficulty, content.BossId,
                content.TicketCost, message.Password);
            ERejectReason addReason = room.TryAdd(member);
            if (addReason != ERejectReason.None)
            {
                return addReason;
            }

            _rooms.Add(room);
            _roomsById.Add(room.RoomId, room);
            _roomOfMember[member.ParticipantId] = room.RoomId;
            BroadcastRoom(room);
            return ERejectReason.None;
        }

        private ERejectReason Join(TownMember member, int roomId, string password, bool viaInvite)
        {
            if (RoomOf(member.ParticipantId) != null)
            {
                return ERejectReason.WrongPhase;
            }

            if (!_roomsById.TryGetValue(roomId, out PartyRoom room))
            {
                return ERejectReason.RoomNotFound;
            }

            if ((!viaInvite) && (!room.PasswordMatches(password)))
            {
                return ERejectReason.RoomPasswordMismatch;
            }

            ERejectReason reason = room.TryAdd(member);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            _roomOfMember[member.ParticipantId] = roomId;
            BroadcastRoom(room);
            return ERejectReason.None;
        }

        private ERejectReason SetReady(TownMember member, in PartyRequestMessage message)
        {
            PartyRoom room = RoomOf(member.ParticipantId);
            if (room == null)
            {
                return ERejectReason.RoomNotFound;
            }

            if (!message.Ready)
            {
                if ((room.Phase == EPartyPhase.Reserving) || (room.Phase == EPartyPhase.Departing))
                {
                    return ERejectReason.WrongPhase;
                }

                room.ClearReady(member.ParticipantId);
                if (room.Phase != EPartyPhase.Open)
                {
                    AbortProgress(room);
                }

                BroadcastRoom(room);
                return ERejectReason.None;
            }

            if (room.Phase != EPartyPhase.Open)
            {
                return ERejectReason.WrongPhase;
            }

            if ((!ReadyRules.IsValidPotionCount(message.SmallPotions)) || (!ReadyRules.IsValidPotionCount(message.LargePotions)))
            {
                return ERejectReason.InvalidTarget;
            }

            if (!member.HasBackendIdentity)
            {
                return ERejectReason.SessionMismatch;
            }

            if (_gateway == null)
            {
                return ERejectReason.BackendUnavailable;
            }

            int index = room.IndexOf(member.ParticipantId);
            if (room.ReadyAt(index) == EPartyReadyState.Checking)
            {
                return ERejectReason.WrongPhase;
            }

            var body = new ReadinessRequest(new[] { new ReadinessParticipant(member.AccountId, member.CharacterId) });
            BackendRequest request = BackendRequest.Post(GameServerRoutes.READINESS, body, typeof(ReadinessResponse), null,
                Constants.Network.API_TIMEOUT_SECONDS);
            int requestId = _gateway.Send(request, this);
            _calls[requestId] = new PendingCall(ECallKind.Readiness, room.RoomId, member.ParticipantId, room.Revision);
            room.BeginCheck(member.ParticipantId, requestId, message.SmallPotions, message.LargePotions);
            BroadcastRoom(room);
            return ERejectReason.None;
        }

        private ERejectReason Invite(TownMember member, string targetName)
        {
            PartyRoom room = RoomOf(member.ParticipantId);
            if (room == null)
            {
                return ERejectReason.RoomNotFound;
            }

            if (room.Phase != EPartyPhase.Open)
            {
                return ERejectReason.WrongPhase;
            }

            if (room.IsFull)
            {
                return ERejectReason.NoCapacity;
            }

            if ((string.IsNullOrEmpty(targetName)) || (!_directory.TryFindByName(targetName, out TownMember target))
                || (target.ParticipantId == member.ParticipantId))
            {
                return ERejectReason.InvalidTarget;
            }

            double now = _clock();
            if (!_invites.Add(target.ParticipantId, room.RoomId, member.ParticipantId, now))
            {
                return ERejectReason.NoCapacity;
            }

            float expiresIn = (float)_invites.RemainingSeconds(target.ParticipantId, room.RoomId, now);
            _audience.Send(target.ParticipantId, new PartyInviteMessage(room.RoomId, room.Name, member.CharacterName, room.Mode,
                room.ContentId, expiresIn));
            return ERejectReason.None;
        }

        private ERejectReason StartSortie(TownMember member)
        {
            PartyRoom room = RoomOf(member.ParticipantId);
            ERejectReason reason = ReadyRules.CanStart(room, member.ParticipantId);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            if (_gateway == null)
            {
                return ERejectReason.BackendUnavailable;
            }

            room.Phase           = EPartyPhase.Countdown;
            room.CountdownEndsAt = _clock() + _countdownSeconds;
            room.Touch();
            BroadcastRoom(room);
            return ERejectReason.None;
        }

        private ERejectReason CancelSortie(TownMember member)
        {
            PartyRoom room = RoomOf(member.ParticipantId);
            if (room == null)
            {
                return ERejectReason.RoomNotFound;
            }

            if (room.LeaderId != member.ParticipantId)
            {
                return ERejectReason.NotLeader;
            }

            // 예약 응답을 기다리는 중에는 취소할 수 없다. 결과를 받은 뒤 확정이면 출정, 아니면 모집으로 돌아간다
            if ((room.Phase != EPartyPhase.Countdown) && (room.Phase != EPartyPhase.Queued))
            {
                return ERejectReason.WrongPhase;
            }

            AbortProgress(room);
            BroadcastRoom(room);
            return ERejectReason.None;
        }

        // 참가자를 방에서 뺀다. 방이 비면 닫고, 진행 중이던 출정 준비는 모집으로 되돌린다
        private bool RemoveFromRoom(int participantId, bool notifyLeaver)
        {
            PartyRoom room = RoomOf(participantId);
            if (room == null)
            {
                return false;
            }

            room.Remove(participantId);
            _roomOfMember.Remove(participantId);
            if (notifyLeaver)
            {
                _audience.Send(participantId, PartyRoomStateMessage.Left());
            }

            if (room.Phase != EPartyPhase.Open)
            {
                AbortProgress(room);
            }

            if (room.IsEmpty)
            {
                CloseRoom(room, false);
                return true;
            }

            BroadcastRoom(room);
            return true;
        }

        // 출정 준비를 모집 단계로 되돌린다. 예약이 이미 확정됐으면(입장권 대기) 취소를 보낸다.
        // 응답을 기다리던 예약은 revision이 달라졌으므로 확정으로 돌아오면 그때 취소한다
        private void AbortProgress(PartyRoom room)
        {
            _queue.Remove(room.RoomId);
            if ((room.Phase == EPartyPhase.Departing) && (!string.IsNullOrEmpty(room.SessionId)))
            {
                SendCancel(room.SessionId);
            }

            room.Phase     = EPartyPhase.Open;
            room.SessionId = null;
            room.ClearPendingWork();
            room.Touch();
        }

        private void CloseRoom(PartyRoom room, bool notifyMembers)
        {
            for (int i = 0; i < room.Count; ++i)
            {
                int participantId = room.MemberAt(i).ParticipantId;
                _roomOfMember.Remove(participantId);
                if (notifyMembers)
                {
                    _audience.Send(participantId, PartyRoomStateMessage.Left());
                }
            }

            _invites.RemoveAll(0, room.RoomId);
            _queue.Remove(room.RoomId);
            _roomsById.Remove(room.RoomId);
            _rooms.Remove(room);
        }

        private void BeginReservation(PartyRoom room)
        {
            room.Phase                  = EPartyPhase.Reserving;
            room.ReservationOperationId = NewOperationId();
            room.Retries                = 0;
            room.Touch();
            BroadcastRoom(room);
            SendReservation(room);
        }

        private void SendReservation(PartyRoom room)
        {
            ReservationRequest body = SortieReservationRules.Build(room.ReservationOperationId, room, _contentVersion);
            if ((body == null) || (_gateway == null))
            {
                AbortProgress(room);
                BroadcastRoom(room);
                return;
            }

            BackendRequest request = BackendRequest.Post(GameServerRoutes.RESERVATIONS, body, typeof(ReservationResponse),
                room.ReservationOperationId, Constants.Network.API_TIMEOUT_SECONDS);
            int requestId = _gateway.Send(request, this);
            room.PendingRequestId = requestId;
            _calls[requestId] = new PendingCall(ECallKind.Reservation, room.RoomId, 0, room.Revision);
        }

        private void SendJoinTickets(PartyRoom room)
        {
            string path = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.JOIN_TICKETS_FORMAT, room.SessionId);
            BackendRequest request = BackendRequest.PostWithoutBody(path, typeof(JoinTicketsResponse), null,
                Constants.Network.API_TIMEOUT_SECONDS);
            int requestId = _gateway.Send(request, this);
            room.PendingRequestId = requestId;
            _calls[requestId] = new PendingCall(ECallKind.JoinTickets, room.RoomId, 0, room.Revision);
        }

        private void SendCancel(string sessionId)
        {
            if ((_gateway == null) || (string.IsNullOrEmpty(sessionId)))
            {
                return;
            }

            string operationId = NewOperationId();
            string path        = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.RESERVATION_CANCEL_FORMAT, sessionId);
            BackendRequest request = BackendRequest.Post(path, new ReservationCancelRequest(operationId), typeof(OperationResult),
                operationId, Constants.Network.API_TIMEOUT_SECONDS);
            int requestId = _gateway.Send(request, this);
            _calls[requestId] = new PendingCall(ECallKind.Cancel, 0, 0, 0);
        }

        private void OnReadinessResponse(in PendingCall call, in BackendResponse response)
        {
            if ((!_roomsById.TryGetValue(call.RoomId, out PartyRoom room)) || (!room.IsChecking(call.ParticipantId, response.RequestId)))
            {
                return;
            }

            int        index  = room.IndexOf(call.ParticipantId);
            TownMember member = room.MemberAt(index);
            var        payload = response.IsSucceeded ? response.Payload as ReadinessResponse : null;
            ERejectReason reason;
            ReservationPotion[] potions = null;
            if (payload == null)
            {
                reason = TownRejectRules.IsDefiniteRejection(in response)
                    ? TownRejectRules.FromApi(response.ReasonCode, ERejectReason.SessionMismatch)
                    : ERejectReason.BackendUnavailable;
            }
            else
            {
                ReadinessParticipantStatus status = FindStatus(payload, member.CharacterId);
                reason = ReadyRules.CheckStatus(status, room.TicketCost, _smallPotionId, room.SmallPotionsAt(index), _largePotionId,
                    room.LargePotionsAt(index), out potions);
                if (reason == ERejectReason.None)
                {
                    // 해금은 참가자마다 다르다. 예약 API가 같은 요구 조건으로 다시 판정한다
                    reason = ReadyRules.CheckProgress(_progression, room.Mode, room.ContentId, status.Progress,
                        _allowUnverifiedProgress);
                }

                if (reason == ERejectReason.None)
                {
                    room.MarkReady(call.ParticipantId, status.CharacterRevision, potions);
                    BroadcastRoom(room);
                    return;
                }
            }

            room.ClearReady(call.ParticipantId);
            BroadcastRoom(room);
            _audience.Send(call.ParticipantId, new PartyResultMessage(EPartyAction.SetReady, reason));
        }

        private void OnReservationResponse(in PendingCall call, in BackendResponse response)
        {
            var             payload = response.Payload as ReservationResponse;
            OperationResult nested  = payload?.Result;
            bool committed = (response.IsSucceeded) && (nested != null)
                && (nested.Status == ContractCodes.OperationStatus.COMMITTED);
            bool rejected  = (!committed) && ((TownRejectRules.IsDefiniteRejection(in response))
                || ((nested != null) && (nested.Status == ContractCodes.OperationStatus.REJECTED)));

            bool stale = (!_roomsById.TryGetValue(call.RoomId, out PartyRoom room)) || (room.Revision != call.Revision)
                || (room.PendingRequestId != response.RequestId);
            if (stale)
            {
                // 그 사이 방이 바뀌어 이 예약은 쓰지 않는다. 확정됐으면 티켓과 물약을 바로 풀어 준다
                if ((committed) && (!string.IsNullOrEmpty(payload.SessionId)))
                {
                    SendCancel(payload.SessionId);
                }

                return;
            }

            room.PendingRequestId = 0;
            double now = _clock();
            if (committed)
            {
                _queue.Remove(room.RoomId);
                room.ClearPendingWork();
                room.SessionId = payload.SessionId;
                room.Phase     = EPartyPhase.Departing;
                room.Touch();
                BroadcastRoom(room);
                SendJoinTickets(room);
                return;
            }

            if (rejected)
            {
                ERejectReason reason = TownRejectRules.ReasonOf(in response, nested, ERejectReason.InvalidTarget);
                if (reason == ERejectReason.NoCapacity)
                {
                    // 대기 중 재예약도 NO_CAPACITY면 자리는 그대로 두고 다음 시각만 미룬다. revision을 바꾸면 줄에서 빠진다
                    if (room.Phase != EPartyPhase.Queued)
                    {
                        room.Phase = EPartyPhase.Queued;
                        room.Touch();
                        _queue.Enqueue(room.RoomId, room.Revision);
                        BroadcastRoom(room);
                    }

                    room.NextAttemptAt = now + Constants.Town.SORTIE_QUEUE_RETRY_SECONDS;
                    return;
                }

                FailSortie(room, reason);
                return;
            }

            // 결과를 모른다. 대기 중이면 다음 대기 시각에 새 operationId로, 아니면 같은 operationId로 다시 보낸다
            if (room.Phase == EPartyPhase.Queued)
            {
                room.NextAttemptAt = now + Constants.Town.SORTIE_QUEUE_RETRY_SECONDS;
                return;
            }

            room.Retries = room.Retries + 1;
            if (room.Retries > Constants.Town.SORTIE_MAX_RETRIES)
            {
                FailSortie(room, ERejectReason.BackendUnavailable);
                return;
            }

            room.NextAttemptAt = now + Math.Max(Constants.Town.SORTIE_RETRY_SECONDS, response.RetryAfterSeconds);
        }

        private void OnJoinTicketsResponse(in PendingCall call, in BackendResponse response)
        {
            if ((!_roomsById.TryGetValue(call.RoomId, out PartyRoom room)) || (room.Revision != call.Revision)
                || (room.PendingRequestId != response.RequestId))
            {
                return;
            }

            room.PendingRequestId = 0;
            var payload = response.Payload as JoinTicketsResponse;
            if ((response.IsSucceeded) && (payload != null) && (payload.ReasonCode == ContractCodes.RejectReason.NONE)
                && (payload.Tickets != null))
            {
                DeliverTickets(room, payload);
                CloseRoom(room, true);
                return;
            }

            bool definite = (TownRejectRules.IsDefiniteRejection(in response))
                || ((payload != null) && (payload.ReasonCode != ContractCodes.RejectReason.NONE));
            if (definite)
            {
                int reasonCode = ((payload != null) && (payload.ReasonCode != ContractCodes.RejectReason.NONE))
                    ? payload.ReasonCode
                    : response.ReasonCode;
                SendCancel(room.SessionId);
                FailSortie(room, TownRejectRules.FromApi(reasonCode, ERejectReason.SessionMismatch));
                return;
            }

            room.Retries = room.Retries + 1;
            if (room.Retries > Constants.Town.SORTIE_MAX_RETRIES)
            {
                SendCancel(room.SessionId);
                FailSortie(room, ERejectReason.BackendUnavailable);
                return;
            }

            room.NextAttemptAt = _clock() + Math.Max(Constants.Town.SORTIE_RETRY_SECONDS, response.RetryAfterSeconds);
        }

        // 각 참가자에게 자기 캐릭터의 입장권만 보낸다. 입장권이 빠진 참가자는 출정에 들어가지 못하므로 기록을 남긴다
        private void DeliverTickets(PartyRoom room, JoinTicketsResponse payload)
        {
            for (int i = 0; i < room.Count; ++i)
            {
                TownMember member = room.MemberAt(i);
                JoinTicket ticket = FindTicket(payload.Tickets, member.CharacterId);
                if (ticket == null)
                {
                    _warn?.Invoke($"[TownParty] 출정 입장권에 참가자가 없다, room={room.RoomId}, participant={member.ParticipantId}");
                    continue;
                }

                _audience.Send(member.ParticipantId, new SortieTicketMessage(payload.SessionId, ticket.InstanceId, ticket.Address,
                    ticket.Port, ticket.JoinToken, ticket.ExpiresAtUnixMs));
            }
        }

        // 예약이 확정 거절됐거나 끝내 결과를 모른다. 준비 확인 값이 낡았으므로 전원 준비를 풀고 모집으로 돌아간다
        private void FailSortie(PartyRoom room, ERejectReason reason)
        {
            _queue.Remove(room.RoomId);
            room.Phase     = EPartyPhase.Open;
            room.SessionId = null;
            room.ClearPendingWork();
            room.ClearAllReady();
            BroadcastRoom(room);
            for (int i = 0; i < room.Count; ++i)
            {
                _audience.Send(room.MemberAt(i).ParticipantId, new PartyResultMessage(EPartyAction.StartSortie, reason));
            }
        }

        private void SendRoomList(TownMember member)
        {
            int count = Math.Min(_rooms.Count, Constants.Town.MAX_LISTED_ROOMS);
            var rooms = new PartyRoomSummary[count];
            for (int i = 0; i < count; ++i)
            {
                PartyRoom room = _rooms[i];
                rooms[i] = new PartyRoomSummary(room.RoomId, room.Name, room.Mode, room.ContentId, room.Difficulty, room.Phase,
                    (byte)room.Count, (byte)room.CountReady(), room.HasPassword, (byte)room.CountClass(EPlayerClass.Warrior),
                    (byte)room.CountClass(EPlayerClass.Sorcerer), (byte)room.CountClass(EPlayerClass.Summoner));
            }

            _audience.Send(member.ParticipantId, new PartyRoomListMessage(rooms));
        }

        private void BroadcastRoom(PartyRoom room)
        {
            var members = new PartyMemberView[room.Count];
            for (int i = 0; i < room.Count; ++i)
            {
                TownMember member = room.MemberAt(i);
                members[i] = new PartyMemberView(member.ParticipantId, member.CharacterName, member.PlayerClass, room.ReadyAt(i),
                    room.SmallPotionsAt(i), room.LargePotionsAt(i));
            }

            float countdown = (room.Phase == EPartyPhase.Countdown)
                ? (float)Math.Max(0.0, room.CountdownEndsAt - _clock())
                : 0f;
            var message = new PartyRoomStateMessage(true, room.RoomId, room.Revision, room.Name, room.Mode, room.ContentId,
                room.Difficulty, room.HasPassword, room.LeaderId, room.Phase, countdown, members);
            for (int i = 0; i < room.Count; ++i)
            {
                _audience.Send(room.MemberAt(i).ParticipantId, message);
            }
        }

        private int NextRoomId()
        {
            ++_lastRoomId;
            if (_lastRoomId <= 0)
            {
                _lastRoomId = 1;
            }

            return _lastRoomId;
        }

        private static string NewOperationId()
        {
            return Guid.NewGuid().ToString(OPERATION_ID_FORMAT);
        }

        private static ReadinessParticipantStatus FindStatus(ReadinessResponse payload, string characterId)
        {
            ReadinessParticipantStatus[] statuses = payload.Participants;
            if (statuses == null)
            {
                return null;
            }

            for (int i = 0; i < statuses.Length; ++i)
            {
                if ((statuses[i] != null) && (string.Equals(statuses[i].CharacterId, characterId, StringComparison.OrdinalIgnoreCase)))
                {
                    return statuses[i];
                }
            }

            return null;
        }

        private static JoinTicket FindTicket(JoinTicket[] tickets, string characterId)
        {
            for (int i = 0; i < tickets.Length; ++i)
            {
                if ((tickets[i] != null) && (string.Equals(tickets[i].CharacterId, characterId, StringComparison.OrdinalIgnoreCase)))
                {
                    return tickets[i];
                }
            }

            return null;
        }

        private static string FindPotionId(LifeConfigSO life, EPotionSize size)
        {
            IReadOnlyList<LifeConfigSO.PotionDefinition> potions = life.Potions;
            for (int i = 0; i < potions.Count; ++i)
            {
                if ((potions[i] != null) && (potions[i].Size == size))
                {
                    return potions[i].DefinitionId;
                }
            }

            return null;
        }

        /****************************************
        *               Records
        ****************************************/

        private enum ECallKind
        {
            Readiness   = 0,
            Reservation = 1,
            JoinTickets = 2,
            Cancel      = 3
        }

        // 요청 번호로 찾는 비동기 호출 하나. 응답 때 방이 같은 상태인지 revision으로 다시 본다
        private readonly struct PendingCall
        {
            private readonly ECallKind _kind;
            private readonly int       _roomId;
            private readonly int       _participantId;
            private readonly long      _revision;

            public ECallKind Kind          => _kind;
            public int       RoomId        => _roomId;
            public int       ParticipantId => _participantId;
            public long      Revision      => _revision;

            public PendingCall(ECallKind kind, int roomId, int participantId, long revision)
            {
                _kind          = kind;
                _roomId        = roomId;
                _participantId = participantId;
                _revision      = revision;
            }
        }
    }
}
