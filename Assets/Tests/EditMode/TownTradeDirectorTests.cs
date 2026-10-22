// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class TownTradeDirectorTests
    {
        // 거래 감독: 요청 거리, 같은 계정, 수락, 거리, 시간 취소, prepare -> commit 성공, 확정 거절 뒤 편집 복귀와 새 executionId,
        // Committing 중 취소 차단과 연결 끊김, commit 응답 불명 뒤 resolve 폴링

        /****************************************
        *                Fields
        ****************************************/

        private const int    STATUS_OK       = 200;
        private const int    STATUS_REJECTED = 409;
        private const int    STATUS_DOWN     = 503;
        private const int    STATUS_NOT_FOUND = 404;
        private const string INSTANCE_ID     = "3a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";

        private double                     _now;
        private volatile string            _prepareMode;
        private volatile string            _commitMode;
        private volatile string            _resolveState;
        private volatile string            _resolveMode;
        private ConcurrentQueue<string>    _paths;
        private ConcurrentQueue<string>    _prepareIds;
        private FakeBackendTransport       _transport;
        private BackendGateway             _gateway;
        private FakeTownAudience           _audience;
        private TownSnapshotTap            _tap;
        private Dictionary<string, TownMember> _byName;
        private List<int>                  _committed;
        private TownTradeDirector          _director;
        private TownMember                 _alpha;
        private TownMember                 _beta;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _now          = 1000.0;
            _prepareMode  = "ok";
            _commitMode   = "ok";
            _resolveState = TradeCodes.ExecutionState.PREPARED;
            _resolveMode  = "ok";
            _paths        = new ConcurrentQueue<string>();
            _prepareIds   = new ConcurrentQueue<string>();
            _transport    = new FakeTownTradeTransport(this).Create();
            _gateway      = new BackendGateway(_transport, () => _now, FakeBackendTransport.ImmediateDelay(new ConcurrentQueue<TimeSpan>()),
                null, null);
            _audience     = new FakeTownAudience();
            _tap          = new TownSnapshotTap(_audience);
            _byName       = new Dictionary<string, TownMember>();
            _committed    = new List<int>();
            _alpha        = new TownMember(1, "acc-a", "11111111-0000-4000-8000-000000000001", "lease-a", "Alpha", EPlayerClass.Warrior);
            _beta         = new TownMember(2, "acc-b", "11111111-0000-4000-8000-000000000002", "lease-b", "Beta", EPlayerClass.Sorcerer);
            _byName["Alpha"] = _alpha;
            _byName["Beta"]  = _beta;
            _audience.SetPosition(1, Vector3.zero);
            _audience.SetPosition(2, new Vector3(3f, 0f, 0f));
            _director = new TownTradeDirector(_gateway, _tap, new NameDirectory(_byName), _tap, INSTANCE_ID, null, null,
                id => _committed.Add(id), () => _now);
            _director.AddMember(_alpha);
            _director.AddMember(_beta);
        }

        [TearDown]
        public void TearDown()
        {
            _gateway.Dispose();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Request_BeyondRequestDistance_RejectsTooFar()
        {
            _audience.SetPosition(2, new Vector3(TradeSettings.REQUEST_DISTANCE + 1f, 0f, 0f));

            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));

            Assert.IsTrue(_audience.TryGetLast(1, out TradeResultMessage result));
            Assert.AreEqual(ERejectReason.TradeTooFar, result.Reason);
            Assert.AreEqual(0, _audience.Count<TradeInviteMessage>(2));
        }

        [Test]
        public void Request_SameAccount_RejectsTradeSameAccount()
        {
            var twin = new TownMember(3, "acc-a", "11111111-0000-4000-8000-000000000003", "lease-c", "Twin", EPlayerClass.Warrior);
            _byName["Twin"] = twin;
            _audience.SetPosition(3, Vector3.zero);
            _director.AddMember(twin);

            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Twin"));

            Assert.IsTrue(_audience.TryGetLast(1, out TradeResultMessage result));
            Assert.AreEqual(ERejectReason.TradeSameAccount, result.Reason);
        }

        [Test]
        public void Respond_Accept_OpensEditingForBoth()
        {
            Guid tradeId = StartTrade();

            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage mine));
            Assert.IsTrue(_audience.TryGetLast(2, out TradeStateMessage theirs));
            Assert.AreEqual(ETradeState.Editing, mine.State);
            Assert.AreEqual(ETradeState.Editing, theirs.State);
            Assert.AreEqual(tradeId, mine.TradeId);
            Assert.AreEqual("Beta", mine.PartnerName);
        }

        [Test]
        public void Respond_AfterExpiry_RejectsRequestExpired()
        {
            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));
            Assert.IsTrue(_audience.TryGetLast(2, out TradeInviteMessage invite));
            _now += TradeSettings.REQUEST_EXPIRE_SECONDS + 1.0;

            _director.Handle(_beta, Message(ETradeAction.Respond, invite.TradeId, null, true));

            Assert.IsTrue(_audience.TryGetLast(2, out TradeResultMessage result));
            Assert.AreEqual(ERejectReason.TradeRequestExpired, result.Reason);
            Assert.AreEqual(0, _director.SessionCount);
        }

        [Test]
        public void Tick_WhenPartnersMoveBeyondCancelDistance_CancelsWithTooFar()
        {
            StartTrade();
            _audience.SetPosition(2, new Vector3(TradeSettings.CANCEL_DISTANCE + 1f, 0f, 0f));
            _now += TradeSettings.CHECK_INTERVAL_SECONDS + 0.1;

            _director.Tick();

            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Cancelled, state.State);
            Assert.AreEqual(ERejectReason.TradeTooFar, state.Reason);
            Assert.AreEqual(0, _director.SessionCount);
        }

        [Test]
        public void Tick_AfterFiveIdleMinutes_CancelsTradeWithExpired()
        {
            StartTrade();
            _now += TradeSettings.IDLE_CANCEL_SECONDS + 1.0;

            _director.Tick();

            Assert.IsTrue(_audience.TryGetLast(2, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Cancelled, state.State);
            Assert.AreEqual(ERejectReason.TradeExpired, state.Reason);
        }

        [Test]
        public void RemoveMember_WhileEditing_CancelsForPartner()
        {
            StartTrade();

            _director.RemoveMember(2);

            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Cancelled, state.State);
            Assert.AreEqual(0, _director.SessionCount);
        }

        [Test]
        public void SetOffer_ByEitherSide_UnlocksBothAndKeepsStaleAcceptOut()
        {
            Guid tradeId = StartTrade();
            LockBoth(tradeId);
            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage locked));
            Assert.AreEqual(ETradeState.Countdown, locked.State);

            _director.Handle(_beta, Message(ETradeAction.SetOffer, tradeId, null, false, Herb(2), 0, locked.OfferRevision,
                locked.ConfirmGeneration));
            _now += TradeSettings.COUNTDOWN_SECONDS + 0.1;
            _director.Tick();
            _director.Handle(_alpha, Message(ETradeAction.Accept, tradeId, null, false, null, 0, locked.OfferRevision,
                locked.ConfirmGeneration));

            Assert.IsTrue(_audience.TryGetLast(1, out TradeResultMessage result));
            Assert.AreEqual(ERejectReason.StaleRevision, result.Reason);
            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Editing, state.State);
            Assert.IsFalse(state.MyLocked);
        }

        [Test]
        public void Commit_PrepareThenCommitSucceeds_FinishesDoneAndNotifiesBoth()
        {
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);

            PumpResults(2);

            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Done, state.State);
            Assert.AreEqual(0, _director.SessionCount);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, _committed);
            Assert.IsTrue(_paths.TryDequeue(out string first));
            Assert.AreEqual(TradeRoutes.PREPARE, first);
            Assert.IsTrue(_paths.TryDequeue(out string second));
            StringAssert.EndsWith("/commit", second);
        }

        [Test]
        public void Commit_RejectedByCommit_ReturnsToEditingAndNextPrepareUsesNewExecutionId()
        {
            _commitMode = "reject";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);
            PumpResults(2);

            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Editing, state.State);
            Assert.AreEqual(ERejectReason.TradeItemChanged, state.Reason);
            Assert.AreEqual(1, _director.SessionCount);

            _commitMode = "ok";
            ReachCommitting(tradeId);
            Pump();

            Assert.AreEqual(2, _prepareIds.Count);
            _prepareIds.TryDequeue(out string firstId);
            _prepareIds.TryDequeue(out string secondId);
            Assert.AreNotEqual(firstId, secondId);
        }

        [Test]
        public void Committing_CancelIsBlockedAndDisconnectKeepsSessionUntilResult()
        {
            _prepareMode = "down";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);

            _director.Handle(_alpha, Message(ETradeAction.Cancel, tradeId, null));
            Assert.IsTrue(_audience.TryGetLast(1, out TradeResultMessage cancel));
            Assert.AreEqual(ERejectReason.PendingCommit, cancel.Reason);

            _director.RemoveMember(2);
            Assert.AreEqual(1, _director.SessionCount);

            _audience.Clear();
            Pump();
            _prepareMode = "ok";
            _now += TradeSettings.PREPARE_RETRY_SECONDS + 0.1;
            _director.Tick();
            PumpResults(2);

            Assert.AreEqual(0, _director.SessionCount);
            Assert.AreEqual(0, _audience.Count<TradeStateMessage>(2));
            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Done, state.State);
        }

        [Test]
        public void Commit_UnknownResult_PollsResolveUntilCommitted()
        {
            _commitMode = "down";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);
            PumpResults(2);

            Assert.AreEqual(1, _director.SessionCount);
            _resolveState = TradeCodes.ExecutionState.PREPARED;
            _now += TradeSettings.RESOLVE_FIRST_SECONDS + 0.1;
            _director.Tick();
            Pump();
            Assert.AreEqual(1, _director.SessionCount);

            _resolveState = TradeCodes.ExecutionState.COMMITTED;
            _now += TradeSettings.RESOLVE_SECOND_SECONDS + 0.1;
            _director.Tick();
            Pump();

            Assert.AreEqual(0, _director.SessionCount);
            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Done, state.State);
            int resolves = 0;
            foreach (string path in _paths)
            {
                if (path.EndsWith("/resolve", StringComparison.Ordinal))
                {
                    ++resolves;
                }
            }

            Assert.AreEqual(2, resolves);
        }

        [Test]
        public void Commit_AbandonedByResolve_FailsTheTrade()
        {
            _commitMode = "down";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);
            PumpResults(2);
            _resolveState = TradeCodes.ExecutionState.ABANDONED;
            _now += TradeSettings.RESOLVE_FIRST_SECONDS + 0.1;
            _director.Tick();
            Pump();

            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Failed, state.State);
            Assert.AreEqual(0, _director.SessionCount);
        }

        [Test]
        public void IsCommittingAccount_TrueOnlyWhileCommittingAndSurvivesReconnect()
        {
            _prepareMode = "down";
            Guid tradeId = StartTrade();
            Assert.IsFalse(_director.IsCommittingAccount("acc-a"));

            ReachCommitting(tradeId);
            Assert.IsTrue(_director.IsCommittingAccount("acc-a"));
            Assert.IsTrue(_director.IsCommittingAccount("acc-b"));
            Assert.IsFalse(_director.IsCommittingAccount("acc-z"));
            Assert.IsFalse(_director.IsCommittingAccount(null));

            // 재접속해 연결 번호가 바뀌어도 세션이 남아 있다
            _director.RemoveMember(1);
            Assert.IsTrue(_director.IsCommittingAccount("acc-a"));

            // 실패한 준비 요청은 스레드 풀에서 재시도하므로 결과가 큐에 들어올 때까지 기다려 한 건만 전달한다.
            // 이 경계 전에 모드를 바꾸면 재시도가 새 모드를 읽는 경합이 생긴다
            Pump();
            Assert.IsTrue(_director.IsCommittingAccount("acc-a"), "준비 결과가 불명이면 확정 중이 유지돼야 한다");
            Assert.AreEqual(0, _gateway.InFlightCount);

            // 다음 재시도 시각은 불명 결과를 받은 시계 기준이라 시계를 올리는 일은 그 뒤에 한다
            _prepareMode = "ok";
            _now += TradeSettings.PREPARE_RETRY_SECONDS + 0.1;
            _director.Tick();
            PumpUntil(() => !_director.IsCommittingAccount("acc-a"));
            Assert.IsFalse(_director.IsCommittingAccount("acc-a"));
        }

        [Test]
        public void Request_ByAccountWithCommittingSession_RejectsPendingCommit()
        {
            _prepareMode = "down";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);
            _director.RemoveMember(1);

            var again = new TownMember(4, "acc-a", "11111111-0000-4000-8000-000000000004", "lease-d", "Alpha", EPlayerClass.Warrior);
            var gamma = new TownMember(5, "acc-c", "11111111-0000-4000-8000-000000000005", "lease-e", "Gamma", EPlayerClass.Warrior);
            _byName["Alpha"] = again;
            _byName["Gamma"] = gamma;
            _audience.SetPosition(4, Vector3.zero);
            _audience.SetPosition(5, Vector3.zero);
            _director.AddMember(again);
            _director.AddMember(gamma);

            _director.Handle(again, Message(ETradeAction.Request, Guid.Empty, "Gamma"));

            Assert.IsTrue(_audience.TryGetLast(4, out TradeResultMessage result));
            Assert.AreEqual(ERejectReason.PendingCommit, result.Reason);
            Assert.AreEqual(0, _audience.Count<TradeInviteMessage>(5));
        }

        [Test]
        public void Commit_AbandonedInRejectedBody_FailsWithTradeExpired()
        {
            _commitMode = "abandoned";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);
            PumpResults(2);

            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Failed, state.State);
            Assert.AreEqual(ERejectReason.TradeExpired, state.Reason);
            Assert.AreEqual(0, _director.SessionCount);
        }

        [Test]
        public void Commit_NotFound_ResolvesInsteadOfRejectingToEditing()
        {
            _commitMode = "notfound";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);
            PumpResults(2);

            Assert.AreEqual(1, _director.SessionCount);
            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreNotEqual(ETradeState.Editing, state.State);

            _resolveState = TradeCodes.ExecutionState.COMMITTED;
            _now += TradeSettings.RESOLVE_FIRST_SECONDS + 0.1;
            _director.Tick();
            Pump();

            Assert.AreEqual(0, _director.SessionCount);
            Assert.IsTrue(_audience.TryGetLast(1, out state));
            Assert.AreEqual(ETradeState.Done, state.State);
        }

        [Test]
        public void Prepare_UnknownUntilGiveUp_AsksResolveOnceAndFollowsItsState()
        {
            _prepareMode = "down";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);
            Pump();

            _resolveState = TradeCodes.ExecutionState.COMMITTED;
            _now += TradeSettings.PREPARE_GIVE_UP_SECONDS + 1.0;
            _director.Tick();
            Pump();
            Assert.AreEqual(1, _director.SessionCount);

            // 연속 실패로 열린 회로가 닫힌 뒤에 묻는다
            _now += 11.0;
            _director.Tick();
            Pump();

            Assert.AreEqual(0, _director.SessionCount);
            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Done, state.State);
            Assert.AreEqual(1, CountPaths("/resolve"));
        }

        [Test]
        public void Prepare_UnknownUntilGiveUp_ResolveFailureFailsWithBackendUnavailable()
        {
            _prepareMode = "down";
            _resolveMode = "down";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);
            Pump();
            _now += TradeSettings.PREPARE_GIVE_UP_SECONDS + 1.0;
            _director.Tick();
            Pump();
            _director.Tick();
            Pump();

            Assert.AreEqual(0, _director.SessionCount);
            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Failed, state.State);
            Assert.AreEqual(ERejectReason.BackendUnavailable, state.Reason);
        }

        [Test]
        public void Resolve_FailingTooManyTimes_FailsWithBackendUnavailableAndRefreshesSnapshots()
        {
            var refreshed = new List<int>();
            _director = new TownTradeDirector(_gateway, _tap, new NameDirectory(_byName), _tap, INSTANCE_ID, null, null,
                id => _committed.Add(id), () => _now, member => refreshed.Add(member.ParticipantId));
            _director.AddMember(_alpha);
            _director.AddMember(_beta);
            _commitMode  = "down";
            _resolveMode = "down";
            Guid tradeId = StartTrade();
            ReachCommitting(tradeId);
            PumpResults(2);
            refreshed.Clear();

            // 회로가 열려도 응답은 실패로 돌아오므로 반복 횟수로 센다
            int rounds = 0;
            while ((rounds < (TradeSettings.RESOLVE_MAX_FAILURES + 5)) && (_director.SessionCount > 0))
            {
                ++rounds;
                _now += TradeSettings.RESOLVE_REPEAT_SECONDS + 0.1;
                _director.Tick();
                Pump();
            }

            Assert.AreEqual(0, _director.SessionCount);
            Assert.AreEqual(TradeSettings.RESOLVE_MAX_FAILURES, rounds);
            Assert.IsTrue(_audience.TryGetLast(1, out TradeStateMessage state));
            Assert.AreEqual(ETradeState.Failed, state.State);
            Assert.AreEqual(ERejectReason.BackendUnavailable, state.Reason);
            CollectionAssert.Contains(refreshed, 1);
            CollectionAssert.Contains(refreshed, 2);
        }

        [Test]
        public void Request_RepeatedWhilePending_DoesNotResendInvite()
        {
            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));
            Assert.IsTrue(_audience.TryGetLast(2, out TradeInviteMessage invite));
            _now += 1.0;

            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));

            Assert.AreEqual(1, _audience.Count<TradeInviteMessage>(2));
            Assert.IsTrue(_audience.TryGetLast(1, out TradeResultMessage result));
            Assert.AreEqual(ERejectReason.None, result.Reason);
            Assert.AreEqual(invite.TradeId, result.TradeId);
            Assert.AreEqual(1, _director.RequestCount);
        }

        [Test]
        public void Request_AfterDecline_IsCooledDownForTenSeconds()
        {
            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));
            Assert.IsTrue(_audience.TryGetLast(2, out TradeInviteMessage invite));
            _director.Handle(_beta, Message(ETradeAction.Respond, invite.TradeId, null, false));
            _now += 1.0;

            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));
            Assert.IsTrue(_audience.TryGetLast(1, out TradeResultMessage blocked));
            Assert.AreEqual(ERejectReason.RateLimited, blocked.Reason);
            Assert.AreEqual(1, _audience.Count<TradeInviteMessage>(2));

            _now += TradeSettings.REQUEST_COOLDOWN_SECONDS;
            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));
            Assert.IsTrue(_audience.TryGetLast(1, out TradeResultMessage allowed));
            Assert.AreEqual(ERejectReason.None, allowed.Reason);
            Assert.AreEqual(2, _audience.Count<TradeInviteMessage>(2));
        }

        [Test]
        public void Request_AfterExpiry_IsCooledDown()
        {
            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));
            _now += TradeSettings.REQUEST_EXPIRE_SECONDS + 0.1;
            _director.Tick();
            _now += 1.0;

            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));

            Assert.IsTrue(_audience.TryGetLast(1, out TradeResultMessage result));
            Assert.AreEqual(ERejectReason.RateLimited, result.Reason);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private int CountPaths(string suffix)
        {
            int count = 0;
            foreach (string path in _paths)
            {
                if (path.EndsWith(suffix, StringComparison.Ordinal))
                {
                    ++count;
                }
            }

            return count;
        }

        private Guid StartTrade()
        {
            _director.Handle(_alpha, Message(ETradeAction.Request, Guid.Empty, "Beta"));
            Assert.IsTrue(_audience.TryGetLast(2, out TradeInviteMessage invite));
            _director.Handle(_beta, Message(ETradeAction.Respond, invite.TradeId, null, true));
            return invite.TradeId;
        }

        private void LockBoth(Guid tradeId)
        {
            _audience.TryGetLast(1, out TradeStateMessage state);
            _director.Handle(_alpha, Message(ETradeAction.Lock, tradeId, null, false, null, 0, state.OfferRevision, state.ConfirmGeneration));
            _director.Handle(_beta, Message(ETradeAction.Lock, tradeId, null, false, null, 0, state.OfferRevision, state.ConfirmGeneration));
        }

        // 양쪽 제안을 올리고 확정, 대기, 양쪽 수락까지 진행한다
        private void ReachCommitting(Guid tradeId)
        {
            _audience.TryGetLast(1, out TradeStateMessage state);
            _director.Handle(_alpha, Message(ETradeAction.SetOffer, tradeId, null, false, Herb(3), 10, state.OfferRevision, state.ConfirmGeneration));
            LockBoth(tradeId);
            _now += TradeSettings.COUNTDOWN_SECONDS + 0.1;
            _director.Tick();
            _audience.TryGetLast(1, out state);
            _director.Handle(_alpha, Message(ETradeAction.Accept, tradeId, null, false, null, 0, state.OfferRevision, state.ConfirmGeneration));
            _director.Handle(_beta, Message(ETradeAction.Accept, tradeId, null, false, null, 0, state.OfferRevision, state.ConfirmGeneration));
        }

        private void Pump()
        {
            FakeBackendTransport.WaitForCompleted(_gateway, 1);
            _gateway.Pump();
        }

        // 준비 성공 뒤 commit 같이 콜백이 다음 요청을 보내는 흐름은 한 Pump이 두 결과를 함께 소비할 수 있어, Pump 횟수 대신 전달 건수로 맞춘다
        private void PumpResults(int expected)
        {
            int delivered = 0;
            while (delivered < expected)
            {
                FakeBackendTransport.WaitForCompleted(_gateway, 1);
                delivered += _gateway.Pump();
            }

            Assert.AreEqual(expected, delivered, "예상보다 많은 결과가 한꺼번에 전달됐다");
        }

        // Pump 한 번이 콜백에서 보낸 다음 요청의 결과까지 소비할 수 있어, 결과 개수 대신 완료 조건으로 멈춘다
        private void PumpUntil(Func<bool> isDone)
        {
            const int MAX_ROUNDS = 8;

            for (int round = 0; round < MAX_ROUNDS; ++round)
            {
                if (isDone())
                {
                    return;
                }

                Pump();
            }

            Assert.IsTrue(isDone(), $"결과 {MAX_ROUNDS}번을 전달해도 완료 조건이 충족되지 않았다");
        }

        private static TradeOfferLine[] Herb(int quantity)
        {
            return new[] { new TradeOfferLine(MarketTradeRules.HERB_ID, null, quantity) };
        }

        private static TradeRequestMessage Message(ETradeAction action, Guid tradeId, string target, bool accept = false,
            TradeOfferLine[] lines = null, long currency = 0, int revision = 0, int generation = 0)
        {
            return new TradeRequestMessage(Guid.NewGuid(), tradeId, action, target, accept, lines, currency, revision, generation);
        }

        private BackendTransportResult Respond(BackendRequest request)
        {
            _paths.Enqueue(request.Path);
            string id = request.OperationId ?? Guid.NewGuid().ToString("D");
            if (request.Path == TradeRoutes.PREPARE)
            {
                _prepareIds.Enqueue(request.OperationId);
                return Execution(_prepareMode, id, TradeCodes.ExecutionState.PREPARED);
            }

            if (request.Path.EndsWith("/commit", StringComparison.Ordinal))
            {
                return Execution(_commitMode, id, TradeCodes.ExecutionState.COMMITTED);
            }

            return Execution(_resolveMode, id, _resolveState);
        }

        private static BackendTransportResult Execution(string mode, string operationId, string okState)
        {
            if (mode == "down")
            {
                return new BackendTransportResult(STATUS_DOWN, null);
            }

            if (mode == "notfound")
            {
                return new BackendTransportResult(STATUS_NOT_FOUND, null);
            }

            if (mode == "abandoned")
            {
                var expired = new OperationResult(operationId, ContractCodes.OperationStatus.REJECTED,
                    (int)ERejectReason.TradeExpired, 0, 0, string.Empty);
                var aborted = new TradeExecutionView(operationId, operationId, TradeCodes.ExecutionState.ABANDONED,
                    (int)ERejectReason.TradeExpired, 0, 0, 0);
                return new BackendTransportResult(STATUS_REJECTED, BackendJson.Serialize(new TradeExecutionResponse(expired, aborted)));
            }

            if (mode == "reject")
            {
                var rejected = new OperationResult(operationId, ContractCodes.OperationStatus.REJECTED,
                    (int)ERejectReason.TradeItemChanged, 0, 0, string.Empty);
                var view = new TradeExecutionView(operationId, operationId, TradeCodes.ExecutionState.REJECTED,
                    (int)ERejectReason.TradeItemChanged, 0, 0, 0);
                return new BackendTransportResult(STATUS_REJECTED, BackendJson.Serialize(new TradeExecutionResponse(rejected, view)));
            }

            var committed = new OperationResult(operationId, ContractCodes.OperationStatus.COMMITTED, 0, 1, 0, string.Empty);
            var executed  = new TradeExecutionView(operationId, operationId, okState, 0, 0, 0, 0);
            return new BackendTransportResult(STATUS_OK, BackendJson.Serialize(new TradeExecutionResponse(committed, executed)));
        }

        /****************************************
        *               Records
        ****************************************/

        // 시험이 정한 응답 함수를 전송기로 묶는다
        private sealed class FakeTownTradeTransport
        {
            private readonly TownTradeDirectorTests _owner;

            public FakeTownTradeTransport(TownTradeDirectorTests owner)
            {
                _owner = owner;
            }

            public FakeBackendTransport Create()
            {
                return new FakeBackendTransport((request, token) => Task.FromResult(_owner.Respond(request)));
            }
        }

        private sealed class NameDirectory : ITownMemberDirectory
        {
            private readonly Dictionary<string, TownMember> _members;

            public NameDirectory(Dictionary<string, TownMember> members)
            {
                _members = members;
            }

            public bool TryFindByName(string characterName, out TownMember member)
            {
                return _members.TryGetValue(characterName, out member);
            }
        }
    }
}
