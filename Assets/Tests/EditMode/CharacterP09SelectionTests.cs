// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class CharacterP09SelectionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int FEMALE   = CharacterAppearance.BODY_TYPE_FEMALE;
        private const int MALE     = CharacterAppearance.BODY_TYPE_MALE;
        private const int WARRIOR  = ContractCodes.PlayerClass.WARRIOR;
        private const int SORCERER = ContractCodes.PlayerClass.SORCERER;
        private const int SUMMONER = ContractCodes.PlayerClass.SUMMONER;
        private const int SWORDSMAN  = ContractCodes.PlayerClass.SWORDSMAN;
        private const int MAGEKNIGHT = ContractCodes.PlayerClass.MAGEKNIGHT;
        private const int ARCHER     = ContractCodes.PlayerClass.ARCHER;

        private static readonly int[] G_Classes = { WARRIOR, SORCERER, SUMMONER, SWORDSMAN, MAGEKNIGHT, ARCHER };
        private static readonly int[] G_Sexes   = { FEMALE, MALE };

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(WARRIOR)]
        [TestCase(SORCERER)]
        [TestCase(SUMMONER)]
        [TestCase(SWORDSMAN)]
        [TestCase(MAGEKNIGHT)]
        [TestCase(ARCHER)]
        public void Constructor_FillsClassDefaults(int classId)
        {
            CharacterP09Selection selection = new CharacterP09Selection(classId);

            Assert.IsTrue(selection.ToAppearance().ContentEquals(CharacterAppearanceCatalog.CreateDefault(classId)));
        }

        [Test]
        public void ApplyClassDefaults_ChosenFieldsSurviveClassChange_OthersFollowNewClass()
        {
            CharacterP09Selection selection = new CharacterP09Selection(WARRIOR);
            selection.Select(CharacterP09Selection.EField.Hair, CharacterAppearanceCatalog.P09_HAIR_SKINHEAD);
            selection.Select(CharacterP09Selection.EField.EyeColor, 5);

            selection.ApplyClassDefaults(SORCERER);

            Assert.AreEqual(CharacterAppearanceCatalog.P09_HAIR_SKINHEAD, selection.Get(CharacterP09Selection.EField.Hair));
            Assert.AreEqual(5, selection.Get(CharacterP09Selection.EField.EyeColor));
            Assert.AreEqual(2, selection.Get(CharacterP09Selection.EField.Face));
            Assert.AreEqual(5, selection.Get(CharacterP09Selection.EField.HairColor));
            Assert.AreEqual(2, selection.Get(CharacterP09Selection.EField.Skin));
        }

        [Test]
        public void ApplyClassDefaults_UnknownClass_ChangesNothing()
        {
            CharacterP09Selection selection = new CharacterP09Selection(WARRIOR);
            CharacterP09Look      before    = selection.ToLook();

            selection.ApplyClassDefaults(99);

            Assert.IsTrue(before.Equals(selection.ToLook()));
        }

        [Test]
        public void ApplyClassDefaults_MaleThenClassChange_KeepsBustZero()
        {
            // 회귀: 직업 기본 가슴 크기 2가 남성 선택에 다시 채워지면 서버가 외형을 거절한다
            CharacterP09Selection selection = new CharacterP09Selection(WARRIOR);
            selection.Select(CharacterP09Selection.EField.BodyType, MALE);

            selection.ApplyClassDefaults(SORCERER);
            selection.ApplyClassDefaults(SUMMONER);

            Assert.AreEqual(0, selection.Get(CharacterP09Selection.EField.BustSize));
            Assert.IsTrue(CharacterAppearanceCatalog.IsAllowed(SUMMONER, selection.ToAppearance()));
        }

        [Test]
        public void ApplyClassDefaults_MaleFacialHairChosen_SurvivesClassChange()
        {
            CharacterP09Selection selection = new CharacterP09Selection(WARRIOR);
            selection.Select(CharacterP09Selection.EField.BodyType, MALE);
            selection.Select(CharacterP09Selection.EField.FacialHair, 6);

            selection.ApplyClassDefaults(SORCERER);

            Assert.AreEqual(6, selection.Get(CharacterP09Selection.EField.FacialHair));
        }

        [Test]
        public void Select_SwitchToMale_ZeroesBustAndFacialHair()
        {
            CharacterP09Selection selection = new CharacterP09Selection(WARRIOR);
            selection.Select(CharacterP09Selection.EField.BustSize, 3);

            selection.Select(CharacterP09Selection.EField.BodyType, MALE);

            Assert.IsTrue(selection.IsMale);
            Assert.AreEqual(0, selection.Get(CharacterP09Selection.EField.BustSize));
            Assert.AreEqual(0, selection.Get(CharacterP09Selection.EField.FacialHair));
        }

        [Test]
        public void Select_SwitchToFemale_ResetsFacialHairAndRestoresDefaultBust()
        {
            CharacterP09Selection selection = new CharacterP09Selection(WARRIOR);
            selection.Select(CharacterP09Selection.EField.BodyType, MALE);
            selection.Select(CharacterP09Selection.EField.FacialHair, 5);

            selection.Select(CharacterP09Selection.EField.BodyType, FEMALE);

            Assert.IsFalse(selection.IsMale);
            Assert.AreEqual(0, selection.Get(CharacterP09Selection.EField.FacialHair));
            Assert.AreEqual(CharacterAppearanceCatalog.P09_BUST_DEFAULT, selection.Get(CharacterP09Selection.EField.BustSize));
        }

        [Test]
        public void Select_SexSwitchDropsPreviousFacialHairAndBustChoices()
        {
            CharacterP09Selection selection = new CharacterP09Selection(WARRIOR);
            selection.Select(CharacterP09Selection.EField.BustSize, 3);
            selection.Select(CharacterP09Selection.EField.BodyType, MALE);
            selection.Select(CharacterP09Selection.EField.BodyType, FEMALE);

            // 이전 여성 선택(3)은 성별을 바꿀 때 버려지고 기본값 2로 돌아온다
            Assert.AreEqual(CharacterAppearanceCatalog.P09_BUST_DEFAULT, selection.Get(CharacterP09Selection.EField.BustSize));
        }

        [Test]
        public void Select_BustOnMaleOrFacialHairOnFemale_IsNormalizedAway()
        {
            CharacterP09Selection male = new CharacterP09Selection(WARRIOR);
            male.Select(CharacterP09Selection.EField.BodyType, MALE);
            male.Select(CharacterP09Selection.EField.BustSize, 3);

            CharacterP09Selection female = new CharacterP09Selection(WARRIOR);
            female.Select(CharacterP09Selection.EField.FacialHair, 4);

            Assert.AreEqual(0, male.Get(CharacterP09Selection.EField.BustSize));
            Assert.AreEqual(0, female.Get(CharacterP09Selection.EField.FacialHair));
        }

        [Test]
        public void ToAppearance_AllSexClassAndPickCombinations_AreAllowed()
        {
            int[] picks = { 1, 2, 3 };
            for (int s = 0; s < G_Sexes.Length; ++s)
            {
                for (int c = 0; c < G_Classes.Length; ++c)
                {
                    for (int c2 = 0; c2 < G_Classes.Length; ++c2)
                    {
                        for (int p = 0; p < picks.Length; ++p)
                        {
                            CharacterP09Selection selection = new CharacterP09Selection(G_Classes[c]);
                            selection.Select(CharacterP09Selection.EField.BodyType, G_Sexes[s]);
                            selection.Select(CharacterP09Selection.EField.Face, picks[p]);
                            selection.Select(CharacterP09Selection.EField.Hair, picks[p]);
                            selection.Select(CharacterP09Selection.EField.Skin, picks[p]);
                            selection.Select(CharacterP09Selection.EField.BustSize, picks[p]);
                            selection.Select(CharacterP09Selection.EField.FacialHair, picks[p] * 2);
                            selection.ApplyClassDefaults(G_Classes[c2]);

                            string label = $"sex={G_Sexes[s]} class={G_Classes[c]}->{G_Classes[c2]} pick={picks[p]}";
                            CharacterAppearance appearance = selection.ToAppearance();
                            Assert.IsTrue(appearance.IsWellFormed(), label);
                            Assert.IsTrue(CharacterAppearanceCatalog.IsAllowed(G_Classes[c2], appearance), label);
                        }
                    }
                }
            }
        }

        [Test]
        public void ToLook_MatchesAppearanceRoundTrip()
        {
            CharacterP09Selection selection = new CharacterP09Selection(SUMMONER);
            selection.Select(CharacterP09Selection.EField.BodyType, MALE);
            selection.Select(CharacterP09Selection.EField.FacialHair, 3);
            selection.Select(CharacterP09Selection.EField.Outfit, CharacterAppearanceCatalog.P09_OUTFIT_HEAD | CharacterAppearanceCatalog.P09_OUTFIT_LEG);

            Assert.IsTrue(CharacterP09Look.TryFrom(selection.ToAppearance(), out CharacterP09Look look));

            Assert.IsTrue(selection.ToLook().Equals(look));
            Assert.IsTrue(look.HasOutfit(CharacterAppearanceCatalog.P09_OUTFIT_LEG));
            Assert.IsFalse(look.HasOutfit(CharacterAppearanceCatalog.P09_OUTFIT_CHEST));
        }
    }
}
