// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class HitSparkPoolTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   LIGHT_COUNT     = 10;
        private const int   HEAVY_COUNT     = 28;
        private const float CRIT_MULTIPLIER = 2.5f;
        private const int   POOL_LIMIT      = 64;

        /****************************************
        *                Methods
        ****************************************/

        [Test]
        public void ResolveCount_NormalHit_KeepsBaseCount()
        {
            Assert.AreEqual(LIGHT_COUNT, HitSparkPool.ResolveCount(LIGHT_COUNT, false, CRIT_MULTIPLIER));
            Assert.AreEqual(HEAVY_COUNT, HitSparkPool.ResolveCount(HEAVY_COUNT, false, CRIT_MULTIPLIER));
        }

        [Test]
        public void ResolveCount_Critical_AppliesMultiplier()
        {
            Assert.AreEqual(25, HitSparkPool.ResolveCount(LIGHT_COUNT, true, CRIT_MULTIPLIER));
        }

        [Test]
        public void ResolveCount_CriticalHeavy_IsClampedToPoolLimit()
        {
            Assert.AreEqual(POOL_LIMIT, HitSparkPool.ResolveCount(HEAVY_COUNT, true, CRIT_MULTIPLIER));
        }

        [Test]
        public void ResolveCount_NegativeBase_NeverGoesBelowZero()
        {
            Assert.AreEqual(0, HitSparkPool.ResolveCount(-5, false, CRIT_MULTIPLIER));
        }
    }
}
