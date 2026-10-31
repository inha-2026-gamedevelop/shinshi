// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class PrologueStageRulesTests
    {
        [TestCase(EPrologueStage.Intro, EPrologueStage.Forest)]
        [TestCase(EPrologueStage.Forest, EPrologueStage.Wolbaek)]
        [TestCase(EPrologueStage.Wolbaek, EPrologueStage.Ending)]
        [TestCase(EPrologueStage.Ending, EPrologueStage.Done)]
        [TestCase(EPrologueStage.Done, EPrologueStage.Done)]
        public void Next_EachStage_ReturnsFollowingStage(EPrologueStage current, EPrologueStage expected)
        {
            Assert.AreEqual(expected, PrologueStageRules.Next(current));
        }

        [TestCase(0, EPrologueStage.Intro)]
        [TestCase(2, EPrologueStage.Wolbaek)]
        [TestCase(4, EPrologueStage.Done)]
        public void TryFromStored_KnownNumber_RestoresStage(int stored, EPrologueStage expected)
        {
            Assert.IsTrue(PrologueStageRules.TryFromStored(stored, out EPrologueStage stage));
            Assert.AreEqual(expected, stage);
        }

        [TestCase(-1)]
        [TestCase(5)]
        [TestCase(99)]
        public void TryFromStored_UnknownNumber_ReturnsFalse(int stored)
        {
            Assert.IsFalse(PrologueStageRules.TryFromStored(stored, out _));
        }

        [Test]
        public void RevivesOnDown_OnlyForest_ReturnsTrue()
        {
            Assert.IsTrue(PrologueStageRules.RevivesOnDown(EPrologueStage.Forest));
            Assert.IsFalse(PrologueStageRules.RevivesOnDown(EPrologueStage.Wolbaek));
            Assert.IsFalse(PrologueStageRules.RevivesOnDown(EPrologueStage.Intro));
        }
    }
}
