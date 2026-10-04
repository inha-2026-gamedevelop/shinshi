// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Tests
{
    public class CharacterAppearanceCatalogTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryResolve_NullStored_ReturnsClassDefault()
        {
            bool success = CharacterAppearanceCatalog.TryResolve(ContractCodes.PlayerClass.SORCERER, null, out CharacterAppearance canonical);

            Assert.IsTrue(success);
            Assert.AreEqual(CharacterAppearance.VERSION_2, canonical.Version);
            Assert.AreEqual(CharacterAppearanceCatalog.FACE_CUTE, canonical.FaceId);
            Assert.AreEqual(CharacterAppearanceCatalog.HAIR_SORCERER, canonical.HairId);
            Assert.AreEqual(CharacterAppearanceCatalog.OUTFIT_SORCERER, canonical.OutfitId);
        }

        [Test]
        public void TryResolve_V1Stored_IgnoresContentAndReturnsClassDefault()
        {
            // v1은 face/outfit 선택을 몰랐다. 저장된 v1 값이 무엇이든 직업 기본값으로 정규화한다
            CharacterAppearance legacy = new CharacterAppearance(CharacterAppearance.VERSION_1, CharacterAppearance.BODY_TYPE_FEMALE,
                NewSliders(CharacterAppearance.FACE_SLIDER_COUNT), NewSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, 999, CharacterAppearance.VALUE_MIN);

            bool success = CharacterAppearanceCatalog.TryResolve(ContractCodes.PlayerClass.WARRIOR, legacy, out CharacterAppearance canonical);

            Assert.IsTrue(success);
            Assert.AreEqual(CharacterAppearanceCatalog.HAIR_WARRIOR, canonical.HairId);
            Assert.AreEqual(CharacterAppearanceCatalog.OUTFIT_WARRIOR, canonical.OutfitId);
        }

        [Test]
        public void TryResolve_UnknownClassId_ReturnsFalse()
        {
            const int UNKNOWN_CLASS_ID = 99;
            CharacterAppearance v2 = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);

            bool success = CharacterAppearanceCatalog.TryResolve(UNKNOWN_CLASS_ID, v2, out CharacterAppearance canonical);

            Assert.IsFalse(success);
            Assert.IsNull(canonical);
        }

        [TestCase(0)]
        [TestCase(2)]
        public void IsAllowed_FaceIdOutsideCuteOnly_ReturnsFalse(int faceId)
        {
            CharacterAppearance v2 = CharacterAppearanceCatalog.Create(faceId,
                CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(ContractCodes.PlayerClass.WARRIOR, v2));
        }

        [TestCase(0)]
        [TestCase(4)]
        public void IsAllowed_HairIdOutOfPartRange_ReturnsFalse(int hairId)
        {
            CharacterAppearance v2 = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE,
                hairId, CharacterAppearanceCatalog.OUTFIT_WARRIOR);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(ContractCodes.PlayerClass.WARRIOR, v2));
        }

        [TestCase(0)]
        [TestCase(4)]
        public void IsAllowed_OutfitIdOutOfPartRange_ReturnsFalse(int outfitId)
        {
            CharacterAppearance v2 = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_WARRIOR, outfitId);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(ContractCodes.PlayerClass.WARRIOR, v2));
        }

        [Test]
        public void IsAllowed_NonNeutralSlider_ReturnsFalse()
        {
            CharacterAppearance v2 = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            CharacterAppearance tampered = new CharacterAppearance(v2.Version, v2.BodyType,
                Mutate(v2.Face, 0, CharacterAppearance.SLIDER_NEUTRAL + 1), (int[])CloneBody(v2),
                v2.SkinTone, v2.EyeColor, v2.BrowColor, v2.LipColor, v2.HairId, v2.HairColor, v2.FaceId, v2.OutfitId);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(ContractCodes.PlayerClass.WARRIOR, tampered));
        }

        [Test]
        public void IsAllowed_UnknownClassId_ReturnsFalse()
        {
            CharacterAppearance v2 = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE,
                CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);

            Assert.IsFalse(CharacterAppearanceCatalog.IsAllowed(ContractCodes.PlayerClass.WARRIOR + 100, v2));
        }

        [Test]
        public void IsAllowed_AllNineHairOutfitCombinations_AreAllowedForEveryClass()
        {
            int[] classIds = { ContractCodes.PlayerClass.WARRIOR, ContractCodes.PlayerClass.SORCERER, ContractCodes.PlayerClass.SUMMONER };
            int[] hairIds  = { CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.HAIR_SORCERER, CharacterAppearanceCatalog.HAIR_SUMMONER };
            int[] outfitIds = { CharacterAppearanceCatalog.OUTFIT_WARRIOR, CharacterAppearanceCatalog.OUTFIT_SORCERER, CharacterAppearanceCatalog.OUTFIT_SUMMONER };

            for (int c = 0; c < classIds.Length; ++c)
            {
                for (int h = 0; h < hairIds.Length; ++h)
                {
                    for (int o = 0; o < outfitIds.Length; ++o)
                    {
                        CharacterAppearance v2 = CharacterAppearanceCatalog.Create(CharacterAppearanceCatalog.FACE_CUTE, hairIds[h], outfitIds[o]);

                        // 헤어/의상 선택은 직업과 독립이므로 어느 직업이든 9조합 전부 허용한다
                        Assert.IsTrue(CharacterAppearanceCatalog.IsAllowed(classIds[c], v2),
                            $"class={classIds[c]} hair={hairIds[h]} outfit={outfitIds[o]}");
                    }
                }
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        [TestCase(-1)]
        [TestCase(3)]
        public void TryResolve_UnknownClass_RejectsNullAndLegacy(int classId)
        {
            Assert.IsFalse(CharacterAppearanceCatalog.TryResolve(classId, null, out CharacterAppearance nullCanonical));
            Assert.IsNull(nullCanonical);

            CharacterAppearance legacy = new CharacterAppearance(CharacterAppearance.VERSION_1,
                CharacterAppearance.BODY_TYPE_FEMALE, NewSliders(CharacterAppearance.FACE_SLIDER_COUNT),
                NewSliders(CharacterAppearance.BODY_SLIDER_COUNT), CharacterAppearance.SLIDER_NEUTRAL,
                CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN);
            Assert.IsFalse(CharacterAppearanceCatalog.TryResolve(classId, legacy, out CharacterAppearance legacyCanonical));
            Assert.IsNull(legacyCanonical);
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

        private static int[] Mutate(System.Collections.Generic.IReadOnlyList<int> source, int index, int value)
        {
            int[] copy = new int[source.Count];
            for (int i = 0; i < copy.Length; ++i)
            {
                copy[i] = source[i];
            }

            copy[index] = value;
            return copy;
        }

        private static int[] CloneBody(CharacterAppearance source)
        {
            int[] copy = new int[source.Body.Count];
            for (int i = 0; i < copy.Length; ++i)
            {
                copy[i] = source.Body[i];
            }

            return copy;
        }
    }
}
