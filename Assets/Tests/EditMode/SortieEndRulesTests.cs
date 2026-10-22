// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class SortieEndRulesTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [TestCase(false, ESessionSettlementState.Settled, true, ESessionEndReason.Success, true)]
        [TestCase(true, ESessionSettlementState.Settled, true, ESessionEndReason.Success, false)]
        [TestCase(false, ESessionSettlementState.InFlight, true, ESessionEndReason.Success, false)]
        [TestCase(false, ESessionSettlementState.Retrying, true, ESessionEndReason.Success, false)]
        [TestCase(false, ESessionSettlementState.Abandoned, true, ESessionEndReason.Success, false)]
        [TestCase(false, ESessionSettlementState.Settled, false, ESessionEndReason.Success, false)]
        [TestCase(false, ESessionSettlementState.Settled, true, ESessionEndReason.Timeout, false)]
        [TestCase(false, ESessionSettlementState.Settled, true, ESessionEndReason.AllDead, false)]
        public void ShouldStartAuction_Conditions_ReturnsExpected(bool hasAuction, ESessionSettlementState settlement,
            bool isBossSession, ESessionEndReason endReason, bool expected)
        {
            Assert.AreEqual(expected, SortieEndRules.ShouldStartAuction(hasAuction, settlement, isBossSession, endReason));
        }

        [Test]
        public void CanReturnAfterEnd_EndNotStored_ReturnsFalseForEverySettlementState()
        {
            foreach (ESessionSettlementState settlement in System.Enum.GetValues(typeof(ESessionSettlementState)))
            {
                Assert.IsFalse(SortieEndRules.CanReturnAfterEnd(false, ESessionEndReason.ServiceCancelled, settlement,
                    false, false, false));
                Assert.IsFalse(SortieEndRules.CanReturnAfterEnd(false, ESessionEndReason.Timeout, settlement,
                    false, false, false));
            }
        }

        [Test]
        public void CanReturnAfterEnd_ServiceCancelledStored_ReturnsTrue()
        {
            Assert.IsTrue(SortieEndRules.CanReturnAfterEnd(true, ESessionEndReason.ServiceCancelled,
                ESessionSettlementState.Idle, false, false, true));
        }

        [Test]
        public void CanReturnAfterEnd_SettlementAbandonedStored_ReturnsTrue()
        {
            Assert.IsTrue(SortieEndRules.CanReturnAfterEnd(true, ESessionEndReason.Timeout,
                ESessionSettlementState.Abandoned, false, false, false));
        }

        [TestCase(ESessionSettlementState.Idle)]
        [TestCase(ESessionSettlementState.InFlight)]
        [TestCase(ESessionSettlementState.Retrying)]
        public void CanReturnAfterEnd_SettlementNotConfirmed_ReturnsFalse(ESessionSettlementState settlement)
        {
            Assert.IsFalse(SortieEndRules.CanReturnAfterEnd(true, ESessionEndReason.Timeout, settlement, false, false, false));
        }

        [Test]
        public void CanReturnAfterEnd_SettledWithAuction_FollowsAuctionFinish()
        {
            Assert.IsFalse(SortieEndRules.CanReturnAfterEnd(true, ESessionEndReason.Success, ESessionSettlementState.Settled,
                true, false, true));
            Assert.IsTrue(SortieEndRules.CanReturnAfterEnd(true, ESessionEndReason.Success, ESessionSettlementState.Settled,
                true, true, true));
        }

        [Test]
        public void CanReturnAfterEnd_BossSuccessSettledBeforeAuctionCreated_ReturnsFalse()
        {
            Assert.IsFalse(SortieEndRules.CanReturnAfterEnd(true, ESessionEndReason.Success, ESessionSettlementState.Settled,
                false, false, true));
        }

        [TestCase(ESessionEndReason.Success, false, true)]
        [TestCase(ESessionEndReason.Timeout, true, true)]
        [TestCase(ESessionEndReason.AllDead, true, true)]
        [TestCase(ESessionEndReason.Success, true, false)]
        public void CanReturnAfterEnd_SettledWithoutAuction_ReturnsExpected(ESessionEndReason endReason, bool isBossSession,
            bool expected)
        {
            Assert.AreEqual(expected, SortieEndRules.CanReturnAfterEnd(true, endReason, ESessionSettlementState.Settled,
                false, false, isBossSession));
        }

        [Test]
        public void DecideTerminal_ServiceCancelled_WaitsForStoredEndUnlessStorageStopped()
        {
            Assert.AreEqual(ESortieTerminalReason.None, SortieEndRules.DecideTerminal(ESessionEndReason.ServiceCancelled,
                false, false, ESessionSettlementState.Idle, false));
            Assert.AreEqual(ESortieTerminalReason.ServiceCancelled, SortieEndRules.DecideTerminal(
                ESessionEndReason.ServiceCancelled, false, true, ESessionSettlementState.Idle, false));
            Assert.AreEqual(ESortieTerminalReason.ServiceCancelled, SortieEndRules.DecideTerminal(
                ESessionEndReason.ServiceCancelled, true, false, ESessionSettlementState.Idle, false));
        }

        [Test]
        public void DecideTerminal_StorageStoppedBeforeEnd_ReturnsServiceCancelled()
        {
            Assert.AreEqual(ESortieTerminalReason.ServiceCancelled, SortieEndRules.DecideTerminal(ESessionEndReason.Timeout,
                true, false, ESessionSettlementState.Settled, false));
        }

        [Test]
        public void DecideTerminal_Settled_WaitsForAuctionToFinish()
        {
            Assert.AreEqual(ESortieTerminalReason.None, SortieEndRules.DecideTerminal(ESessionEndReason.Success,
                false, true, ESessionSettlementState.Settled, true));
            Assert.AreEqual(ESortieTerminalReason.Settled, SortieEndRules.DecideTerminal(ESessionEndReason.Success,
                false, true, ESessionSettlementState.Settled, false));
        }

        [Test]
        public void DecideTerminal_SettlementAbandoned_ReturnsSettlementAbandoned()
        {
            Assert.AreEqual(ESortieTerminalReason.SettlementAbandoned, SortieEndRules.DecideTerminal(
                ESessionEndReason.Timeout, false, true, ESessionSettlementState.Abandoned, false));
        }

        [TestCase(ESessionSettlementState.Idle)]
        [TestCase(ESessionSettlementState.InFlight)]
        [TestCase(ESessionSettlementState.Retrying)]
        public void DecideTerminal_SettlementPending_ReturnsNone(ESessionSettlementState settlement)
        {
            Assert.AreEqual(ESortieTerminalReason.None, SortieEndRules.DecideTerminal(ESessionEndReason.Timeout,
                false, true, settlement, false));
        }

        [Test]
        public void DecidePhase_Combinations_ReturnsExpected()
        {
            Assert.AreEqual(ESortiePhase.Closed, SortieEndRules.DecidePhase(true, true, true, true));
            Assert.AreEqual(ESortiePhase.Waiting, SortieEndRules.DecidePhase(false, false, false, false));
            Assert.AreEqual(ESortiePhase.Running, SortieEndRules.DecidePhase(false, true, true, false));
            Assert.AreEqual(ESortiePhase.Auction, SortieEndRules.DecidePhase(false, true, false, true));
            Assert.AreEqual(ESortiePhase.Ended, SortieEndRules.DecidePhase(false, true, false, false));
        }

        [TestCase(ESessionSettlementState.Settled, ESortieSettlementState.Settled)]
        [TestCase(ESessionSettlementState.Abandoned, ESortieSettlementState.Deferred)]
        [TestCase(ESessionSettlementState.Idle, ESortieSettlementState.Pending)]
        [TestCase(ESessionSettlementState.InFlight, ESortieSettlementState.Pending)]
        [TestCase(ESessionSettlementState.Retrying, ESortieSettlementState.Pending)]
        public void DecideSettlementState_State_ReturnsClientState(ESessionSettlementState settlement,
            ESortieSettlementState expected)
        {
            Assert.AreEqual(expected, SortieEndRules.DecideSettlementState(settlement));
        }
    }
}
