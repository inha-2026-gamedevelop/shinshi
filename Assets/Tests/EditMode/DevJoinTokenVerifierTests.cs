// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class DevJoinTokenVerifierTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int REQUEST_ID = 3;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryParse_ValidToken_ReturnsKey()
        {
            Assert.IsTrue(DevJoinTokenVerifier.TryParse("dev-Player_01", out string key));
            Assert.AreEqual("Player_01", key);
        }

        [Test]
        public void TryParse_InvalidTokens_ReturnFalse()
        {
            string tooLong = Constants.Network.DEV_JOIN_TOKEN_PREFIX
                + new string('a', Constants.Network.DEV_PARTICIPANT_KEY_MAX_LENGTH + 1);

            Assert.IsFalse(DevJoinTokenVerifier.TryParse(null, out _));
            Assert.IsFalse(DevJoinTokenVerifier.TryParse("dev-", out _));
            Assert.IsFalse(DevJoinTokenVerifier.TryParse("prod-abc", out _));
            Assert.IsFalse(DevJoinTokenVerifier.TryParse("dev-a b", out _));
            Assert.IsFalse(DevJoinTokenVerifier.TryParse(tooLong, out _));
        }

        [Test]
        public void Verify_InvalidToken_ReportsTokenRevoked()
        {
            var listener = new RecordingListener();
            new DevJoinTokenVerifier().Verify(REQUEST_ID, "bad", listener);

            Assert.AreEqual(REQUEST_ID, listener.RequestId);
            Assert.AreEqual(ERejectReason.TokenRevoked, listener.Reason);
        }

        [Test]
        public void Verify_ValidToken_ReportsClaims()
        {
            var listener = new RecordingListener();
            new DevJoinTokenVerifier().Verify(REQUEST_ID, "dev-abc", listener);

            Assert.AreEqual(ERejectReason.None, listener.Reason);
            Assert.AreEqual("abc", listener.Claims.ParticipantKey);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private sealed class RecordingListener : IJoinTokenVerifyListener
        {
            public int             RequestId { get; private set; }
            public ERejectReason   Reason    { get; private set; }
            public JoinTokenClaims Claims    { get; private set; }

            public void OnJoinTokenVerified(int requestId, ERejectReason reason, in JoinTokenClaims claims)
            {
                RequestId = requestId;
                Reason    = reason;
                Claims    = claims;
            }
        }
    }
}
