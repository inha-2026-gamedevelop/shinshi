// System
using System;
using System.IO;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    /// <summary> CharacterAppearanceSerializer의 와이어 바이트 수와 버전별 거절 경계를 고정한다.
    /// 내부 보초 바이트가 다시 생기면 아래 길이 검증이 먼저 깨진다 </summary>
    public class CharacterAppearanceSerializerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // exists(1) + version(1) + bodyType(1) + face(1+22) + body(1+10) + skinTone(1) + eyeColor(1)
        // + browColor(1) + lipColor(1) + hairId(2) + hairColor(1) = 44. v2는 faceId/outfitId 2바이트가 더 붙는다
        private const int V1_TOTAL_BYTES = 44;
        private const int V2_TOTAL_BYTES = V1_TOTAL_BYTES + 2;

        private const int SAMPLE_SKIN_TONE  = 90;
        private const int SAMPLE_EYE_COLOR  = 3;
        private const int SAMPLE_BROW_COLOR = 1;
        private const int SAMPLE_LIP_COLOR  = 5;
        private const int SAMPLE_HAIR_ID    = 300;
        private const int SAMPLE_HAIR_COLOR = 7;
        private const int SAMPLE_FACE_ID    = CharacterAppearanceCatalog.FACE_CUTE;
        private const int SAMPLE_OUTFIT_ID  = CharacterAppearanceCatalog.OUTFIT_WARRIOR;

        private const byte TRAILING_MARKER = 0x5A;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ProtocolVersion_CurrentConstant_IsThree()
        {
            Assert.AreEqual(3, Constants.Network.PROTOCOL_VERSION);
        }

        [Test]
        public void WriteCharacterAppearance_V1_ProducesGoldenByteLength()
        {
            NetworkWriter writer = new NetworkWriter();
            writer.WriteCharacterAppearance(CreateV1());

            Assert.AreEqual(V1_TOTAL_BYTES, writer.ToArraySegment().Count);
        }

        [Test]
        public void WriteCharacterAppearance_V2_ProducesGoldenByteLength()
        {
            NetworkWriter writer = new NetworkWriter();
            writer.WriteCharacterAppearance(CreateV2());

            Assert.AreEqual(V2_TOTAL_BYTES, writer.ToArraySegment().Count);
        }

        [Test]
        public void ReadCharacterAppearance_V1RoundTrip_MatchesAndLeavesTrailingByteUntouched()
        {
            CharacterAppearance original = CreateV1();
            NetworkWriter writer = new NetworkWriter();
            writer.WriteCharacterAppearance(original);
            // serializer는 자기 payload 바깥에 아무것도 쓰지 않는다 - 이 marker는 테스트가 직접 붙인 것이다
            writer.WriteByte(TRAILING_MARKER);

            NetworkReader reader = new NetworkReader(writer.ToArraySegment());
            CharacterAppearance copy = reader.ReadCharacterAppearance();

            AssertSameAppearance(original, copy);
            Assert.AreEqual(TRAILING_MARKER, reader.ReadByte(),
                "reader가 DTO를 다 읽은 뒤의 다음 바이트는 테스트가 붙인 marker여야 한다");
        }

        [Test]
        public void ReadCharacterAppearance_V2RoundTrip_MatchesAndLeavesTrailingByteUntouched()
        {
            CharacterAppearance original = CreateV2();
            NetworkWriter writer = new NetworkWriter();
            writer.WriteCharacterAppearance(original);
            writer.WriteByte(TRAILING_MARKER);

            NetworkReader reader = new NetworkReader(writer.ToArraySegment());
            CharacterAppearance copy = reader.ReadCharacterAppearance();

            AssertSameAppearance(original, copy);
            Assert.AreEqual(SAMPLE_FACE_ID, copy.FaceId);
            Assert.AreEqual(SAMPLE_OUTFIT_ID, copy.OutfitId);
            Assert.AreEqual(TRAILING_MARKER, reader.ReadByte(),
                "reader가 DTO를 다 읽은 뒤의 다음 바이트는 테스트가 붙인 marker여야 한다");
        }

        [Test]
        public void ReadCharacterAppearance_NullWritten_ReturnsNull()
        {
            NetworkWriter writer = new NetworkWriter();
            writer.WriteCharacterAppearance(null);

            NetworkReader reader = new NetworkReader(writer.ToArraySegment());
            Assert.IsNull(reader.ReadCharacterAppearance());
        }

        [Test]
        public void ReadCharacterAppearance_TruncatedPacket_ThrowsEndOfStream()
        {
            NetworkWriter writer = new NetworkWriter();
            writer.WriteCharacterAppearance(CreateV2());
            ArraySegment<byte> full = writer.ToArraySegment();
            ArraySegment<byte> truncated = new ArraySegment<byte>(full.Array, full.Offset, full.Count - 1);

            NetworkReader reader = new NetworkReader(truncated);
            Assert.Throws<EndOfStreamException>(() => reader.ReadCharacterAppearance());
        }

        [Test]
        public void WriteCharacterAppearance_UnsupportedVersion_ThrowsArgumentException()
        {
            CharacterAppearance invalid = new CharacterAppearance(CharacterAppearance.VERSION_2 + 1, CharacterAppearance.BODY_TYPE_FEMALE,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT), NewSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                SAMPLE_SKIN_TONE, SAMPLE_EYE_COLOR, SAMPLE_BROW_COLOR, SAMPLE_LIP_COLOR, SAMPLE_HAIR_ID, SAMPLE_HAIR_COLOR);
            NetworkWriter writer = new NetworkWriter();

            Assert.Throws<ArgumentException>(() => writer.WriteCharacterAppearance(invalid));
        }

        [Test]
        public void ReadCharacterAppearance_UnsupportedVersionByte_ThrowsInvalidOperation()
        {
            // exists=true 뒤 바로 알 수 없는 version 바이트를 심는다. 뒤따르는 필드 수를 모르므로
            // reader는 더 읽지 않고 그 자리에서 예외로 거절해야 한다
            NetworkWriter writer = new NetworkWriter();
            writer.WriteBool(true);
            writer.WriteByte((byte)(CharacterAppearance.VERSION_2 + 1));

            NetworkReader reader = new NetworkReader(writer.ToArraySegment());
            Assert.Throws<InvalidOperationException>(() => reader.ReadCharacterAppearance());
        }

        [Test]
        public void WriteCharacterAppearance_InvalidV2ByteRange_ThrowsArgumentException()
        {
            // outfitId가 1바이트 범위를 넘으면 IsWellFormed가 거절한다. serializer는 그 값을 바이트로 자르지 않는다
            CharacterAppearance invalid = new CharacterAppearance(CharacterAppearance.VERSION_2, CharacterAppearance.BODY_TYPE_FEMALE,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT), NewSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                SAMPLE_SKIN_TONE, SAMPLE_EYE_COLOR, SAMPLE_BROW_COLOR, SAMPLE_LIP_COLOR, SAMPLE_HAIR_ID, SAMPLE_HAIR_COLOR,
                SAMPLE_FACE_ID, CharacterAppearance.VALUE_MAX + 1);
            NetworkWriter writer = new NetworkWriter();

            Assert.Throws<ArgumentException>(() => writer.WriteCharacterAppearance(invalid));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static CharacterAppearance CreateV1()
        {
            return new CharacterAppearance(CharacterAppearance.VERSION_1, CharacterAppearance.BODY_TYPE_FEMALE,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT), NewSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                SAMPLE_SKIN_TONE, SAMPLE_EYE_COLOR, SAMPLE_BROW_COLOR, SAMPLE_LIP_COLOR, SAMPLE_HAIR_ID, SAMPLE_HAIR_COLOR);
        }

        private static CharacterAppearance CreateV2()
        {
            return new CharacterAppearance(CharacterAppearance.VERSION_2, CharacterAppearance.BODY_TYPE_FEMALE,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT), NewSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                SAMPLE_SKIN_TONE, SAMPLE_EYE_COLOR, SAMPLE_BROW_COLOR, SAMPLE_LIP_COLOR, SAMPLE_HAIR_ID, SAMPLE_HAIR_COLOR,
                SAMPLE_FACE_ID, SAMPLE_OUTFIT_ID);
        }

        private static int[] NewSliders(int count)
        {
            int[] sliders = new int[count];
            for (int i = 0; i < count; ++i)
            {
                sliders[i] = CharacterAppearance.SLIDER_NEUTRAL;
            }

            return sliders;
        }

        private static void AssertSameAppearance(CharacterAppearance expected, CharacterAppearance actual)
        {
            Assert.IsNotNull(actual);
            Assert.AreEqual(expected.Version, actual.Version);
            Assert.AreEqual(expected.BodyType, actual.BodyType);
            CollectionAssert.AreEqual(expected.Face, actual.Face);
            CollectionAssert.AreEqual(expected.Body, actual.Body);
            Assert.AreEqual(expected.SkinTone, actual.SkinTone);
            Assert.AreEqual(expected.EyeColor, actual.EyeColor);
            Assert.AreEqual(expected.BrowColor, actual.BrowColor);
            Assert.AreEqual(expected.LipColor, actual.LipColor);
            Assert.AreEqual(expected.HairId, actual.HairId);
            Assert.AreEqual(expected.HairColor, actual.HairColor);
            Assert.IsTrue(actual.ContentEquals(expected));
        }
    }
}
