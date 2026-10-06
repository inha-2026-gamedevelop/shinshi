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

        /****************************************
        *                Helpers
        ****************************************/

        private static CharacterLook CreateLook(int hairId, int outfitId)
        {
            return new CharacterLook(CharacterAppearanceCatalog.FACE_CUTE, hairId, outfitId);
        }
    }
}
