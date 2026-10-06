// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    /// <summary> 외형 계약 v3의 구조 검증과 JSON 형태, v1/v2 JSON이 바뀌지 않음을 고정한다 </summary>
    public class CharacterAppearanceV3Tests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 변경 전 코드가 CharacterAppearanceCatalog.Create(1, 2, 3)으로 만든 v2 JSON. 과거 request_hash가 이 문자열에 묶여 있다
        private const string V2_GOLDEN_JSON =
            "{\"version\":2,\"bodyType\":0,\"face\":[128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128],"
            + "\"body\":[128,128,128,128,128,128,128,128,128,128],\"skinTone\":128,\"eyeColor\":0,\"browColor\":0,\"lipColor\":0,"
            + "\"hairId\":2,\"hairColor\":0,\"faceId\":1,\"outfitId\":3}";

        private const string V1_GOLDEN_JSON =
            "{\"version\":1,\"bodyType\":0,\"face\":[128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128],"
            + "\"body\":[128,128,128,128,128,128,128,128,128,128],\"skinTone\":128,\"eyeColor\":0,\"browColor\":0,\"lipColor\":0,"
            + "\"hairId\":0,\"hairColor\":0}";

        private const int FACE_ID_SAMPLE   = 1;
        private const int OUTFIT_ID_SAMPLE = 3;
        private const int SAMPLE_HAIR_ID   = 2;
        private const int STRAY_FACIAL     = 4;
        private const int STRAY_BUST       = 2;

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(CharacterAppearance.BODY_TYPE_FEMALE)]
        [TestCase(CharacterAppearance.BODY_TYPE_MALE)]
        public void IsWellFormed_V3BodyType_FemaleAndMaleAreValid(int bodyType)
        {
            Assert.IsTrue(NewAppearance(CharacterAppearance.VERSION_3, bodyType).IsWellFormed());
        }

        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(255)]
        public void IsWellFormed_V3BodyTypeOutsideZeroOne_ReturnsFalse(int bodyType)
        {
            Assert.IsFalse(NewAppearance(CharacterAppearance.VERSION_3, bodyType).IsWellFormed());
        }

        [TestCase(CharacterAppearance.VERSION_1)]
        [TestCase(CharacterAppearance.VERSION_2)]
        public void IsWellFormed_LegacyVersionWithMaleBodyType_ReturnsFalse(int version)
        {
            Assert.IsFalse(NewAppearance(version, CharacterAppearance.BODY_TYPE_MALE).IsWellFormed());
        }

        [Test]
        public void IsWellFormed_V3FacialHairOrBustOutOfByteRange_ReturnsFalse()
        {
            CharacterAppearance badFacialHair = NewAppearance(CharacterAppearance.VERSION_3, CharacterAppearance.BODY_TYPE_MALE, 256, 0);
            CharacterAppearance badBust       = NewAppearance(CharacterAppearance.VERSION_3, CharacterAppearance.BODY_TYPE_FEMALE, 0, -1);

            Assert.IsFalse(badFacialHair.IsWellFormed());
            Assert.IsFalse(badBust.IsWellFormed());
        }

        [Test]
        public void IsWellFormed_Version4_ReturnsFalse()
        {
            Assert.IsFalse(NewAppearance(CharacterAppearance.VERSION_3 + 1, CharacterAppearance.BODY_TYPE_FEMALE).IsWellFormed());
        }

        [Test]
        public void CurrentVersion_IsVersion3()
        {
            Assert.AreEqual(CharacterAppearance.VERSION_3, CharacterAppearance.CURRENT_VERSION);
            Assert.AreEqual(3, CharacterAppearance.CURRENT_VERSION);
        }

        [Test]
        public void Serialize_V3WithZeroFacialHairAndBust_OmitsBothKeys()
        {
            string json = BackendJson.Serialize(NewAppearance(CharacterAppearance.VERSION_3, CharacterAppearance.BODY_TYPE_FEMALE, 0, 0));

            StringAssert.DoesNotContain("facialHair", json);
            StringAssert.DoesNotContain("bustSize", json);
        }

        [Test]
        public void Serialize_V3WithNonZeroFacialHair_EmitsOnlyFacialHair()
        {
            string json = BackendJson.Serialize(NewAppearance(CharacterAppearance.VERSION_3, CharacterAppearance.BODY_TYPE_MALE, 5, 0));

            StringAssert.Contains("\"facialHair\":5", json);
            StringAssert.DoesNotContain("bustSize", json);
        }

        [Test]
        public void Serialize_V3WithNonZeroBust_EmitsOnlyBustSize()
        {
            string json = BackendJson.Serialize(NewAppearance(CharacterAppearance.VERSION_3, CharacterAppearance.BODY_TYPE_FEMALE, 0, 2));

            StringAssert.Contains("\"bustSize\":2", json);
            StringAssert.DoesNotContain("facialHair", json);
        }

        [Test]
        public void Serialize_V3WarriorDefault_PutsBustSizeAfterOutfitId()
        {
            string json = BackendJson.Serialize(CharacterAppearanceCatalog.CreateDefault(ContractCodes.PlayerClass.WARRIOR));

            StringAssert.Contains("\"faceId\":1,\"outfitId\":31,\"bustSize\":2}", json);
        }

        [Test]
        public void Serialize_V2WithStrayV3Fields_OmitsFacialHairAndBust()
        {
            CharacterAppearance v2 = NewAppearance(CharacterAppearance.VERSION_2, CharacterAppearance.BODY_TYPE_FEMALE, STRAY_FACIAL, STRAY_BUST);

            string json = BackendJson.Serialize(v2);

            StringAssert.DoesNotContain("facialHair", json);
            StringAssert.DoesNotContain("bustSize", json);
        }

        [Test]
        public void Serialize_V1WithStrayNewFields_OmitsAllNewKeys()
        {
            CharacterAppearance v1 = NewAppearance(CharacterAppearance.VERSION_1, CharacterAppearance.BODY_TYPE_FEMALE, STRAY_FACIAL, STRAY_BUST);

            string json = BackendJson.Serialize(v1);

            StringAssert.DoesNotContain("faceId", json);
            StringAssert.DoesNotContain("outfitId", json);
            StringAssert.DoesNotContain("facialHair", json);
            StringAssert.DoesNotContain("bustSize", json);
        }

        [Test]
        public void Serialize_V2Create123_MatchesGoldenJson()
        {
            string json = BackendJson.Serialize(CharacterAppearanceCatalog.Create(1, 2, 3));

            Assert.AreEqual(V2_GOLDEN_JSON, json);
        }

        [Test]
        public void Serialize_V1Neutral_MatchesGoldenJson()
        {
            CharacterAppearance v1 = new CharacterAppearance(CharacterAppearance.VERSION_1, CharacterAppearance.BODY_TYPE_FEMALE,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT), NewSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN);

            Assert.AreEqual(V1_GOLDEN_JSON, BackendJson.Serialize(v1));
        }

        [Test]
        public void Deserialize_V2GoldenJson_ReserializesToSameJson()
        {
            CharacterAppearance parsed = BackendJson.Deserialize<CharacterAppearance>(V2_GOLDEN_JSON);

            Assert.AreEqual(CharacterAppearance.VERSION_2, parsed.Version);
            Assert.AreEqual(V2_GOLDEN_JSON, BackendJson.Serialize(parsed));
        }

        [Test]
        public void Deserialize_V3Json_KeepsFacialHairAndBust()
        {
            CharacterAppearance original = CharacterAppearanceCatalog.CreateP09(CharacterAppearance.BODY_TYPE_MALE, 2, 3, 4, 5, 3, 7, 0, 21);

            CharacterAppearance copy = BackendJson.Deserialize<CharacterAppearance>(BackendJson.Serialize(original));

            Assert.IsTrue(original.ContentEquals(copy));
            Assert.AreEqual(7, copy.FacialHair);
        }

        [Test]
        public void ContentEquals_DiffersOnlyInFacialHairOrBust_ReturnsFalse()
        {
            CharacterAppearance baseline = NewAppearance(CharacterAppearance.VERSION_3, CharacterAppearance.BODY_TYPE_MALE, 1, 0);

            Assert.IsFalse(baseline.ContentEquals(NewAppearance(CharacterAppearance.VERSION_3, CharacterAppearance.BODY_TYPE_MALE, 2, 0)));
            Assert.IsFalse(baseline.ContentEquals(NewAppearance(CharacterAppearance.VERSION_3, CharacterAppearance.BODY_TYPE_MALE, 1, 1)));
            Assert.IsTrue(baseline.ContentEquals(NewAppearance(CharacterAppearance.VERSION_3, CharacterAppearance.BODY_TYPE_MALE, 1, 0)));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static CharacterAppearance NewAppearance(int version, int bodyType, int facialHair = 0, int bustSize = 0)
        {
            return new CharacterAppearance(version, bodyType, NewSliders(CharacterAppearance.FACE_SLIDER_COUNT),
                NewSliders(CharacterAppearance.BODY_SLIDER_COUNT), CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN, SAMPLE_HAIR_ID, CharacterAppearance.VALUE_MIN,
                FACE_ID_SAMPLE, OUTFIT_ID_SAMPLE, facialHair, bustSize);
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
    }
}
