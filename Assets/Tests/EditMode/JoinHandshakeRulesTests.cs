// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class JoinHandshakeRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SERVER_HASH           = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string OTHER_HASH            = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
        private const string SHORT_HASH            = "0123";
        private const string TOKEN                 = "dev-player01";
        private const int    OTHER_PROTOCOL_OFFSET = 1;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Check_MatchingRequest_ReturnsNone()
        {
            Assert.AreEqual(ERejectReason.None, Check(Constants.Network.PROTOCOL_VERSION, SERVER_HASH, TOKEN));
        }

        [Test]
        public void Check_ProtocolMismatch_ReturnsVersionMismatch()
        {
            int otherProtocol = Constants.Network.PROTOCOL_VERSION + OTHER_PROTOCOL_OFFSET;

            Assert.AreEqual(ERejectReason.VersionMismatch, Check(otherProtocol, SERVER_HASH, TOKEN));
        }

        [Test]
        public void Check_ContentHashMismatch_ReturnsVersionMismatch()
        {
            Assert.AreEqual(ERejectReason.VersionMismatch, Check(Constants.Network.PROTOCOL_VERSION, OTHER_HASH, TOKEN));
            Assert.AreEqual(ERejectReason.VersionMismatch, Check(Constants.Network.PROTOCOL_VERSION, SHORT_HASH, TOKEN));
            Assert.AreEqual(ERejectReason.VersionMismatch, Check(Constants.Network.PROTOCOL_VERSION, null, TOKEN));
        }

        [Test]
        public void Check_ServerHashMissing_ReturnsVersionMismatch()
        {
            ERejectReason reason = JoinHandshakeRules.Check(Constants.Network.PROTOCOL_VERSION, SERVER_HASH, TOKEN,
                Constants.Network.PROTOCOL_VERSION, null);

            Assert.AreEqual(ERejectReason.VersionMismatch, reason);
        }

        [Test]
        public void Check_EmptyOrOversizedToken_ReturnsTokenRevoked()
        {
            string oversized = new string('a', Constants.Network.MAX_JOIN_TOKEN_LENGTH + 1);

            Assert.AreEqual(ERejectReason.TokenRevoked, Check(Constants.Network.PROTOCOL_VERSION, SERVER_HASH, string.Empty));
            Assert.AreEqual(ERejectReason.TokenRevoked, Check(Constants.Network.PROTOCOL_VERSION, SERVER_HASH, oversized));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static ERejectReason Check(int protocolVersion, string contentHash, string joinToken)
        {
            return JoinHandshakeRules.Check(protocolVersion, contentHash, joinToken, Constants.Network.PROTOCOL_VERSION,
                SERVER_HASH);
        }
    }
}
