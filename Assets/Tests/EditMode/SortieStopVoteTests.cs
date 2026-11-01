// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class SortieStopVoteTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    CAPACITY = 4;
        private const double DURATION = 30.0;
        private const double RETRY    = 20.0;
        private const double START    = 100.0;
        private const int    ONE      = 0b0001;
        private const int    TWO      = 0b0011;
        private const int    THREE    = 0b0111;
        private const int    FOUR     = 0b1111;

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(4, 4)]
        public void RequiredFor_IsEveryone(int eligible, int required)
        {
            Assert.AreEqual(required, SortieStopVote.RequiredFor(eligible));
        }

        [Test]
        public void Solo_Agree_PassesImmediately()
        {
            SortieStopVote vote = Create();

            Assert.AreEqual(ERejectReason.None, vote.Cast(0, true, START, ONE));
            Assert.AreEqual(ESortieStopVoteState.Passed, vote.Evaluate(START, ONE));
        }

        [Test]
        public void Four_AllAgree_Passes()
        {
            SortieStopVote vote = Create();
            vote.Cast(0, true, START, FOUR);
            vote.Cast(1, true, START, FOUR);
            vote.Cast(2, true, START, FOUR);
            Assert.AreEqual(ESortieStopVoteState.Open, vote.Evaluate(START, FOUR), "셋만 찬성하면 아직 열려 있다");

            vote.Cast(3, true, START, FOUR);
            Assert.AreEqual(ESortieStopVoteState.Passed, vote.Evaluate(START, FOUR));
        }

        [Test]
        public void Four_OneDisagree_Fails()
        {
            SortieStopVote vote = Create();
            vote.Cast(0, true, START, FOUR);
            vote.Cast(1, true, START, FOUR);
            vote.Cast(2, true, START, FOUR);
            vote.Cast(3, false, START, FOUR);

            Assert.AreEqual(ESortieStopVoteState.Failed, vote.Evaluate(START, FOUR));
        }

        [Test]
        public void Two_OneDisagree_FailsAndBlocksRetry()
        {
            SortieStopVote vote = Create();
            vote.Cast(0, true, START, TWO);
            vote.Cast(1, false, START, TWO);

            Assert.AreEqual(ESortieStopVoteState.Failed, vote.Evaluate(START, TWO));
            Assert.AreEqual(ERejectReason.Cooldown, vote.Cast(0, true, START + 1.0, TWO));
            Assert.AreEqual(ERejectReason.None, vote.Cast(0, true, START + RETRY, TWO));
            Assert.AreEqual(ESortieStopVoteState.Open, vote.State);
        }

        [Test]
        public void Open_Expires_Fails()
        {
            SortieStopVote vote = Create();
            vote.Cast(0, true, START, THREE);

            Assert.AreEqual(ESortieStopVoteState.Open, vote.Evaluate(START + DURATION - 0.1, THREE));
            Assert.AreEqual(ESortieStopVoteState.Failed, vote.Evaluate(START + DURATION, THREE));
        }

        [Test]
        public void Disagree_WithoutOpenVote_DoesNotOpen()
        {
            SortieStopVote vote = Create();

            Assert.AreEqual(ERejectReason.WrongPhase, vote.Cast(0, false, START, TWO));
            Assert.AreEqual(ESortieStopVoteState.None, vote.State);
        }

        [Test]
        public void SecondBallot_SameVoter_Rejected()
        {
            SortieStopVote vote = Create();
            vote.Cast(0, true, START, THREE);

            Assert.AreEqual(ERejectReason.AlreadySettled, vote.Cast(0, false, START, THREE));
            Assert.AreEqual(ESortieStopVoteChoice.Agree, vote.GetChoice(0));
        }

        [Test]
        public void IneligibleVoter_Rejected()
        {
            SortieStopVote vote = Create();

            Assert.AreEqual(ERejectReason.NotEligible, vote.Cast(2, true, START, TWO));
            Assert.AreEqual(ESortieStopVoteState.None, vote.State);
        }

        [Test]
        public void Three_TwoAgree_StaysOpen()
        {
            SortieStopVote vote = Create();
            vote.Cast(0, true, START, THREE);
            vote.Cast(1, true, START, THREE);

            Assert.AreEqual(ESortieStopVoteState.Open, vote.Evaluate(START, THREE));
        }

        [Test]
        public void DisconnectedNonVoter_DoesNotBlock()
        {
            SortieStopVote vote = Create();
            vote.Cast(0, true, START, TWO);
            Assert.AreEqual(ESortieStopVoteState.Open, vote.Evaluate(START, TWO));

            // 표를 내지 않은 참가자가 끊기면 남은 한 명의 찬성으로 가결된다
            Assert.AreEqual(ESortieStopVoteState.Passed, vote.Evaluate(START + 1.0, ONE));
        }

        [Test]
        public void Version_ChangesOnEveryVisibleChange()
        {
            SortieStopVote vote = Create();
            int before = vote.Version;
            vote.Cast(0, true, START, TWO);
            int afterCast = vote.Version;
            vote.Evaluate(START, TWO);

            Assert.Greater(afterCast, before);
            Assert.Greater(vote.Version, afterCast);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static SortieStopVote Create()
        {
            return new SortieStopVote(CAPACITY, DURATION, RETRY);
        }
    }
}
