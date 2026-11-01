// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class SortieExitRulesTests
    {
        // 던전을 떠날 때의 판정만 확인한다. 실제 포기, 이탈 저장, 마을 재접속은 서버와 API를 띄운 다인 접속 검증이 맡는다

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ShouldReturnToTownOnDisconnect_DungeonWithReturnableResult_ReturnsTrue()
        {
            SortieStatusMessage settled = Status(ESortiePhase.Closed, false, true);

            Assert.IsTrue(SortieExitRules.ShouldReturnToTownOnDisconnect(true, true, in settled));
        }

        [Test]
        public void ShouldReturnToTownOnDisconnect_NotReturnableOrNotDungeon_ReturnsFalse()
        {
            SortieStatusMessage running = Status(ESortiePhase.Running, false, false);
            SortieStatusMessage settled = Status(ESortiePhase.Closed, false, true);

            Assert.IsFalse(SortieExitRules.ShouldReturnToTownOnDisconnect(true, true, in running));
            Assert.IsFalse(SortieExitRules.ShouldReturnToTownOnDisconnect(false, true, in settled));
            Assert.IsFalse(SortieExitRules.ShouldReturnToTownOnDisconnect(true, false, in settled));
        }

        [Test]
        public void ShouldAbandonBeforeLeave_RunningWithoutPersonalClose_ReturnsTrue()
        {
            SortieStatusMessage running = Status(ESortiePhase.Running, false, false);

            Assert.IsTrue(SortieExitRules.ShouldAbandonBeforeLeave(true, true, in running));
        }

        [Test]
        public void ShouldAbandonBeforeLeave_ClosedPersonallyOrNotRunningOrTown_ReturnsFalse()
        {
            SortieStatusMessage departed = Status(ESortiePhase.Running, true, false);
            SortieStatusMessage waiting  = Status(ESortiePhase.Waiting, false, false);
            SortieStatusMessage ended    = Status(ESortiePhase.Ended, false, false);
            SortieStatusMessage running  = Status(ESortiePhase.Running, false, false);

            Assert.IsFalse(SortieExitRules.ShouldAbandonBeforeLeave(true, true, in departed));
            Assert.IsFalse(SortieExitRules.ShouldAbandonBeforeLeave(true, true, in waiting));
            Assert.IsFalse(SortieExitRules.ShouldAbandonBeforeLeave(true, true, in ended));
            Assert.IsFalse(SortieExitRules.ShouldAbandonBeforeLeave(false, true, in running));
            Assert.IsFalse(SortieExitRules.ShouldAbandonBeforeLeave(true, false, in running));
        }

        [Test]
        public void ShouldWaitBeforeLeave_RunningEndedAuctionBeforeReturn_ReturnsTrue()
        {
            SortieStatusMessage running  = Status(ESortiePhase.Running, false, false);
            SortieStatusMessage departed = Status(ESortiePhase.Running, true, false);
            SortieStatusMessage ended    = Status(ESortiePhase.Ended, false, false);
            SortieStatusMessage auction  = Status(ESortiePhase.Auction, false, false);

            Assert.IsTrue(SortieExitRules.ShouldWaitBeforeLeave(true, true, in running));
            Assert.IsTrue(SortieExitRules.ShouldWaitBeforeLeave(true, true, in departed));
            Assert.IsTrue(SortieExitRules.ShouldWaitBeforeLeave(true, true, in ended));
            Assert.IsTrue(SortieExitRules.ShouldWaitBeforeLeave(true, true, in auction));
        }

        [Test]
        public void ShouldWaitBeforeLeave_ReturnableWaitingClosedOrTown_ReturnsFalse()
        {
            SortieStatusMessage returnable = Status(ESortiePhase.Ended, false, true);
            SortieStatusMessage waiting    = Status(ESortiePhase.Waiting, false, false);
            SortieStatusMessage closed     = Status(ESortiePhase.Closed, false, false);
            SortieStatusMessage running    = Status(ESortiePhase.Running, false, false);

            Assert.IsFalse(SortieExitRules.ShouldWaitBeforeLeave(true, true, in returnable));
            Assert.IsFalse(SortieExitRules.ShouldWaitBeforeLeave(true, true, in waiting));
            Assert.IsFalse(SortieExitRules.ShouldWaitBeforeLeave(true, true, in closed));
            Assert.IsFalse(SortieExitRules.ShouldWaitBeforeLeave(false, true, in running));
            Assert.IsFalse(SortieExitRules.ShouldWaitBeforeLeave(true, false, in running));
        }

        [Test]
        public void IsLeaveReady_ReturnableOrNoStatus_ReturnsTrue()
        {
            SortieStatusMessage departed = Status(ESortiePhase.Running, true, true);
            SortieStatusMessage pending  = Status(ESortiePhase.Running, true, false);

            Assert.IsTrue(SortieExitRules.IsLeaveReady(true, in departed));
            Assert.IsTrue(SortieExitRules.IsLeaveReady(false, in pending));
            Assert.IsFalse(SortieExitRules.IsLeaveReady(true, in pending));
        }

        /****************************************
        *                Methods
        ****************************************/

        private static SortieStatusMessage Status(ESortiePhase phase, bool hasPersonalClose, bool canReturn)
        {
            ESessionEndReason personalReason = hasPersonalClose ? ESessionEndReason.Abandon : ESessionEndReason.Success;
            return new SortieStatusMessage(phase, ESessionEndReason.Success, hasPersonalClose, personalReason, canReturn,
                ESortieSettlementState.None);
        }
    }
}
