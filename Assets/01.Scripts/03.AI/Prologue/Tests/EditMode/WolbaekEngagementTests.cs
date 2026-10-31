// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class WolbaekEngagementTests
    {
        private const double START = 100.0;

        [Test]
        public void Update_BeforeBegin_DoesNotEnd()
        {
            var engagement = new WolbaekEngagement();

            Assert.AreEqual(EWolbaekEnd.None, engagement.Update(START + 999.0));
            Assert.IsFalse(engagement.IsRunning);
        }

        [Test]
        public void Update_Before30Seconds_KeepsRunning()
        {
            var engagement = new WolbaekEngagement();
            engagement.Begin(START);

            Assert.AreEqual(EWolbaekEnd.None, engagement.Update(START + 29.9));
            Assert.IsTrue(engagement.IsRunning);
        }

        [Test]
        public void Update_At30Seconds_EndsAsTimeUp()
        {
            var engagement = new WolbaekEngagement();
            engagement.Begin(START);

            Assert.AreEqual(EWolbaekEnd.TimeUp, engagement.Update(START + 30.0));
            Assert.IsFalse(engagement.IsRunning);
        }

        [Test]
        public void ReportPlayerDown_Within30Seconds_EndsAsPlayerDown()
        {
            var engagement = new WolbaekEngagement();
            engagement.Begin(START);

            Assert.AreEqual(EWolbaekEnd.PlayerDown, engagement.ReportPlayerDown(START + 5.0));
        }

        [Test]
        public void ReportPlayerDown_After30Seconds_KeepsTimeUp()
        {
            var engagement = new WolbaekEngagement();
            engagement.Begin(START);

            Assert.AreEqual(EWolbaekEnd.TimeUp, engagement.ReportPlayerDown(START + 31.0));
        }

        [Test]
        public void Update_AfterPlayerDown_KeepsFirstReason()
        {
            var engagement = new WolbaekEngagement();
            engagement.Begin(START);
            engagement.ReportPlayerDown(START + 5.0);

            Assert.AreEqual(EWolbaekEnd.PlayerDown, engagement.Update(START + 40.0));
        }

        [Test]
        public void Begin_CalledTwice_IgnoresSecondCall()
        {
            var engagement = new WolbaekEngagement();
            engagement.Begin(START);
            engagement.Begin(START + 20.0);

            Assert.AreEqual(EWolbaekEnd.TimeUp, engagement.Update(START + 30.0));
        }

        [Test]
        public void ReportPlayerDown_BeforeBegin_IsIgnored()
        {
            var engagement = new WolbaekEngagement();

            Assert.AreEqual(EWolbaekEnd.None, engagement.ReportPlayerDown(START));
        }
    }
}
