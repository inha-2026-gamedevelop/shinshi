// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class CharacterLookSelectionTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Constructor_AppliesClassDefaultHairAndOutfit()
        {
            CharacterLookSelection selection = new CharacterLookSelection(ContractCodes.PlayerClass.SUMMONER);

            Assert.AreEqual(CharacterAppearanceCatalog.FACE_CUTE, selection.FaceId);
            Assert.AreEqual(CharacterAppearanceCatalog.HAIR_SUMMONER, selection.HairId);
            Assert.AreEqual(CharacterAppearanceCatalog.OUTFIT_SUMMONER, selection.OutfitId);
        }

        [Test]
        public void ApplyClassDefaults_NoManualChoiceYet_FollowsNewClassDefaults()
        {
            CharacterLookSelection selection = new CharacterLookSelection(ContractCodes.PlayerClass.WARRIOR);

            selection.ApplyClassDefaults(ContractCodes.PlayerClass.SORCERER);

            Assert.AreEqual(CharacterAppearanceCatalog.HAIR_SORCERER, selection.HairId);
            Assert.AreEqual(CharacterAppearanceCatalog.OUTFIT_SORCERER, selection.OutfitId);
        }

        [Test]
        public void ApplyClassDefaults_AfterManualHairChoice_KeepsHairButUpdatesOutfit()
        {
            CharacterLookSelection selection = new CharacterLookSelection(ContractCodes.PlayerClass.WARRIOR);

            selection.SelectHair(CharacterAppearanceCatalog.HAIR_SUMMONER);
            selection.ApplyClassDefaults(ContractCodes.PlayerClass.SORCERER);

            Assert.AreEqual(CharacterAppearanceCatalog.HAIR_SUMMONER, selection.HairId);
            Assert.AreEqual(CharacterAppearanceCatalog.OUTFIT_SORCERER, selection.OutfitId);
        }

        [Test]
        public void ApplyClassDefaults_AfterManualOutfitChoice_KeepsOutfitButUpdatesHair()
        {
            CharacterLookSelection selection = new CharacterLookSelection(ContractCodes.PlayerClass.WARRIOR);

            selection.SelectOutfit(CharacterAppearanceCatalog.OUTFIT_SUMMONER);
            selection.ApplyClassDefaults(ContractCodes.PlayerClass.SORCERER);

            Assert.AreEqual(CharacterAppearanceCatalog.HAIR_SORCERER, selection.HairId);
            Assert.AreEqual(CharacterAppearanceCatalog.OUTFIT_SUMMONER, selection.OutfitId);
        }

        [Test]
        public void ApplyClassDefaults_AfterBothManualChoices_KeepsBothAcrossRepeatedClassChanges()
        {
            CharacterLookSelection selection = new CharacterLookSelection(ContractCodes.PlayerClass.WARRIOR);

            selection.SelectHair(CharacterAppearanceCatalog.HAIR_SUMMONER);
            selection.SelectOutfit(CharacterAppearanceCatalog.OUTFIT_WARRIOR);
            selection.ApplyClassDefaults(ContractCodes.PlayerClass.SORCERER);
            selection.ApplyClassDefaults(ContractCodes.PlayerClass.WARRIOR);

            Assert.AreEqual(CharacterAppearanceCatalog.HAIR_SUMMONER, selection.HairId);
            Assert.AreEqual(CharacterAppearanceCatalog.OUTFIT_WARRIOR, selection.OutfitId);
        }

        [Test]
        public void ToLook_ReflectsCurrentSelection()
        {
            CharacterLookSelection selection = new CharacterLookSelection(ContractCodes.PlayerClass.WARRIOR);
            selection.SelectHair(CharacterAppearanceCatalog.HAIR_SORCERER);
            selection.SelectOutfit(CharacterAppearanceCatalog.OUTFIT_SUMMONER);

            CharacterLook look = selection.ToLook();

            Assert.AreEqual(CharacterAppearanceCatalog.FACE_CUTE, look.FaceId);
            Assert.AreEqual(CharacterAppearanceCatalog.HAIR_SORCERER, look.HairId);
            Assert.AreEqual(CharacterAppearanceCatalog.OUTFIT_SUMMONER, look.OutfitId);
        }
    }
}
