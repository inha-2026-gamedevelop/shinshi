// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class HitFeedbackCooldownTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  COOLDOWN = 0.4f;
        private const double START    = 10.0;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryUse_First_IsAllowed()
        {
            var cooldown = new HitFeedbackCooldown();

            Assert.That(cooldown.TryUse(START, COOLDOWN), Is.True);
        }

        [Test]
        public void TryUse_WithinCooldown_IsBlocked()
        {
            var cooldown = new HitFeedbackCooldown();
            cooldown.TryUse(START, COOLDOWN);

            Assert.That(cooldown.TryUse(START + 0.39, COOLDOWN), Is.False);
        }

        [Test]
        public void TryUse_AfterCooldown_IsAllowedAgain()
        {
            var cooldown = new HitFeedbackCooldown();
            cooldown.TryUse(START, COOLDOWN);

            Assert.That(cooldown.TryUse(START + COOLDOWN, COOLDOWN), Is.True);
        }

        [Test]
        public void TryUse_BlockedAttempt_DoesNotExtendCooldown()
        {
            var cooldown = new HitFeedbackCooldown();
            cooldown.TryUse(START, COOLDOWN);
            cooldown.TryUse(START + 0.3, COOLDOWN);

            Assert.That(cooldown.TryUse(START + COOLDOWN, COOLDOWN), Is.True);
        }

        [Test]
        public void Reset_AllowsImmediately()
        {
            var cooldown = new HitFeedbackCooldown();
            cooldown.TryUse(START, COOLDOWN);
            cooldown.Reset();

            Assert.That(cooldown.TryUse(START, COOLDOWN), Is.True);
        }
    }
}
