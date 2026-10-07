// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class TradeSessionTests
    {
        // 1:1 거래 상태 기계: 제안 변경은 양쪽 확정을 풀고, 낡은 수락은 무시하며, 대기와 방치 시간, Committing의 취소 차단, 확정 거절 뒤 새 executionId

        /****************************************
        *                Fields
        ****************************************/

        private const double START = 100.0;

        private TradeSession _session;
        private TradeOfferLine[] _lines;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            var a = new TownMember(1, "acc-a", "char-a", "lease-a", "Alpha", EPlayerClass.Warrior);
            var b = new TownMember(2, "acc-b", "char-b", "lease-b", "Beta", EPlayerClass.Sorcerer);
            _session = new TradeSession(Guid.NewGuid(), a, b, START);
            _lines   = new[] { new TradeOfferLine("item_herb", null, 3) };
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void SetOffer_AfterBothLocked_UnlocksBothAndBumpsGenerations()
        {
            LockBoth(START);
            int revision   = _session.OfferRevision;
            int generation = _session.ConfirmGeneration;

            ERejectReason reason = _session.SetOffer(0, _lines, 10, START + 1.0);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual(ETradeState.Editing, _session.State);
            Assert.IsFalse(_session.IsLocked(0));
            Assert.IsFalse(_session.IsLocked(1));
            Assert.AreEqual(revision + 1, _session.OfferRevision);
            Assert.AreEqual(generation + 1, _session.ConfirmGeneration);
        }

        [Test]
        public void Lock_WithStaleValues_ReturnsStaleRevisionAndKeepsState()
        {
            int oldRevision = _session.OfferRevision;
            _session.SetOffer(1, _lines, 0, START);

            ERejectReason reason = _session.Lock(0, oldRevision, _session.ConfirmGeneration, START);

            Assert.AreEqual(ERejectReason.StaleRevision, reason);
            Assert.IsFalse(_session.IsLocked(0));
        }

        [Test]
        public void Accept_BeforeCountdownEnds_IsTooEarly()
        {
            LockBoth(START);

            ERejectReason reason = _session.Accept(0, _session.OfferRevision, _session.ConfirmGeneration, START + 1.0, out bool ready);

            Assert.AreEqual(ERejectReason.TooEarly, reason);
            Assert.IsFalse(ready);
            Assert.AreEqual(ETradeState.Countdown, _session.State);
        }

        [Test]
        public void Accept_WithStaleGeneration_IsIgnoredAfterOfferChange()
        {
            LockBoth(START);
            _session.Tick(START + TradeSettings.COUNTDOWN_SECONDS);
            int staleRevision   = _session.OfferRevision;
            int staleGeneration = _session.ConfirmGeneration;
            _session.SetOffer(1, _lines, 5, START + 6.0);

            ERejectReason reason = _session.Accept(0, staleRevision, staleGeneration, START + 6.0, out bool ready);

            Assert.AreEqual(ERejectReason.StaleRevision, reason);
            Assert.IsFalse(ready);
            Assert.IsFalse(_session.IsAccepted(0));
        }

        [Test]
        public void Tick_AfterCountdown_OpensAccepting()
        {
            LockBoth(START);

            Assert.IsFalse(_session.Tick(START + TradeSettings.COUNTDOWN_SECONDS - 0.1));
            Assert.IsTrue(_session.Tick(START + TradeSettings.COUNTDOWN_SECONDS));
            Assert.AreEqual(ETradeState.Accepting, _session.State);
        }

        [Test]
        public void Lock_AgainWhileCountingDown_DoesNotRestartCountdown()
        {
            LockBoth(START);

            _session.Lock(0, _session.OfferRevision, _session.ConfirmGeneration, START + 3.0);

            Assert.IsTrue(_session.Tick(START + TradeSettings.COUNTDOWN_SECONDS));
        }

        [Test]
        public void Unlock_ReturnsToLockedWhenOtherSideStillLocked()
        {
            LockBoth(START);
            int generation = _session.ConfirmGeneration;

            _session.Unlock(0, START + 1.0);

            Assert.AreEqual(ETradeState.Locked, _session.State);
            Assert.IsFalse(_session.IsLocked(0));
            Assert.IsTrue(_session.IsLocked(1));
            Assert.AreEqual(generation + 1, _session.ConfirmGeneration);
        }

        [Test]
        public void IsIdleExpired_AfterFiveMinutesWithoutChange_IsTrue()
        {
            Assert.IsFalse(_session.IsIdleExpired(START + TradeSettings.IDLE_CANCEL_SECONDS - 1.0));
            Assert.IsTrue(_session.IsIdleExpired(START + TradeSettings.IDLE_CANCEL_SECONDS));

            _session.SetOffer(0, _lines, 0, START + 200.0);

            Assert.IsFalse(_session.IsIdleExpired(START + TradeSettings.IDLE_CANCEL_SECONDS));
        }

        [Test]
        public void Committing_BlocksCancelAndOfferChangeAndUnlock()
        {
            ReachCommitting();

            Assert.IsFalse(_session.TryCancel(ERejectReason.TradeTooFar));
            Assert.AreEqual(ERejectReason.PendingCommit, _session.SetOffer(0, _lines, 1, START + 10.0));
            Assert.AreEqual(ERejectReason.PendingCommit, _session.Unlock(0, START + 10.0));
            Assert.AreEqual(ETradeState.Committing, _session.State);
            Assert.IsFalse(_session.IsIdleExpired(START + 10000.0));
        }

        [Test]
        public void CompleteRejected_ReturnsToEditingAndNextExecutionUsesNewId()
        {
            ReachCommitting();
            Guid firstExecution = _session.ExecutionId;
            Guid firstCommit    = _session.CommitOperationId;

            _session.CompleteRejected(ERejectReason.TradeItemChanged, START + 10.0);

            Assert.AreEqual(ETradeState.Editing, _session.State);
            Assert.AreEqual(ERejectReason.TradeItemChanged, _session.LastReason);
            Assert.IsFalse(_session.IsLocked(0));
            Assert.IsFalse(_session.IsAccepted(0));

            LockBoth(START + 11.0);
            _session.Tick(START + 11.0 + TradeSettings.COUNTDOWN_SECONDS);
            _session.Accept(0, _session.OfferRevision, _session.ConfirmGeneration, START + 17.0, out _);
            _session.Accept(1, _session.OfferRevision, _session.ConfirmGeneration, START + 17.0, out bool ready);

            Assert.IsTrue(ready);
            Assert.AreNotEqual(firstExecution, _session.ExecutionId);
            Assert.AreNotEqual(firstCommit, _session.CommitOperationId);
        }

        [Test]
        public void ToMessage_SwapsMyAndPartnerSides()
        {
            _session.SetOffer(0, _lines, 7, START);

            TradeStateMessage mine    = _session.ToMessage(0, START);
            TradeStateMessage partner = _session.ToMessage(1, START);

            Assert.AreEqual(7, mine.MyCurrency);
            Assert.AreEqual(7, partner.PartnerCurrency);
            Assert.AreEqual("Beta", mine.PartnerName);
            Assert.AreEqual("Alpha", partner.PartnerName);
            Assert.AreEqual(1, partner.PartnerLines.Length);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void LockBoth(double now)
        {
            _session.Lock(0, _session.OfferRevision, _session.ConfirmGeneration, now);
            _session.Lock(1, _session.OfferRevision, _session.ConfirmGeneration, now);
        }

        private void ReachCommitting()
        {
            LockBoth(START);
            _session.Tick(START + TradeSettings.COUNTDOWN_SECONDS);
            _session.Accept(0, _session.OfferRevision, _session.ConfirmGeneration, START + 6.0, out _);
            _session.Accept(1, _session.OfferRevision, _session.ConfirmGeneration, START + 6.0, out bool ready);
            Assert.IsTrue(ready);
            Assert.AreEqual(ETradeState.Committing, _session.State);
        }
    }
}
