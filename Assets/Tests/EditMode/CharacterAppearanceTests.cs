// System
using System;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class CharacterAppearanceTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SAMPLE_SKIN_TONE  = 90;
        private const int SAMPLE_EYE_COLOR  = 3;
        private const int SAMPLE_BROW_COLOR = 1;
        private const int SAMPLE_LIP_COLOR  = 5;
        private const int SAMPLE_HAIR_ID    = 300;
        private const int SAMPLE_HAIR_COLOR = 0;
        private const int OUT_OF_RANGE      = CharacterAppearance.VALUE_MAX + 1;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void IsWellFormed_CurrentVersionWithValidValues_ReturnsTrue()
        {
            Assert.IsTrue(CreateSample().IsWellFormed());
        }

        [Test]
        public void IsWellFormed_WrongFaceLength_ReturnsFalse()
        {
            var appearance = Create(CharacterAppearance.CURRENT_VERSION,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT - 1),
                NewSliders(CharacterAppearance.BODY_SLIDER_COUNT), SAMPLE_HAIR_ID);

            Assert.IsFalse(appearance.IsWellFormed());
        }

        [Test]
        public void IsWellFormed_SliderOutOfRange_ReturnsFalse()
        {
            int[] body = NewSliders(CharacterAppearance.BODY_SLIDER_COUNT);
            body[0] = OUT_OF_RANGE;

            var appearance = Create(CharacterAppearance.CURRENT_VERSION,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT), body, SAMPLE_HAIR_ID);

            Assert.IsFalse(appearance.IsWellFormed());
        }

        [Test]
        public void IsWellFormed_UnknownVersion_ReturnsFalse()
        {
            var appearance = Create(CharacterAppearance.CURRENT_VERSION + 1,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT),
                NewSliders(CharacterAppearance.BODY_SLIDER_COUNT), SAMPLE_HAIR_ID);

            Assert.IsFalse(appearance.IsWellFormed());
        }

        [Test]
        public void Constructor_SourceArrayChanged_KeepsOriginalValues()
        {
            int[] face       = NewSliders(CharacterAppearance.FACE_SLIDER_COUNT);
            var   appearance = Create(CharacterAppearance.CURRENT_VERSION, face,
                NewSliders(CharacterAppearance.BODY_SLIDER_COUNT), SAMPLE_HAIR_ID);

            face[0] = CharacterAppearance.VALUE_MIN;

            Assert.AreEqual(CharacterAppearance.SLIDER_NEUTRAL, appearance.Face[0]);
        }

        [Test]
        public void ReadCharacterAppearance_AfterWrite_ReturnsSameValues()
        {
            var original = CreateSample();

            var copy = RoundTrip(original);

            Assert.AreEqual(original.Version, copy.Version);
            Assert.AreEqual(original.BodyType, copy.BodyType);
            CollectionAssert.AreEqual(original.Face, copy.Face);
            CollectionAssert.AreEqual(original.Body, copy.Body);
            Assert.AreEqual(original.SkinTone, copy.SkinTone);
            Assert.AreEqual(original.EyeColor, copy.EyeColor);
            Assert.AreEqual(original.BrowColor, copy.BrowColor);
            Assert.AreEqual(original.LipColor, copy.LipColor);
            Assert.AreEqual(original.HairId, copy.HairId);
            Assert.AreEqual(original.HairColor, copy.HairColor);
            Assert.IsTrue(copy.IsWellFormed());
        }

        [Test]
        public void ReadCharacterAppearance_AfterWriteNull_ReturnsNull()
        {
            Assert.IsNull(RoundTrip(null));
        }

        [Test]
        public void WriteCharacterAppearance_NotWellFormed_Throws()
        {
            var invalid = Create(CharacterAppearance.CURRENT_VERSION,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT),
                NewSliders(CharacterAppearance.BODY_SLIDER_COUNT), CharacterAppearance.HAIR_ID_MAX + 1);
            var writer  = new NetworkWriter();

            Assert.Throws<ArgumentException>(() => writer.WriteCharacterAppearance(invalid));
        }

        [Test]
        public void ReadCharacterAppearance_TruncatedData_Throws()
        {
            var writer = new NetworkWriter();
            writer.WriteCharacterAppearance(CreateSample());
            ArraySegment<byte> full = writer.ToArraySegment();
            var truncated = new ArraySegment<byte>(full.Array, full.Offset, full.Count - 1);

            var reader = new NetworkReader(truncated);

            Assert.Throws<System.IO.EndOfStreamException>(() => reader.ReadCharacterAppearance());
        }

        [Test]
        public void Face_CastToArray_CannotModifyInternalValues()
        {
            var appearance = CreateSample();

            Assert.IsNull(appearance.Face as int[]);
        }

        [Test]
        public void InvalidAppearance_ContractCodeAndEnum_HaveSameValue()
        {
            Assert.AreEqual(ContractCodes.RejectReason.INVALID_APPEARANCE, (int)ERejectReason.InvalidAppearance);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static CharacterAppearance CreateSample()
        {
            int[] face = NewSliders(CharacterAppearance.FACE_SLIDER_COUNT);
            int[] body = NewSliders(CharacterAppearance.BODY_SLIDER_COUNT);
            face[0] = CharacterAppearance.VALUE_MIN;
            body[body.Length - 1] = CharacterAppearance.VALUE_MAX;

            return Create(CharacterAppearance.CURRENT_VERSION, face, body, SAMPLE_HAIR_ID);
        }

        private static CharacterAppearance Create(int version, int[] face, int[] body, int hairId)
        {
            return new CharacterAppearance(version, CharacterAppearance.BODY_TYPE_FEMALE, face, body,
                SAMPLE_SKIN_TONE, SAMPLE_EYE_COLOR, SAMPLE_BROW_COLOR, SAMPLE_LIP_COLOR, hairId, SAMPLE_HAIR_COLOR);
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

        private static CharacterAppearance RoundTrip(CharacterAppearance value)
        {
            var writer = new NetworkWriter();
            writer.WriteCharacterAppearance(value);

            var reader = new NetworkReader(writer.ToArraySegment());
            return reader.ReadCharacterAppearance();
        }
    }
}
