// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 마을 1:1 거래의 요청, 세션, 거리, 시간 취소, 실행(prepare -> commit -> resolve)을 맡는다, 메인 스레드에서만 부른다 </summary>
    public sealed class TownTradeDirector : IBackendResponseListener
    {
        // 거래 세션은 마을 서버 메모리에만 있다. 신원은 TownMember에서만 꺼내고 메시지의 이름은 대상을 찾는 데만 쓴다.
        // 제안을 넣거나 확정할 때 마지막 성장, 생활 스냅숏으로 보유량을 미리 걸러 빠르게 알린다. 보관은 하지 않고 실제 판정은 실행 API가 한다.
        // 양쪽이 수락하면 Committing이 된다. 이때부터는 제안 변경, 취소, 거리, 출정 취소를 막고 연결이 끊겨도 결과를 받을 때까지 세션을 남긴다.
        // 결과를 모르는 prepare는 같은 operationId로 다시 보내고, commit 응답이 없으면 resolve로 묻는다(1, 2, 4초 뒤, 이후 5초마다).
        // 확정 거절이면 편집으로 돌아가고 다음 실행은 새 executionId를 쓴다. 방치로 끝났으면 거래를 실패로 마친다

        /****************************************
        *                Fields
        ****************************************/

        private readonly BackendGateway                 _gateway;
        private readonly ITownAudience                  _audience;
        private readonly ITownMemberDirectory           _directory;
        private readonly TownSnapshotTap                _snapshots;
        private readonly string                         _townInstanceId;
        private readonly string                         _currencyItemId;
        private readonly Func<int, bool>                _isSortieBusy;
        private readonly Action<int>                    _tradeCommitted;
        private readonly Func<double>                   _clock;
        private readonly Action<TownMember>             _refreshSnapshots;
        private readonly Dictionary<long, double>       _cooldowns;
        private readonly TradeRequestTable              _requests;
        private readonly Dictionary<int, TownMember>    _members;
        private readonly Dictionary<Guid, TradeSession> _sessions;
        private readonly Dictionary<int, TradeSession>  _byParticipant;
        private readonly Dictionary<int, Guid>          _owners;
        private readonly List<TradeRequestEntry>        _scratchRequests;
        private readonly List<TradeSession>             _scratchSessions;
        private readonly List<long>                     _scratchCooldownKeys = new List<long>();

        private const int STATUS_NOT_FOUND = 404;

        private double _nextCheckAt = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        public int SessionCount => _sessions.Count;
        public int RequestCount => _requests.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gateway가 null이면 요청을 BackendUnavailable로 거절한다. snapshots가 null이면 사전 검사를 건너뛴다.
        /// isSortieBusy는 출정을 시작한 참가자인지, tradeCommitted는 거래가 확정된 참가자 번호를 받는다 </summary>
        public TownTradeDirector(BackendGateway gateway, ITownAudience audience, ITownMemberDirectory directory,
            TownSnapshotTap snapshots, string townInstanceId, string currencyItemId, Func<int, bool> isSortieBusy,
            Action<int> tradeCommitted, Func<double> clock, Action<TownMember> refreshSnapshots = null)
        {
            _refreshSnapshots = refreshSnapshots;
            _cooldowns        = new Dictionary<long, double>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _gateway         = gateway;
            _audience        = audience ?? throw new ArgumentNullException(nameof(audience));
            _directory       = directory ?? throw new ArgumentNullException(nameof(directory));
            _snapshots       = snapshots;
            _townInstanceId  = townInstanceId;
            _currencyItemId  = currencyItemId;
            _isSortieBusy    = isSortieBusy;
            _tradeCommitted  = tradeCommitted;
            _clock           = clock ?? BackendGateway.MonotonicSeconds;
            _requests        = new TradeRequestTable(TradeSettings.REQUEST_TABLE_SIZE, TradeSettings.REQUEST_EXPIRE_SECONDS,
                TradeSettings.MAX_SENT_REQUESTS);
            _members         = new Dictionary<int, TownMember>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _sessions        = new Dictionary<Guid, TradeSession>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _byParticipant   = new Dictionary<int, TradeSession>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _owners          = new Dictionary<int, Guid>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _scratchRequests = new List<TradeRequestEntry>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _scratchSessions = new List<TradeSession>(Constants.Session.MAX_SESSION_PARTICIPANTS);
        }

        /// <summary> 마을에 접속한 참가자를 알린다, 요청의 상대를 번호로 찾는 데 쓴다 </summary>
        public void AddMember(TownMember member)
        {
            if (member != null)
            {
                _members[member.ParticipantId] = member;
            }
        }

        /// <summary> 이 계정이 양쪽 수락 뒤 실행(Committing) 중인 거래의 참가자인지, 재접속한 새 연결도 찾는다 </summary>
        public bool IsCommittingAccount(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return false;
            }

            foreach (KeyValuePair<Guid, TradeSession> pair in _sessions)
            {
                TradeSession session = pair.Value;
                if ((session.IsCommitting)
                    && ((string.Equals(session.MemberAt(0).AccountId, accountId, StringComparison.Ordinal))
                        || (string.Equals(session.MemberAt(1).AccountId, accountId, StringComparison.Ordinal))))
                {
                    return true;
                }
            }

            return false;
        }

        public bool InSession(int participantId)
        {
            return _byParticipant.ContainsKey(participantId);
        }

        public bool TryGetSession(int participantId, out TradeSession session)
        {
            return _byParticipant.TryGetValue(participantId, out session);
        }

        public void Handle(TownMember member, in TradeRequestMessage message)
        {
            if (member == null)
            {
                return;
            }

            double now = _clock();
            if (!member.TryAcceptRequest(ETownChannel.Trade, now))
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.RateLimited);
                return;
            }

            if (!member.HasBackendIdentity)
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.SessionMismatch);
                return;
            }

            switch (message.Action)
            {
                case ETradeAction.Request:
                    HandleRequest(member, in message, now);
                    return;
                case ETradeAction.Respond:
                    HandleRespond(member, in message, now);
                    return;
                case ETradeAction.SetOffer:
                case ETradeAction.Lock:
                case ETradeAction.Unlock:
                case ETradeAction.Accept:
                case ETradeAction.Cancel:
                    HandleSession(member, in message, now);
                    return;
                default:
                    Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.InvalidTarget);
                    return;
            }
        }

        /// <summary> 마을 연결이 끊겼다. 요청을 지우고, 실행 중이 아닌 거래는 취소한다. 실행 중이면 결과를 받을 때까지 세션을 남긴다 </summary>
        public void RemoveMember(int participantId)
        {
            _members.Remove(participantId);
            _snapshots?.Forget(participantId);

            _scratchRequests.Clear();
            _requests.RemoveAll(participantId, _scratchRequests);
            for (int i = 0; i < _scratchRequests.Count; ++i)
            {
                TradeRequestEntry entry = _scratchRequests[i];
                int other = (entry.RequesterId == participantId) ? entry.TargetId : entry.RequesterId;
                SendEnded(other, entry.TradeId, participantId, ERejectReason.TradeNotInTown);
            }

            if (!_byParticipant.TryGetValue(participantId, out TradeSession session))
            {
                return;
            }

            int side = session.SideOf(participantId);
            session.SetConnected(side, false);
            _byParticipant.Remove(participantId);
            if (session.TryCancel(ERejectReason.TradeNotInTown))
            {
                Finish(session, _clock());
            }
        }

        /// <summary> 매 프레임 부른다. 요청 만료, 카운트다운, 5분 방치, 1초마다 거리와 출정, 실행 재시도를 본다 </summary>
        public void Tick()
        {
            double now = _clock();

            _scratchRequests.Clear();
            _requests.Prune(now, _scratchRequests);
            for (int i = 0; i < _scratchRequests.Count; ++i)
            {
                StartCooldown(_scratchRequests[i], now);
                NotifyRequestEnded(_scratchRequests[i], ERejectReason.TradeRequestExpired);
            }

            bool check = now >= _nextCheckAt;
            if (check)
            {
                _nextCheckAt = now + TradeSettings.CHECK_INTERVAL_SECONDS;
                PruneCooldowns(now);
            }

            _scratchSessions.Clear();
            foreach (KeyValuePair<Guid, TradeSession> pair in _sessions)
            {
                _scratchSessions.Add(pair.Value);
            }

            for (int i = 0; i < _scratchSessions.Count; ++i)
            {
                TradeSession session = _scratchSessions[i];
                if (session.IsCommitting)
                {
                    DriveCommit(session, now);
                    continue;
                }

                if (session.Tick(now))
                {
                    BroadcastState(session, now);
                }

                if (session.IsIdleExpired(now))
                {
                    CancelSession(session, ERejectReason.TradeExpired, now);
                    continue;
                }

                if (check)
                {
                    ERejectReason reason = CheckPresence(session);
                    if (reason != ERejectReason.None)
                    {
                        CancelSession(session, reason, now);
                    }
                }
            }
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_owners.TryGetValue(response.RequestId, out Guid tradeId))
            {
                return;
            }

            _owners.Remove(response.RequestId);
            if ((!_sessions.TryGetValue(tradeId, out TradeSession session)) || (session.PendingRequestId != response.RequestId))
            {
                return;
            }

            session.PendingRequestId = 0;
            var    payload = response.Payload as TradeExecutionResponse;
            double now     = _clock();
            switch (session.CommitPhase)
            {
                case ETradeCommitPhase.Preparing:
                    OnPrepareResponse(session, in response, payload, now);
                    return;
                case ETradeCommitPhase.Committing:
                    OnCommitResponse(session, in response, payload, now);
                    return;
                case ETradeCommitPhase.Resolving:
                    OnResolveResponse(session, in response, payload, now);
                    return;
            }
        }

        /****************************************
        *               Requests
        ****************************************/

        private void HandleRequest(TownMember member, in TradeRequestMessage message, double now)
        {
            if (_gateway == null)
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.BackendUnavailable);
                return;
            }

            if ((string.IsNullOrEmpty(message.TargetName)) || (!_directory.TryFindByName(message.TargetName, out TownMember target))
                || (target == member) || (!target.HasBackendIdentity))
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.InvalidTarget);
                return;
            }

            if (string.Equals(member.AccountId, target.AccountId, StringComparison.Ordinal))
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.TradeSameAccount);
                return;
            }

            if ((_byParticipant.ContainsKey(member.ParticipantId)) || (_byParticipant.ContainsKey(target.ParticipantId)))
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.TradeBusy);
                return;
            }

            // 재접속해 연결 번호가 바뀌어도 실행 중인 거래가 남아 있는 계정은 새 거래를 열 수 없다
            if ((IsCommittingAccount(member.AccountId)) || (IsCommittingAccount(target.AccountId)))
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.PendingCommit);
                return;
            }

            if ((IsSortieBusy(member.ParticipantId)) || (IsSortieBusy(target.ParticipantId)))
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.WrongPhase);
                return;
            }

            if (!IsWithin(member.ParticipantId, target.ParticipantId, TradeSettings.REQUEST_DISTANCE))
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.TradeTooFar);
                return;
            }

            // 이미 보낸 요청이 대기 중이면 초대를 다시 보내지 않고 그대로 둔다(만료도 늘리지 않는다)
            if (_requests.TryFindPair(member.ParticipantId, target.ParticipantId, out TradeRequestEntry existing))
            {
                Reply(member.ParticipantId, in message, existing.TradeId, ERejectReason.None);
                return;
            }

            if ((_cooldowns.TryGetValue(PairKey(member.ParticipantId, target.ParticipantId), out double until)) && (now < until))
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.RateLimited);
                return;
            }

            if (!_requests.Add(member.ParticipantId, target.ParticipantId, now, out Guid tradeId))
            {
                Reply(member.ParticipantId, in message, Guid.Empty, ERejectReason.TradeBusy);
                return;
            }

            _refreshSnapshots?.Invoke(member);
            _audience.Send(target.ParticipantId, new TradeInviteMessage(tradeId, member.CharacterName,
                (float)TradeSettings.REQUEST_EXPIRE_SECONDS));
            _audience.Send(member.ParticipantId, new TradeStateMessage(tradeId, ETradeState.Requested, ERejectReason.None,
                target.CharacterName, null, null, 0, 0, false, false, false, false, 0, 0, 0f));
            Reply(member.ParticipantId, in message, tradeId, ERejectReason.None);
        }

        private void HandleRespond(TownMember member, in TradeRequestMessage message, double now)
        {
            if (!_requests.TryGet(message.TradeId, member.ParticipantId, out TradeRequestEntry entry))
            {
                Reply(member.ParticipantId, in message, message.TradeId, ERejectReason.TradeRequestExpired);
                return;
            }

            if (now >= entry.ExpiresAt)
            {
                _requests.Remove(entry.TradeId);
                StartCooldown(entry, now);
                NotifyRequestEnded(entry, ERejectReason.TradeRequestExpired);
                Reply(member.ParticipantId, in message, entry.TradeId, ERejectReason.TradeRequestExpired);
                return;
            }

            if (!message.Accept)
            {
                _requests.Remove(entry.TradeId);
                StartCooldown(entry, now);
                NotifyRequestEnded(entry, ERejectReason.None);
                Reply(member.ParticipantId, in message, entry.TradeId, ERejectReason.None);
                return;
            }

            ERejectReason reason = CheckAcceptable(member, entry);
            if (reason != ERejectReason.None)
            {
                _requests.Remove(entry.TradeId);
                NotifyRequestEnded(entry, reason);
                Reply(member.ParticipantId, in message, entry.TradeId, reason);
                return;
            }

            TownMember requester = _members[entry.RequesterId];
            _refreshSnapshots?.Invoke(requester);
            _refreshSnapshots?.Invoke(member);
            var session = new TradeSession(entry.TradeId, requester, member, now);
            _sessions[session.TradeId]            = session;
            _byParticipant[requester.ParticipantId] = session;
            _byParticipant[member.ParticipantId]    = session;

            // 두 사람의 다른 요청은 모두 접는다
            _scratchRequests.Clear();
            _requests.RemoveAll(requester.ParticipantId, _scratchRequests);
            _requests.RemoveAll(member.ParticipantId, _scratchRequests);
            for (int i = 0; i < _scratchRequests.Count; ++i)
            {
                NotifyRequestEnded(_scratchRequests[i], ERejectReason.TradeBusy);
            }

            BroadcastState(session, now);
            Reply(member.ParticipantId, in message, session.TradeId, ERejectReason.None);
        }

        private ERejectReason CheckAcceptable(TownMember member, TradeRequestEntry entry)
        {
            if ((!_members.TryGetValue(entry.RequesterId, out TownMember requester)) || (!requester.HasBackendIdentity))
            {
                return ERejectReason.TradeRequestExpired;
            }

            if ((_byParticipant.ContainsKey(requester.ParticipantId)) || (_byParticipant.ContainsKey(member.ParticipantId)))
            {
                return ERejectReason.TradeBusy;
            }

            if ((IsCommittingAccount(requester.AccountId)) || (IsCommittingAccount(member.AccountId)))
            {
                return ERejectReason.PendingCommit;
            }

            if ((IsSortieBusy(requester.ParticipantId)) || (IsSortieBusy(member.ParticipantId)))
            {
                return ERejectReason.WrongPhase;
            }

            return IsWithin(requester.ParticipantId, member.ParticipantId, TradeSettings.REQUEST_DISTANCE)
                ? ERejectReason.None
                : ERejectReason.TradeTooFar;
        }

        private static long PairKey(int requesterId, int targetId)
        {
            return ((long)requesterId << 32) | (uint)targetId;
        }

        private void StartCooldown(TradeRequestEntry entry, double now)
        {
            _cooldowns[PairKey(entry.RequesterId, entry.TargetId)] = now + TradeSettings.REQUEST_COOLDOWN_SECONDS;
        }

        private void PruneCooldowns(double now)
        {
            if (_cooldowns.Count == 0)
            {
                return;
            }

            _scratchCooldownKeys.Clear();
            foreach (KeyValuePair<long, double> pair in _cooldowns)
            {
                if (now >= pair.Value)
                {
                    _scratchCooldownKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < _scratchCooldownKeys.Count; ++i)
            {
                _cooldowns.Remove(_scratchCooldownKeys[i]);
            }
        }

        // 요청이 끝났다. 요청자와 받는 사람 모두에게 취소 상태를 알려 화면의 대기와 초대를 닫게 한다
        private void NotifyRequestEnded(TradeRequestEntry entry, ERejectReason reason)
        {
            SendEnded(entry.RequesterId, entry.TradeId, entry.TargetId, reason);
            SendEnded(entry.TargetId, entry.TradeId, entry.RequesterId, reason);
        }

        private void SendEnded(int participantId, Guid tradeId, int partnerId, ERejectReason reason)
        {
            if (!_members.ContainsKey(participantId))
            {
                return;
            }

            string partnerName = _members.TryGetValue(partnerId, out TownMember partner) ? partner.CharacterName : null;
            _audience.Send(participantId, new TradeStateMessage(tradeId, ETradeState.Cancelled, reason, partnerName, null, null, 0, 0,
                false, false, false, false, 0, 0, 0f));
        }

        /****************************************
        *               Session
        ****************************************/

        private void HandleSession(TownMember member, in TradeRequestMessage message, double now)
        {
            if ((!_byParticipant.TryGetValue(member.ParticipantId, out TradeSession session)) || (session.TradeId != message.TradeId))
            {
                Reply(member.ParticipantId, in message, message.TradeId, ERejectReason.InvalidTarget);
                return;
            }

            int           side   = session.SideOf(member.ParticipantId);
            ERejectReason reason = ERejectReason.None;
            bool          ready  = false;
            switch (message.Action)
            {
                case ETradeAction.SetOffer:
                    reason = PreCheck(member, message.Lines, message.Currency);
                    if (reason == ERejectReason.None)
                    {
                        reason = session.SetOffer(side, message.Lines, message.Currency, now);
                    }

                    break;
                case ETradeAction.Lock:
                    reason = PreCheck(member, session.LinesOf(side), session.CurrencyOf(side));
                    if (reason == ERejectReason.None)
                    {
                        reason = session.Lock(side, message.OfferRevision, message.ConfirmGeneration, now);
                    }

                    break;
                case ETradeAction.Unlock:
                    reason = session.Unlock(side, now);
                    break;
                case ETradeAction.Accept:
                    reason = session.Accept(side, message.OfferRevision, message.ConfirmGeneration, now, out ready);
                    break;
                case ETradeAction.Cancel:
                    if (session.TryCancel(ERejectReason.None))
                    {
                        Reply(member.ParticipantId, in message, session.TradeId, ERejectReason.None);
                        Finish(session, now);
                        return;
                    }

                    reason = (session.IsCommitting) ? ERejectReason.PendingCommit : ERejectReason.WrongPhase;
                    break;
            }

            Reply(member.ParticipantId, in message, session.TradeId, reason);
            if (reason == ERejectReason.None)
            {
                if (ready)
                {
                    StartCommit(session, now);
                }

                BroadcastState(session, now);
                return;
            }

            // 낡은 값으로 보낸 요청이다. 최신 상태를 다시 보내 화면을 맞춘다
            if (reason == ERejectReason.StaleRevision)
            {
                _audience.Send(member.ParticipantId, session.ToMessage(side, now));
            }
        }

        private ERejectReason PreCheck(TownMember member, TradeOfferLine[] lines, long currency)
        {
            if (_snapshots == null)
            {
                return TradeOfferRules.ValidateFormat(lines, currency);
            }

            bool hasGrowth = _snapshots.TryGetGrowth(member.ParticipantId, out GrowthSnapshotMessage growth);
            if (!hasGrowth)
            {
                // 성장 스냅숏이 없으면 장비를 확인할 수 없다. 서버가 직접 받아 두고 이번 제안은 거절한다
                _refreshSnapshots?.Invoke(member);
            }

            bool hasLife   = _snapshots.TryGetLife(member.ParticipantId, out LifeSnapshotMessage life);
            return TradeOfferRules.PreCheck(lines, currency, hasGrowth, in growth, hasLife, in life, _currencyItemId);
        }

        private void CancelSession(TradeSession session, ERejectReason reason, double now)
        {
            if (session.TryCancel(reason))
            {
                Finish(session, now);
            }
        }

        // 거래가 끝났다. 연결된 참가자에게 마지막 상태를 보내고 세션을 지운다
        private void Finish(TradeSession session, double now)
        {
            BroadcastState(session, now);
            _sessions.Remove(session.TradeId);
            for (int side = 0; side < TradeSession.SIDE_COUNT; ++side)
            {
                int participantId = session.MemberAt(side).ParticipantId;
                if ((_byParticipant.TryGetValue(participantId, out TradeSession mapped)) && (mapped == session))
                {
                    _byParticipant.Remove(participantId);
                }
            }

            if (session.PendingRequestId != 0)
            {
                _owners.Remove(session.PendingRequestId);
                session.PendingRequestId = 0;
            }
        }

        private void BroadcastState(TradeSession session, double now)
        {
            for (int side = 0; side < TradeSession.SIDE_COUNT; ++side)
            {
                if (session.IsConnected(side))
                {
                    _audience.Send(session.MemberAt(side).ParticipantId, session.ToMessage(side, now));
                }
            }
        }

        // 거리와 출정 시작을 본다. 위치를 알 수 없으면 마을을 떠난 것으로 본다
        private ERejectReason CheckPresence(TradeSession session)
        {
            int first  = session.MemberAt(0).ParticipantId;
            int second = session.MemberAt(1).ParticipantId;
            if ((IsSortieBusy(first)) || (IsSortieBusy(second)))
            {
                return ERejectReason.TradeNotInTown;
            }

            if ((!_audience.TryGetPosition(first, out Vector3 a)) || (!_audience.TryGetPosition(second, out Vector3 b)))
            {
                return ERejectReason.TradeNotInTown;
            }

            return (HorizontalDistanceSqr(a, b) > (TradeSettings.CANCEL_DISTANCE * TradeSettings.CANCEL_DISTANCE))
                ? ERejectReason.TradeTooFar
                : ERejectReason.None;
        }

        private bool IsWithin(int first, int second, float distance)
        {
            if ((!_audience.TryGetPosition(first, out Vector3 a)) || (!_audience.TryGetPosition(second, out Vector3 b)))
            {
                return false;
            }

            return HorizontalDistanceSqr(a, b) <= (distance * distance);
        }

        private bool IsSortieBusy(int participantId)
        {
            return (_isSortieBusy != null) && (_isSortieBusy(participantId));
        }

        // 마을은 쿼터뷰 평면이라 높이 차이는 보지 않는다
        private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (dx * dx) + (dz * dz);
        }

        /****************************************
        *               Commit
        ****************************************/

        private void StartCommit(TradeSession session, double now)
        {
            session.CommitStartedAt = now;
            session.CommitPhase     = ETradeCommitPhase.Preparing;
            SendPrepare(session, now);
        }

        private void SendPrepare(TradeSession session, double now)
        {
            if (_gateway == null)
            {
                RejectToEditing(session, ERejectReason.BackendUnavailable, now);
                return;
            }

            session.CommitPhase      = ETradeCommitPhase.Preparing;
            session.PendingRequestId = _gateway.Send(TradeApiRules.BuildPrepare(session, _townInstanceId), this);
            _owners[session.PendingRequestId] = session.TradeId;
        }

        private void SendCommit(TradeSession session)
        {
            session.CommitPhase      = ETradeCommitPhase.Committing;
            session.PendingRequestId = _gateway.Send(TradeApiRules.BuildCommit(session), this);
            _owners[session.PendingRequestId] = session.TradeId;
        }

        private void SendResolve(TradeSession session)
        {
            session.CommitPhase      = ETradeCommitPhase.Resolving;
            session.PendingRequestId = _gateway.Send(TradeApiRules.BuildResolve(session), this);
            _owners[session.PendingRequestId] = session.TradeId;
        }

        private void DriveCommit(TradeSession session, double now)
        {
            if ((session.PendingRequestId != 0) || (now < session.NextActionAt) || (_gateway == null))
            {
                return;
            }

            if (session.CommitPhase == ETradeCommitPhase.Preparing)
            {
                SendPrepare(session, now);
                return;
            }

            SendResolve(session);
        }

        private void OnPrepareResponse(TradeSession session, in BackendResponse response, TradeExecutionResponse payload, double now)
        {
            if (response.IsSucceeded)
            {
                if (TradeApiRules.TryReadState(payload, out string state, out int reasonCode))
                {
                    if (state == TradeCodes.ExecutionState.PREPARED)
                    {
                        SendCommit(session);
                        return;
                    }

                    ApplyExecutionState(session, state, reasonCode, now);
                    return;
                }

                // 상태 본문이 없어도 확정된 작업이면 준비됐다고 본다
                if ((payload?.Result != null) && (payload.Result.Status == ContractCodes.OperationStatus.COMMITTED))
                {
                    SendCommit(session);
                    return;
                }
            }

            if (IsDefiniteRejection(in response, payload))
            {
                RejectToEditing(session, ReasonOf(in response, payload), now);
                return;
            }

            // 결과를 모른다. 같은 operationId로 다시 보낸다. 준비 유효 시간이 지나면 API가 알아서 방치로 끝낸다
            if ((now - session.CommitStartedAt) >= TradeSettings.PREPARE_GIVE_UP_SECONDS)
            {
                // 포기하기 전에 resolve로 한 번 묻고 그 상태를 따른다
                session.PrepareGaveUp   = true;
                session.CommitPhase     = ETradeCommitPhase.Resolving;
                session.ResolveAttempts = 0;
                session.NextActionAt    = now;
                return;
            }

            session.NextActionAt = now + TradeSettings.PREPARE_RETRY_SECONDS;
        }

        private void OnCommitResponse(TradeSession session, in BackendResponse response, TradeExecutionResponse payload, double now)
        {
            if (IsAbandoned(in response, payload))
            {
                FinishFailed(session, ERejectReason.TradeExpired, now);
                return;
            }

            // 실행 행을 못 찾았다. 결과를 모르므로 거절로 보지 않고 resolve로 확인한다
            if (response.StatusCode == STATUS_NOT_FOUND)
            {
                BeginResolving(session, now);
                return;
            }

            if (response.IsSucceeded)
            {
                if (TradeApiRules.TryReadState(payload, out string state, out int reasonCode))
                {
                    ApplyExecutionState(session, state, reasonCode, now);
                    return;
                }

                if ((payload?.Result != null) && (payload.Result.Status == ContractCodes.OperationStatus.COMMITTED))
                {
                    FinishDone(session, now);
                    return;
                }
            }
            else if (IsDefiniteRejection(in response, payload))
            {
                RejectToEditing(session, ReasonOf(in response, payload), now);
                return;
            }

            BeginResolving(session, now);
        }

        private void OnResolveResponse(TradeSession session, in BackendResponse response, TradeExecutionResponse payload, double now)
        {
            if ((response.IsSucceeded) && (TradeApiRules.TryReadState(payload, out string state, out int reasonCode)))
            {
                session.ResolveFailures = 0;
                if ((session.PrepareGaveUp) && (state != TradeCodes.ExecutionState.COMMITTED)
                    && (state != TradeCodes.ExecutionState.REJECTED) && (state != TradeCodes.ExecutionState.ABANDONED))
                {
                    // prepare를 포기한 뒤의 단 한 번의 확인이다. 아직 끝나지 않았으면 API가 준비 유효 시간으로 정리한다
                    RefreshSnapshotsOf(session);
                    FinishFailed(session, ERejectReason.BackendUnavailable, now);
                    return;
                }

                ApplyExecutionState(session, state, reasonCode, now);
                return;
            }

            ++session.ResolveFailures;
            if ((session.PrepareGaveUp) || (session.ResolveFailures >= TradeSettings.RESOLVE_MAX_FAILURES))
            {
                // 끝내 결과를 알 수 없다. 거래를 마치고 양쪽이 스냅숏으로 실제 재고를 확인하게 한다
                RefreshSnapshotsOf(session);
                FinishFailed(session, ERejectReason.BackendUnavailable, now);
                return;
            }

            ScheduleResolve(session, now);
        }

        private void RefreshSnapshotsOf(TradeSession session)
        {
            if (_refreshSnapshots == null)
            {
                return;
            }

            for (int side = 0; side < TradeSession.SIDE_COUNT; ++side)
            {
                if (session.IsConnected(side))
                {
                    _refreshSnapshots(session.MemberAt(side));
                }
            }
        }

        private static bool IsAbandoned(in BackendResponse response, TradeExecutionResponse payload)
        {
            if ((payload?.Execution != null) && (payload.Execution.State == TradeCodes.ExecutionState.ABANDONED))
            {
                return true;
            }

            return ((payload?.Result != null) && (payload.Result.ReasonCode == ContractCodes.RejectReason.TRADE_EXPIRED))
                || (response.ReasonCode == ContractCodes.RejectReason.TRADE_EXPIRED);
        }

        // 실행 행의 상태로 거래를 마무리한다. Prepared면 아직 결과가 없어 계속 묻는다
        private void ApplyExecutionState(TradeSession session, string state, int reasonCode, double now)
        {
            switch (state)
            {
                case TradeCodes.ExecutionState.COMMITTED:
                    FinishDone(session, now);
                    return;
                case TradeCodes.ExecutionState.REJECTED:
                    RejectToEditing(session, TownRejectRules.FromApi(reasonCode, ERejectReason.TradeItemChanged), now);
                    return;
                case TradeCodes.ExecutionState.ABANDONED:
                    FinishFailed(session, ERejectReason.TradeExpired, now);
                    return;
                default:
                    if (session.CommitPhase == ETradeCommitPhase.Resolving)
                    {
                        ScheduleResolve(session, now);
                    }
                    else
                    {
                        BeginResolving(session, now);
                    }

                    return;
            }
        }

        private void BeginResolving(TradeSession session, double now)
        {
            session.CommitPhase     = ETradeCommitPhase.Resolving;
            session.ResolveAttempts = 0;
            session.NextActionAt    = now + TradeApiRules.ResolveDelay(0);
        }

        private void ScheduleResolve(TradeSession session, double now)
        {
            ++session.ResolveAttempts;
            session.NextActionAt = now + TradeApiRules.ResolveDelay(session.ResolveAttempts);
        }

        private void RejectToEditing(TradeSession session, ERejectReason reason, double now)
        {
            session.CompleteRejected(reason, now);
            BroadcastState(session, now);
        }

        private void FinishDone(TradeSession session, double now)
        {
            session.CompleteDone();
            for (int side = 0; side < TradeSession.SIDE_COUNT; ++side)
            {
                if (session.IsConnected(side))
                {
                    _tradeCommitted?.Invoke(session.MemberAt(side).ParticipantId);
                }
            }

            Finish(session, now);
        }

        private void FinishFailed(TradeSession session, ERejectReason reason, double now)
        {
            session.CompleteFailed(reason);
            Finish(session, now);
        }

        private static bool IsDefiniteRejection(in BackendResponse response, TradeExecutionResponse payload)
        {
            return (TownRejectRules.IsDefiniteRejection(in response))
                || ((payload?.Result != null) && (payload.Result.Status == ContractCodes.OperationStatus.REJECTED));
        }

        private static ERejectReason ReasonOf(in BackendResponse response, TradeExecutionResponse payload)
        {
            return TownRejectRules.ReasonOf(in response, payload?.Result, ERejectReason.TradeItemChanged);
        }

        private void Reply(int participantId, in TradeRequestMessage message, Guid tradeId, ERejectReason reason)
        {
            _audience.Send(participantId, new TradeResultMessage(message.RequestId, tradeId, message.Action, reason));
        }
    }
}
