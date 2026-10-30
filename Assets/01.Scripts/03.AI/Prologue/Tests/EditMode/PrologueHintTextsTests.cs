// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class PrologueHintTextsTests
    {
        [Test]
        public void ForHint_None_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, PrologueHintTexts.ForHint(EForestHint.None));
        }

        [Test]
        public void ForHint_MoveAndAttack_NameTheRightMouseButtons()
        {
            StringAssert.Contains("오른쪽", PrologueHintTexts.ForHint(EForestHint.Move));
            StringAssert.Contains("왼쪽", PrologueHintTexts.ForHint(EForestHint.BasicAttack));
        }

        [Test]
        public void ForHint_SkillQ_NamesTheQKey()
        {
            StringAssert.Contains("Q", PrologueHintTexts.ForHint(EForestHint.SkillQ));
        }

        [Test]
        public void PotionHint_NumberKey_ShowsDigitWithoutAlphaPrefix()
        {
            string text = PrologueHintTexts.PotionHint();

            StringAssert.Contains("1", text);
            StringAssert.DoesNotContain("Alpha", text);
        }

        [Test]
        public void ForHint_EveryNonNoneHint_HasText()
        {
            foreach (EForestHint hint in System.Enum.GetValues(typeof(EForestHint)))
            {
                if (hint == EForestHint.None)
                {
                    continue;
                }

                Assert.IsFalse(string.IsNullOrEmpty(PrologueHintTexts.ForHint(hint)), hint.ToString());
            }
        }
    }
}
