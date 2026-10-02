// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class SkillInputSerializerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    SESSION_EPOCH  = 123456789;
        private const int    PARTICIPANT_ID = 4;
        private const uint   SEQUENCE       = uint.MaxValue;
        private const double SERVER_TIME    = 1234.5678;
        private const ushort AIM_YAW        = ushort.MaxValue;
        private const byte   SLOT           = 3;
        private const int    PAYLOAD_BYTES  = 35;

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(ESkillRequestKind.Regular)]
        [TestCase(ESkillRequestKind.Ascension)]
        [TestCase((ESkillRequestKind)byte.MaxValue)]
        public void PlayerSkillInput_RoundTrip_KeepsAllFields(ESkillRequestKind kind)
        {
            var header = new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, SESSION_EPOCH, PARTICIPANT_ID,
                SEQUENCE, SERVER_TIME);
            var input  = new PlayerSkillInput(header, kind, SLOT, AIM_YAW, new Vector3(1.5f, -2f, 30.25f));

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WritePlayerSkillInput(input);
                Assert.AreEqual(PAYLOAD_BYTES, writer.Position);

                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    PlayerSkillInput read = reader.ReadPlayerSkillInput();

                    Assert.AreEqual(header.ProtocolVersion, read.Header.ProtocolVersion);
                    Assert.AreEqual(SESSION_EPOCH, read.Header.SessionEpoch);
                    Assert.AreEqual(PARTICIPANT_ID, read.Header.ParticipantId);
                    Assert.AreEqual(SEQUENCE, read.Header.Sequence);
                    Assert.AreEqual(SERVER_TIME, read.Header.EstimatedServerTime);
                    Assert.AreEqual(kind, read.Kind);
                    Assert.AreEqual(SLOT, read.Slot);
                    Assert.AreEqual(AIM_YAW, read.AimYaw);
                    Assert.AreEqual(input.Point, read.Point);
                    Assert.AreEqual(0, reader.Remaining);
                }
            }
        }

        [Test]
        public void PlayerSkillInput_ConcatenatedMessages_ReadsOneMessageAtATime()
        {
            var header = new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, SESSION_EPOCH, PARTICIPANT_ID,
                SEQUENCE, SERVER_TIME);
            var first  = new PlayerSkillInput(header, ESkillRequestKind.Regular, SLOT, AIM_YAW, Vector3.forward);
            var second = new PlayerSkillInput(header, ESkillRequestKind.Ascension, 0, 0, Vector3.zero);

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WritePlayerSkillInput(first);
                writer.WritePlayerSkillInput(second);

                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    PlayerSkillInput readFirst = reader.ReadPlayerSkillInput();
                    Assert.AreEqual(first.Kind, readFirst.Kind);
                    Assert.AreEqual(first.Point, readFirst.Point);
                    Assert.AreEqual(PAYLOAD_BYTES, reader.Remaining);

                    PlayerSkillInput readSecond = reader.ReadPlayerSkillInput();
                    Assert.AreEqual(second.Kind, readSecond.Kind);
                    Assert.AreEqual(second.Point, readSecond.Point);
                    Assert.AreEqual(0, reader.Remaining);
                }
            }
        }
    }
}
