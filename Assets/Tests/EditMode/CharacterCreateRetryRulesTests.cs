// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class CharacterCreateRetryRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PENDING_NAME = "여우아이";
        private const int    PENDING_CLASS_ID = ContractCodes.PlayerClass.WARRIOR;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void CanReuse_IdenticalSnapshot_ReturnsTrue()
        {
            CharacterLook look = CreateLook(CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);

            bool canReuse = CharacterCreateRetryRules.CanReuse(PENDING_NAME, PENDING_CLASS_ID, look,
                PENDING_NAME, PENDING_CLASS_ID, look);

            Assert.IsTrue(canReuse);
        }

        [Test]
        public void CanReuse_DifferentName_ReturnsFalse()
        {
            CharacterLook look = CreateLook(CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);

            bool canReuse = CharacterCreateRetryRules.CanReuse(PENDING_NAME, PENDING_CLASS_ID, look,
                "다른이름", PENDING_CLASS_ID, look);

            Assert.IsFalse(canReuse);
        }

        [Test]
        public void CanReuse_DifferentClassId_ReturnsFalse()
        {
            CharacterLook look = CreateLook(CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);

            bool canReuse = CharacterCreateRetryRules.CanReuse(PENDING_NAME, PENDING_CLASS_ID, look,
                PENDING_NAME, ContractCodes.PlayerClass.SORCERER, look);

            Assert.IsFalse(canReuse);
        }

        [Test]
        public void CanReuse_DifferentHair_ReturnsFalse()
        {
            CharacterLook pendingLook = CreateLook(CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            CharacterLook nextLook    = CreateLook(CharacterAppearanceCatalog.HAIR_SORCERER, CharacterAppearanceCatalog.OUTFIT_WARRIOR);

            bool canReuse = CharacterCreateRetryRules.CanReuse(PENDING_NAME, PENDING_CLASS_ID, pendingLook,
                PENDING_NAME, PENDING_CLASS_ID, nextLook);

            Assert.IsFalse(canReuse);
        }

        [Test]
        public void CanReuse_DifferentOutfit_ReturnsFalse()
        {
            CharacterLook pendingLook = CreateLook(CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            CharacterLook nextLook    = CreateLook(CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_SUMMONER);

            bool canReuse = CharacterCreateRetryRules.CanReuse(PENDING_NAME, PENDING_CLASS_ID, pendingLook,
                PENDING_NAME, PENDING_CLASS_ID, nextLook);

            Assert.IsFalse(canReuse);
        }

        [Test]
        public void CanReuse_P09IdenticalSnapshot_ReturnsTrue()
        {
            Assert.IsTrue(CharacterP09Look.TryFrom(CharacterAppearanceCatalog.CreateDefault(PENDING_CLASS_ID), out CharacterP09Look look));

            Assert.IsTrue(CharacterCreateRetryRules.CanReuse(PENDING_NAME, PENDING_CLASS_ID, look, PENDING_NAME, PENDING_CLASS_ID, look));
        }

        [Test]
        public void CanReuse_P09DifferentName_OrClass_ReturnsFalse()
        {
            CharacterP09Look look = CreateP09(CharacterAppearance.BODY_TYPE_FEMALE, 1, 1, 1, 1, 1, 0, 2, 31);

            Assert.IsFalse(CharacterCreateRetryRules.CanReuse(PENDING_NAME, PENDING_CLASS_ID, look, "다른이름", PENDING_CLASS_ID, look));
            Assert.IsFalse(CharacterCreateRetryRules.CanReuse(PENDING_NAME, PENDING_CLASS_ID, look,
                PENDING_NAME, ContractCodes.PlayerClass.SORCERER, look));
        }

        // 9개 값 중 하나만 달라도 새 요청이다
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void CanReuse_P09OneFieldDiffers_ReturnsFalse(int fieldIndex)
        {
            int[] baseValues = { CharacterAppearance.BODY_TYPE_MALE, 1, 1, 1, 1, 1, 1, 0, 30 };
            int[] changed    = (int[])baseValues.Clone();
            changed[fieldIndex] = (fieldIndex == 0) ? CharacterAppearance.BODY_TYPE_FEMALE : (changed[fieldIndex] + 1);
            CharacterP09Look pending = CreateP09(baseValues[0], baseValues[1], baseValues[2], baseValues[3], baseValues[4], baseValues[5],
                baseValues[6], baseValues[7], baseValues[8]);
            CharacterP09Look next    = CreateP09(changed[0], changed[1], changed[2], changed[3], changed[4], changed[5],
                changed[6], changed[7], changed[8]);

            Assert.IsFalse(CharacterCreateRetryRules.CanReuse(PENDING_NAME, PENDING_CLASS_ID, pending, PENDING_NAME, PENDING_CLASS_ID, next));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static CharacterP09Look CreateP09(int bodyType, int face, int hair, int hairColor, int eye, int skin,
            int facialHair, int bust, int outfit)
        {
            return new CharacterP09Look(bodyType, face, hair, hairColor, eye, skin, facialHair, bust, outfit);
        }

        private static CharacterLook CreateLook(int hairId, int outfitId)
        {
            return new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE, hairId, outfitId);
        }
    }
}
