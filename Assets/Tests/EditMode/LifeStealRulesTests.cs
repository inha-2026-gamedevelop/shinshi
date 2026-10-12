// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public class LifeStealRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  MAX_HEALTH     = 1000f;
        private const float  HALF_HEALTH    = 500f;
        private const float  RATE           = 0.02f;
        private const float  CAP_PER_SECOND = 0.02f; // 구간 상한 20
        private const float  SMALL_DAMAGE   = 500f;  // 후보 회복 10
        private const float  LARGE_DAMAGE   = 6000f; // 후보 회복 120
        private const double NOW            = 10.25;
        private const float  TOLERANCE      = 0.0001f;

        private long  _window;
        private float _windowHealed;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _window       = LifeStealRules.NO_WINDOW;
            _windowHealed = 0f;
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Grant_UnderCap_ReturnsDamageTimesRate()
        {
            float healed = Grant(SMALL_DAMAGE, HALF_HEALTH, NOW);

            Assert.AreEqual(SMALL_DAMAGE * RATE, healed, TOLERANCE);
            Assert.AreEqual(healed, _windowHealed, TOLERANCE);
        }

        [Test]
        public void Grant_OverCapInOneWindow_ClampsToCap()
        {
            float healed = Grant(LARGE_DAMAGE, HALF_HEALTH, NOW);

            Assert.AreEqual(MAX_HEALTH * CAP_PER_SECOND, healed, TOLERANCE);
        }

        [Test]
        public void Grant_SameWindowAfterCap_ReturnsZero()
        {
            Grant(LARGE_DAMAGE, HALF_HEALTH, NOW);

            Assert.AreEqual(0f, Grant(SMALL_DAMAGE, HALF_HEALTH, NOW + 0.5));
        }

        [Test]
        public void Grant_SameWindowPartialBudget_StopsAtCap()
        {
            float first  = Grant(SMALL_DAMAGE, HALF_HEALTH, NOW);
            float second = Grant(LARGE_DAMAGE, HALF_HEALTH, NOW + 0.1);

            Assert.AreEqual(MAX_HEALTH * CAP_PER_SECOND, first + second, TOLERANCE);
            Assert.AreEqual(MAX_HEALTH * CAP_PER_SECOND, _windowHealed, TOLERANCE);
        }

        [Test]
        public void Grant_NextWindow_ResetsBudget()
        {
            Grant(LARGE_DAMAGE, HALF_HEALTH, NOW);

            float healed = Grant(SMALL_DAMAGE, HALF_HEALTH, NOW + 1.0);

            Assert.AreEqual(SMALL_DAMAGE * RATE, healed, TOLERANCE);
        }

        [Test]
        public void Grant_TimeRewound_StartsNewWindow()
        {
            Grant(LARGE_DAMAGE, HALF_HEALTH, NOW);

            float healed = Grant(SMALL_DAMAGE, HALF_HEALTH, NOW - 5.0);

            Assert.AreEqual(SMALL_DAMAGE * RATE, healed, TOLERANCE);
        }

        [Test]
        public void Grant_FullHealth_ReturnsZeroAndKeepsBudget()
        {
            float healed = Grant(SMALL_DAMAGE, MAX_HEALTH, NOW);

            Assert.AreEqual(0f, healed);
            Assert.AreEqual(0f, _windowHealed);
        }

        [Test]
        public void Grant_NearFullHealth_CountsOnlyActualGain()
        {
            const float MISSING = 3f;

            float healed = Grant(SMALL_DAMAGE, MAX_HEALTH - MISSING, NOW);

            Assert.AreEqual(MISSING, healed, TOLERANCE);
            Assert.AreEqual(MISSING, _windowHealed, TOLERANCE);
        }

        [TestCase(0f, RATE, MAX_HEALTH, CAP_PER_SECOND)]
        [TestCase(SMALL_DAMAGE, 0f, MAX_HEALTH, CAP_PER_SECOND)]
        [TestCase(SMALL_DAMAGE, RATE, 0f, CAP_PER_SECOND)]
        [TestCase(SMALL_DAMAGE, RATE, MAX_HEALTH, 0f)]
        [TestCase(float.NaN, RATE, MAX_HEALTH, CAP_PER_SECOND)]
        [TestCase(SMALL_DAMAGE, float.NaN, MAX_HEALTH, CAP_PER_SECOND)]
        [TestCase(float.PositiveInfinity, RATE, MAX_HEALTH, CAP_PER_SECOND)]
        [TestCase(-SMALL_DAMAGE, RATE, MAX_HEALTH, CAP_PER_SECOND)]
        public void Grant_InvalidInput_ReturnsZeroAndKeepsWindow(float damage, float rate, float maxHealth, float cap)
        {
            float healed = LifeStealRules.Grant(damage, rate, 0f, maxHealth, cap, NOW, ref _window, ref _windowHealed);

            Assert.AreEqual(0f, healed);
            Assert.AreEqual(LifeStealRules.NO_WINDOW, _window);
        }

        [Test]
        public void Grant_NonFiniteTime_ReturnsZero()
        {
            Assert.AreEqual(0f, Grant(SMALL_DAMAGE, HALF_HEALTH, double.NaN));
            Assert.AreEqual(0f, Grant(SMALL_DAMAGE, HALF_HEALTH, double.PositiveInfinity));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private float Grant(float damage, float health, double serverTime)
        {
            return LifeStealRules.Grant(damage, RATE, health, MAX_HEALTH, CAP_PER_SECOND, serverTime, ref _window,
                ref _windowHealed);
        }
    }
}
