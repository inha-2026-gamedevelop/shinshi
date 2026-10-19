// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class PartyInviteTrackerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TARGET_A = "대상에이";
        private const string TARGET_B = "대상비";
        private const float  TIMEOUT  = 8f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Resolve_NotPending_IgnoresEvenMatchingId()
        {
            var tracker = new PartyInviteTracker();

            EInviteOutcome outcome = tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.None, 1u), out string target);

            Assert.AreEqual(EInviteOutcome.Ignored, outcome);
            Assert.IsEmpty(target);
        }

        [Test]
        public void Resolve_MatchingAccepted_ReturnsAcceptedWithTargetAndClearsPending()
        {
            var tracker = new PartyInviteTracker();
            tracker.Begin(5u, TARGET_A, 0f);

            EInviteOutcome outcome = tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.None, 5u), out string target);

            Assert.AreEqual(EInviteOutcome.Accepted, outcome);
            Assert.AreEqual(TARGET_A, target);
            Assert.IsFalse(tracker.IsPending);
        }

        [Test]
        public void Resolve_MatchingRejected_ReturnsRejected()
        {
            var tracker = new PartyInviteTracker();
            tracker.Begin(5u, TARGET_A, 0f);

            EInviteOutcome outcome = tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.InvalidTarget, 5u), out string _);

            Assert.AreEqual(EInviteOutcome.Rejected, outcome);
            Assert.IsFalse(tracker.IsPending);
        }

        [Test]
        public void Resolve_OtherUiInviteAcceptedThenOwnRejected_OwnRejectionIsNotSwallowed()
        {
            // 친구창이 A를 초대(ID 1), 파티창이 B를 초대(ID 2). A의 늦은 승인이 먼저 오고 B의 실제 거절이 뒤에 온다
            var tracker = new PartyInviteTracker();
            tracker.Begin(2u, TARGET_B, 0f);

            EInviteOutcome lateA = tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.None, 1u), out string _);
            Assert.AreEqual(EInviteOutcome.Ignored, lateA);
            Assert.IsTrue(tracker.IsPending);

            EInviteOutcome ownB = tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.InvalidTarget, 2u), out string target);
            Assert.AreEqual(EInviteOutcome.Rejected, ownB);
            Assert.AreEqual(TARGET_B, target);
        }

        [Test]
        public void Resolve_ResultWithoutRequestId_IsIgnoredWhilePending()
        {
            var tracker = new PartyInviteTracker();
            tracker.Begin(3u, TARGET_A, 0f);

            EInviteOutcome outcome = tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.None, 0u), out string _);

            Assert.AreEqual(EInviteOutcome.Ignored, outcome);
            Assert.IsTrue(tracker.IsPending);
        }

        [Test]
        public void Resolve_SameIdOtherAction_IsIgnored()
        {
            var tracker = new PartyInviteTracker();
            tracker.Begin(3u, TARGET_A, 0f);

            EInviteOutcome outcome = tracker.Resolve(Result(EPartyAction.JoinRoom, ERejectReason.None, 3u), out string _);

            Assert.AreEqual(EInviteOutcome.Ignored, outcome);
            Assert.IsTrue(tracker.IsPending);
        }

        [Test]
        public void Resolve_AfterCloseAndReopen_LateResultOfOldRequestIsIgnored()
        {
            var tracker = new PartyInviteTracker();
            tracker.Begin(1u, TARGET_A, 0f);
            tracker.Reset();
            tracker.Begin(2u, TARGET_B, 1f);

            EInviteOutcome late = tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.None, 1u), out string _);

            Assert.AreEqual(EInviteOutcome.Ignored, late);
            Assert.AreEqual(TARGET_B, tracker.Target);
            Assert.AreEqual(2u, tracker.RequestId);
        }

        [Test]
        public void Resolve_AfterTimeoutReset_LateResultOfOldRequestIsIgnoredAndNewOneStillWorks()
        {
            var tracker = new PartyInviteTracker();
            tracker.Begin(1u, TARGET_A, 0f);
            Assert.IsTrue(tracker.IsExpired(TIMEOUT, TIMEOUT));
            tracker.Reset();
            tracker.Begin(2u, TARGET_B, TIMEOUT + 1f);

            Assert.AreEqual(EInviteOutcome.Ignored, tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.None, 1u), out string _));
            Assert.AreEqual(EInviteOutcome.Accepted, tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.None, 2u), out string target));
            Assert.AreEqual(TARGET_B, target);
        }

        [Test]
        public void Resolve_SameResultTwice_SecondIsIgnored()
        {
            var tracker = new PartyInviteTracker();
            tracker.Begin(4u, TARGET_A, 0f);
            PartyResultMessage result = Result(EPartyAction.Invite, ERejectReason.None, 4u);

            Assert.AreEqual(EInviteOutcome.Accepted, tracker.Resolve(in result, out string _));
            Assert.AreEqual(EInviteOutcome.Ignored, tracker.Resolve(in result, out string _));
        }

        [Test]
        public void Reset_ClearsRequestIdAndTarget()
        {
            var tracker = new PartyInviteTracker();
            tracker.Begin(8u, TARGET_A, 0f);

            tracker.Reset();

            Assert.IsFalse(tracker.IsPending);
            Assert.AreEqual(0u, tracker.RequestId);
            Assert.IsEmpty(tracker.Target);
        }

        [Test]
        public void IsExpired_BeforeAndAtTimeout_FollowsTimeout()
        {
            var tracker = new PartyInviteTracker();
            Assert.IsFalse(tracker.IsExpired(100f, TIMEOUT));

            tracker.Begin(1u, TARGET_A, 10f);

            Assert.IsFalse(tracker.IsExpired(10f + TIMEOUT - 0.1f, TIMEOUT));
            Assert.IsTrue(tracker.IsExpired(10f + TIMEOUT, TIMEOUT));
        }

        [Test]
        public void Begin_BeforeSend_ResponseArrivingImmediatelyIsStillMatched()
        {
            // 보내기 전에 Begin을 걸어 두면 응답이 Send 직후 바로 와도 놓치지 않는다
            var tracker = new PartyInviteTracker();
            tracker.Begin(6u, TARGET_A, 0f);

            EInviteOutcome outcome = tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.None, 6u), out string target);

            Assert.AreEqual(EInviteOutcome.Accepted, outcome);
            Assert.AreEqual(TARGET_A, target);
        }

        [Test]
        public void Begin_RolledBackWhenSendFails_NoPendingLeft()
        {
            var tracker = new PartyInviteTracker();
            tracker.Begin(6u, TARGET_A, 0f);

            tracker.Reset();

            Assert.IsFalse(tracker.IsPending);
            Assert.AreEqual(EInviteOutcome.Ignored, tracker.Resolve(Result(EPartyAction.Invite, ERejectReason.None, 6u), out string _));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static PartyResultMessage Result(EPartyAction action, ERejectReason reason, uint requestId)
        {
            return new PartyResultMessage(action, reason, requestId);
        }
    }
}
