// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class JoinRetryRulesTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ShouldRetry_AlreadyConnectedUnderLimit_ReturnsTrue()
        {
            Assert.IsTrue(JoinRetryRules.ShouldRetry(ERejectReason.AlreadyConnected, 0));
            Assert.IsTrue(JoinRetryRules.ShouldRetry(ERejectReason.AlreadyConnected,
                Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES - 1));
        }

        [Test]
        public void ShouldRetry_AtLimit_ReturnsFalse()
        {
            Assert.IsFalse(JoinRetryRules.ShouldRetry(ERejectReason.AlreadyConnected,
                Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES));
        }

        [Test]
        public void ShouldRetry_OtherReasons_ReturnFalse()
        {
            Assert.IsFalse(JoinRetryRules.ShouldRetry(ERejectReason.TokenExpired, 0));
            Assert.IsFalse(JoinRetryRules.ShouldRetry(ERejectReason.VersionMismatch, 0));
            Assert.IsFalse(JoinRetryRules.ShouldRetry(ERejectReason.None, 0));
        }

        [Test]
        public void RetryWindow_DefaultDurations_CoversKcpTimeoutAndStaysInsideServerReuseWindow()
        {
            // 이전 연결의 끊김이 늦게 감지되는 KCP 기본 Timeout(10초)보다 길게 재시도해야 하고,
            // 서버가 교환 결과를 기억하는 시간 안에 끝나야 같은 토큰으로 들어갈 수 있다
            const float KCP_DEFAULT_TIMEOUT_SECONDS = 10f;
            float       window                      = Constants.Network.JOIN_ALREADY_CONNECTED_RETRY_INTERVAL_SECONDS
                * Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES;

            Assert.Greater(window, KCP_DEFAULT_TIMEOUT_SECONDS);
            Assert.Less(window, (float)Constants.Network.JOIN_EXCHANGE_REUSE_SECONDS);
            Assert.Less((float)Constants.Network.JOIN_EXCHANGE_REUSE_SECONDS, Constants.Network.JOIN_TOKEN_LIFETIME_SECONDS);
        }
    }
}
