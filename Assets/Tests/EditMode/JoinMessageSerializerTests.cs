// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class JoinMessageSerializerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string HASH        = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string TOKEN       = "dev-player01";
        private const int    PARTICIPANT = 42;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void JoinRequest_RoundTrip_KeepsAllFields()
        {
            var writer = new NetworkWriter();
            writer.WriteJoinRequestMessage(new JoinRequestMessage(Constants.Network.PROTOCOL_VERSION, HASH, TOKEN));

            var                reader = new NetworkReader(writer.ToArraySegment());
            JoinRequestMessage read   = reader.ReadJoinRequestMessage();

            Assert.AreEqual(Constants.Network.PROTOCOL_VERSION, read.ProtocolVersion);
            Assert.AreEqual(HASH, read.ContentHash);
            Assert.AreEqual(TOKEN, read.JoinToken);
        }

        [Test]
        public void JoinResponse_RoundTrip_KeepsReasonAndParticipant()
        {
            var writer = new NetworkWriter();
            writer.WriteJoinResponseMessage(new JoinResponseMessage(ERejectReason.AlreadyConnected, PARTICIPANT));

            var                 reader = new NetworkReader(writer.ToArraySegment());
            JoinResponseMessage read   = reader.ReadJoinResponseMessage();

            Assert.AreEqual(ERejectReason.AlreadyConnected, read.Reason);
            Assert.AreEqual(PARTICIPANT, read.ParticipantId);
            Assert.IsFalse(read.IsAccepted);
        }
    }
}
