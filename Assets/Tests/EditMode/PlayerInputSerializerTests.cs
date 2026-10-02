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
    public class PlayerInputSerializerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    SESSION_EPOCH  = 123456789;
        private const int    PARTICIPANT_ID = 4;
        private const uint   SEQUENCE       = 300;
        private const double SERVER_TIME    = 1234.5678;
        private const ushort AIM_YAW        = 40000;

        // Mirror 메시지 머리를 제외한 입력 크기 상한으로 네트워크 전송량을 제한한다
        private const int MAX_ATTACK_BYTES = 24;
        private const int MAX_MOVE_BYTES   = 32;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void MoveInput_RoundTrip_KeepsAllFields()
        {
            var input = new PlayerMoveInput(Header(), new Vector3(1.5f, -2f, 30.25f));

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WritePlayerMoveInput(input);
                Assert.LessOrEqual(writer.Position, MAX_MOVE_BYTES);

                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    PlayerMoveInput read = reader.ReadPlayerMoveInput();

                    AssertHeader(read.Header);
                    Assert.AreEqual(input.Destination, read.Destination);
                    Assert.AreEqual(0, reader.Remaining);
                }
            }
        }

        [Test]
        public void AttackInput_RoundTrip_KeepsAllFields()
        {
            var input = new PlayerAttackInput(Header(), AIM_YAW);

            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WritePlayerAttackInput(input);
                Assert.LessOrEqual(writer.Position, MAX_ATTACK_BYTES);

                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    PlayerAttackInput read = reader.ReadPlayerAttackInput();

                    AssertHeader(read.Header);
                    Assert.AreEqual(AIM_YAW, read.AimYaw);
                    Assert.AreEqual(0, reader.Remaining);
                }
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static PlayerInputHeader Header()
        {
            return new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, SESSION_EPOCH, PARTICIPANT_ID, SEQUENCE, SERVER_TIME);
        }

        private static void AssertHeader(PlayerInputHeader header)
        {
            Assert.AreEqual(Constants.Network.PROTOCOL_VERSION, header.ProtocolVersion);
            Assert.AreEqual(SESSION_EPOCH, header.SessionEpoch);
            Assert.AreEqual(PARTICIPANT_ID, header.ParticipantId);
            Assert.AreEqual(SEQUENCE, header.Sequence);
            Assert.AreEqual(SERVER_TIME, header.EstimatedServerTime);
        }
    }
}
