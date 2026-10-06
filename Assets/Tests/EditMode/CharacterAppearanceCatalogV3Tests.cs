// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Tests
{
    /// <summary> 외형 계약 v3(P09)의 허용 범위, 직업 기본값, 해석 규칙 </summary>
    public class CharacterAppearanceCatalogV3Tests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int FEMALE = CharacterAppearance.BODY_TYPE_FEMALE;
        private const int MALE   = CharacterAppearance.BODY_TYPE_MALE;
        private const int WARRIOR  = ContractCodes.PlayerClass.WARRIOR;
        private const int SORCERER = ContractCodes.PlayerClass.SORCERER;
        private const int SUMMONER = ContractCodes.PlayerClass.SUMMONER;

        // 순서는 bodyType, face, hair, hairColor, eye, skin, facialHair, bust, outfit
        private static readonly int[][] G_ExpectedDefaults =
        {
            new[] { FEMALE, 1, 1, 1, 1, 1, 0, 2, 31 },
            new[] { FEMALE, 2, 2, 5, 4, 2, 0, 2, 31 },
            new[] { FEMALE, 3, 2, 8, 3, 1, 0, 2, 31 }
        };

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(WARRIOR)]
        [TestCase(SORCERER)]
        [TestCase(SUMMONER)]
        public void CreateDefault_EachClass_MatchesDefaultsTable(int classId)
        {
            int[]               e = G_ExpectedDefaults[classId];
            CharacterAppearance a = CharacterAppearanceCatalog.CreateDefault(classId);

            Assert.AreEqual(CharacterAppearance.VERSION_3, a.Version);
            Assert.AreEqual(e[0], a.BodyType);
            Assert.AreEqual(e[1], a.FaceId);
            Assert.AreEqual(e[2], a.HairId);
            Assert.AreEqual(e[3], a.HairColor);
            Assert.AreEqual(e[4], a.EyeColor);
            Assert.AreEqual(e[5], a.SkinTone);
            Assert.AreEqual(e[6], a.FacialHair);
            Assert.AreEqual(e[7], a.BustSize);
            Assert.AreEqual(e[8], a.OutfitId);
            Assert.IsTrue(CharacterAppearanceCatalog.IsAllowed(classId, a));
        }

        [TestCase(-1)]
        [TestCase(3)]
        [TestCase(99)]
        public void CreateDefault_UnknownClass_ReturnsNull(int classId)
        {
            Assert.IsNull(CharacterAppearanceCatalog.CreateDefault(classId));
        }

        [Test]
        public void CreateDefaultV2_KeepsLegacyTripoDefault()
        {
            CharacterAppearance v2 = CharacterAppearanceCatalog.CreateDefaultV2(SORCERER);

            Assert.AreEqual(CharacterAppearance.VERSION_2, v2.Version);
            Assert.AreEqual(CharacterAppearanceCatalog.FACE_CUTE, v2.FaceId);
            Assert.AreEqual(2, v2.HairId);
            Assert.AreEqual(2, v2.OutfitId);
            Assert.IsTrue(CharacterAppearanceCatalog.IsAllowed(SORCERER, v2));
        }

        // 범위 경계, 각 줄은 필드 하나만 경계값으로 바꾼다. 기준은 여성 기본값이다
        [TestCase(FEMALE, 1, 1, 1, 1, 1, 0, 1, 0, true)]
        [TestCase(FEMALE, 3, 3, 9, 5, 3, 0, 3, 31, true)]
        [TestCase(FEMALE, 0, 1, 1, 1, 1, 0, 2, 31, false)]
        [TestCase(FEMALE, 4, 1, 1, 1, 1, 0, 2, 31, false)]
        [TestCase(FEMALE, 1, 0, 1, 1, 1, 0, 2, 31, false)]
        [TestCase(FEMALE, 1, 4, 1, 1, 1, 0, 2, 31, false)]
        [TestCase(FEMALE, 1, 1, 0, 1, 1, 0, 2, 31, false)]
        [TestCase(FEMALE, 1, 1, 10, 1, 1, 0, 2, 31, false)]
        [TestCase(FEMALE, 1, 1, 1, 0, 1, 0, 2, 31, false)]
        [TestCase(FEMALE, 1, 1, 1, 6, 1, 0, 2, 31, false)]
        [TestCase(FEMALE, 1, 1, 1, 1, 0, 0, 2, 31, false)]
        [TestCase(FEMALE, 1, 1, 1, 1, 4, 0, 2, 31, false)]
        [TestCase(FEMALE, 1, 1, 1, 1, 1, 0, 2, 32, false)]
        [TestCase(FEMALE, 1, 1, 1, 1, 1, 0, 2, 255, false)]
        public void IsAllowed_V3FieldBoundaries_MatchContract(int bodyType, int face, int hair, int hairColor, int eye, int skin,
            int facialHair, int bust, int outfit, bool expected)
        {
            CharacterAppearance a = CharacterAppearanceCatalog.CreateP09(bodyType, face, hair, hairColor, eye, skin, facialHair, bust, outfit);

            Assert.AreEqual(expected, CharacterAppearanceCatalog.IsAllowed(WARRIOR, a));
        }

        [TestCase(0, true)]
        [TestCase(8, true)]
        [TestCase(9, false)]
        public void IsAllowed_MaleFacialHairRange(int facialHair, bool expected)
        {
            CharacterAppearance a = CharacterAppearanceCatalog.CreateP09(MALE, 1, 1, 1, 1, 1, facialHair, 0, 31);

            Assert.AreEqual(expected, CharacterAppearanceCatalog.IsAllowed(SORCERER, a));
        }

        [TestCase(1)]
        [TestCase(3)]
        public void IsAllowed_MaleWithBust_ReturnsFalse(int bust)
        {
            CharacterAppearance a = CharacterAppearanceCatalog.CreateP09(MALE, 1, 1, 1, 1, 1, 0, bust, 31);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, a));
        }

        [TestCase(1)]
        [TestCase(8)]
        public void IsAllowed_FemaleWithFacialHair_ReturnsFalse(int facialHair)
        {
            CharacterAppearance a = CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 1, 1, facialHair, 2, 31);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, a));
        }

        [Test]
        public void IsAllowed_FemaleWithoutBust_ReturnsFalse()
        {
            CharacterAppearance a = CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 1, 1, 0, 0, 31);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, a));
        }

        [Test]
        public void IsAllowed_V3NonZeroBrowOrLip_ReturnsFalse()
        {
            CharacterAppearance baseline = CharacterAppearanceCatalog.CreateDefault(WARRIOR);
            CharacterAppearance brow = Rebuild(baseline, browColor: 1);
            CharacterAppearance lip  = Rebuild(baseline, lipColor: 1);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, brow));
            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, lip));
        }

        [Test]
        public void IsAllowed_V3NonNeutralSliders_ReturnsFalse()
        {
            CharacterAppearance baseline = CharacterAppearanceCatalog.CreateDefault(WARRIOR);
            int[] face = ToArray(baseline.Face);
            int[] body = ToArray(baseline.Body);
            face[0]  = CharacterAppearance.SLIDER_NEUTRAL + 1;
            CharacterAppearance faceTampered = Rebuild(baseline, face: face);
            face[0]  = CharacterAppearance.SLIDER_NEUTRAL;
            body[9]  = CharacterAppearance.VALUE_MIN;
            CharacterAppearance bodyTampered = Rebuild(baseline, body: body);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, faceTampered));
            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, bodyTampered));
        }

        [Test]
        public void IsAllowed_V3HairIdOutsideP09Hairs_ReturnsFalse()
        {
            CharacterAppearance baseline = CharacterAppearanceCatalog.CreateDefault(WARRIOR);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, Rebuild(baseline, hairId: 0)));
            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, Rebuild(baseline, hairId: 4)));
        }

        [Test]
        public void IsAllowed_ClassDoesNotRestrictV3Look_AnyClassAcceptsAnyAllowedLook()
        {
            CharacterAppearance sorcererLook = CharacterAppearanceCatalog.CreateDefault(SORCERER);

            Assert.IsTrue(CharacterAppearanceCatalog.IsAllowed(WARRIOR, sorcererLook));
            Assert.IsTrue(CharacterAppearanceCatalog.IsAllowed(SUMMONER, sorcererLook));
        }

        [Test]
        public void IsAllowed_UnknownClassOrNull_ReturnsFalse()
        {
            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(99, CharacterAppearanceCatalog.CreateDefault(WARRIOR)));
            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, null));
        }

        [Test]
        public void IsAllowed_Version4_ReturnsFalse()
        {
            CharacterAppearance baseline = CharacterAppearanceCatalog.CreateDefault(WARRIOR);
            CharacterAppearance v4       = Rebuild(baseline, version: CharacterAppearance.CURRENT_VERSION + 1);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(WARRIOR, v4));
        }

        [TestCase(WARRIOR)]
        [TestCase(SORCERER)]
        [TestCase(SUMMONER)]
        public void TryResolve_AllowedV2_ReturnsClassDefaultV3(int classId)
        {
            CharacterAppearance v2 = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE, 3, 2);

            bool success = CharacterAppearanceCatalog.TryResolve(classId, v2, out CharacterAppearance canonical);

            Assert.IsTrue(success);
            Assert.IsTrue(canonical.ContentEquals(CharacterAppearanceCatalog.CreateDefault(classId)));
        }

        [Test]
        public void TryResolve_DisallowedV2_ReturnsFalse()
        {
            CharacterAppearance v2 = CharacterAppearanceCatalog.Create(2, 1, 1);

            Assert.IsFalse(CharacterAppearanceCatalog.TryResolve(WARRIOR, v2, out CharacterAppearance canonical));
            Assert.IsNull(canonical);
        }

        [Test]
        public void TryResolve_AllowedV3_ReturnsContentEqualCopyNotSameInstance()
        {
            CharacterAppearance stored = CharacterAppearanceCatalog.CreateP09(MALE, 3, 3, 9, 5, 3, 8, 0, 21);

            bool success = CharacterAppearanceCatalog.TryResolve(SUMMONER, stored, out CharacterAppearance canonical);

            Assert.IsTrue(success);
            Assert.IsTrue(canonical.ContentEquals(stored));
            Assert.AreNotSame(stored, canonical);
        }

        [Test]
        public void TryResolve_DisallowedV3_ReturnsFalse()
        {
            CharacterAppearance stored = CharacterAppearanceCatalog.CreateP09(MALE, 1, 1, 1, 1, 1, 0, 2, 31);

            Assert.IsFalse(CharacterAppearanceCatalog.TryResolve(WARRIOR, stored, out CharacterAppearance canonical));
            Assert.IsNull(canonical);
        }

        [Test]
        public void TryResolve_V3WithNonCanonicalExtras_IsRejectedNotCleaned()
        {
            CharacterAppearance baseline = CharacterAppearanceCatalog.CreateDefault(WARRIOR);
            CharacterAppearance stored   = Rebuild(baseline, browColor: 3);

            Assert.IsFalse(CharacterAppearanceCatalog.TryResolve(WARRIOR, stored, out _));
        }

        [TestCase(MALE, 5, 3, 5, 0)]
        [TestCase(FEMALE, 5, 3, 0, 3)]
        [TestCase(FEMALE, 5, 0, 0, 2)]
        [TestCase(FEMALE, 0, 9, 0, 2)]
        [TestCase(FEMALE, 0, 1, 0, 1)]
        [TestCase(MALE, 0, 0, 0, 0)]
        public void NormalizeForBodyType_AdjustsToGenderRules(int bodyType, int facialHair, int bust, int expectedFacialHair, int expectedBust)
        {
            CharacterAppearanceCatalog.NormalizeForBodyType(bodyType, ref facialHair, ref bust);

            Assert.AreEqual(expectedFacialHair, facialHair);
            Assert.AreEqual(expectedBust, bust);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static int[] ToArray(System.Collections.Generic.IReadOnlyList<int> source)
        {
            int[] copy = new int[source.Count];
            for (int i = 0; i < copy.Length; ++i)
            {
                copy[i] = source[i];
            }

            return copy;
        }

        private static CharacterAppearance Rebuild(CharacterAppearance source, int? version = null, int[] face = null, int[] body = null,
            int? browColor = null, int? lipColor = null, int? hairId = null)
        {
            return new CharacterAppearance(version ?? source.Version, source.BodyType, face ?? ToArray(source.Face), body ?? ToArray(source.Body),
                source.SkinTone, source.EyeColor, browColor ?? source.BrowColor, lipColor ?? source.LipColor, hairId ?? source.HairId,
                source.HairColor, source.FaceId, source.OutfitId, source.FacialHair, source.BustSize);
        }
    }
}
