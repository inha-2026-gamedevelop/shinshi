// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class PrologueEntryRulesTests
    {
        [Test]
        public void Decide_AccountNeverCompleted_RequiresPlay()
        {
            Assert.AreEqual(EPrologueEntry.PlayRequired, PrologueEntryRules.Decide(false));
        }

        [Test]
        public void Decide_AccountAlreadyCompleted_AsksPlayOrSkip()
        {
            Assert.AreEqual(EPrologueEntry.AskPlayOrSkip, PrologueEntryRules.Decide(true));
        }

        [Test]
        public void StartStage_Play_StartsAtIntro()
        {
            Assert.AreEqual(EPrologueStage.Intro, PrologueEntryRules.StartStage(EPrologueChoice.Play));
        }

        [Test]
        public void StartStage_SkipToTown_GoesStraightToDone()
        {
            Assert.AreEqual(EPrologueStage.Done, PrologueEntryRules.StartStage(EPrologueChoice.SkipToTown));
        }
    }
}
