// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class TownPartyClientTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ReserveRequestId_Repeated_IsNonZeroAndStrictlyIncreasing()
        {
            uint previous = TownPartyClient.ReserveRequestId();
            Assert.AreNotEqual(0u, previous);

            for (int i = 0; i < 5; ++i)
            {
                uint next = TownPartyClient.ReserveRequestId();
                Assert.AreNotEqual(0u, next);
                Assert.Greater(next, previous);
                previous = next;
            }
        }

        [Test]
        public void Invite_WhenNotConnected_ReturnsFalseButStillIssuesNonZeroId()
        {
            bool sent = TownPartyClient.Invite("누군가", out uint requestId);

            Assert.IsFalse(sent);
            Assert.AreNotEqual(0u, requestId);
        }

        [Test]
        public void Invite_TwiceWhenNotConnected_IssuesIncreasingIds()
        {
            TownPartyClient.Invite("누군가", out uint first);
            TownPartyClient.Invite("누군가", out uint second);

            Assert.Greater(second, first);
        }

        [Test]
        public void Invite_LegacyOverloadWhenNotConnected_ReturnsFalse()
        {
            Assert.IsFalse(TownPartyClient.Invite("누군가"));
        }
    }
}
