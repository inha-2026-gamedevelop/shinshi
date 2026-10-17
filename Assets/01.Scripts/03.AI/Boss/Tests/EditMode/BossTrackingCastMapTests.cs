// 서드파티
using NUnit.Framework;

namespace Shinshi.AI.Tests
{
    public sealed class BossTrackingCastMapTests
    {
        [Test]
        public void TryCancel_LastAreaOfCast_ReturnsOnlyItsCast()
        {
            var map = new BossTrackingCastMap();
            map.Add(10, 100);
            map.Add(11, 100);
            map.Add(20, 200);

            Assert.That(map.TryCancel(10, out int firstCast, out bool firstIsLast), Is.True);
            Assert.That(firstCast, Is.EqualTo(100));
            Assert.That(firstIsLast, Is.False);

            Assert.That(map.TryCancel(11, out int lastCast, out bool lastIsLast), Is.True);
            Assert.That(lastCast, Is.EqualTo(100));
            Assert.That(lastIsLast, Is.True);
            Assert.That(map.Count, Is.EqualTo(1), "다른 시전의 장판은 남는다");
        }

        [Test]
        public void TryCancel_UnknownOrActivatedArea_CannotEndAMotion()
        {
            var map = new BossTrackingCastMap();
            map.Add(10, 100);
            map.Remove(10);

            Assert.That(map.TryCancel(10, out _, out _), Is.False);
            Assert.That(map.TryCancel(99, out _, out _), Is.False);
        }

        [Test]
        public void HasCast_TrueOnlyWhileThatCastHasAreasLeft()
        {
            var map = new BossTrackingCastMap();
            map.Add(10, 100);
            map.Add(11, 100);
            map.Add(20, 200);

            Assert.That(map.HasCast(100), Is.True);
            map.TryCancel(10, out _, out _);
            Assert.That(map.HasCast(100), Is.True);
            map.TryCancel(11, out _, out _);
            Assert.That(map.HasCast(100), Is.False);
            Assert.That(map.HasCast(200), Is.True);
            Assert.That(map.HasCast(300), Is.False);
        }

        [Test]
        public void Clear_ReconnectDiscardOldAreaIds()
        {
            var map = new BossTrackingCastMap();
            map.Add(10, 100);
            map.Clear();
            map.Add(10, 200);

            Assert.That(map.TryCancel(10, out int castId, out bool isLast), Is.True);
            Assert.That(castId, Is.EqualTo(200));
            Assert.That(isLast, Is.True);
        }
    }
}
