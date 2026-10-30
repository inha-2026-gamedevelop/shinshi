// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class IllustrationSequenceTests
    {
        [Test]
        public void Click_WhileTyping_RevealsTextWithoutChangingPage()
        {
            var sequence = new IllustrationSequence(3);

            Assert.AreEqual(EIllustrationClick.RevealedText, sequence.Click());
            Assert.AreEqual(0, sequence.PageIndex);
            Assert.IsFalse(sequence.IsTyping);
        }

        [Test]
        public void Click_AfterRevealed_MovesToNextPage()
        {
            var sequence = new IllustrationSequence(3);
            sequence.Click();

            Assert.AreEqual(EIllustrationClick.NextPage, sequence.Click());
            Assert.AreEqual(1, sequence.PageIndex);
            Assert.IsTrue(sequence.IsTyping);
        }

        [Test]
        public void Click_TextCompletedNaturally_NextClickMovesPage()
        {
            var sequence = new IllustrationSequence(2);
            sequence.OnTextCompleted();

            Assert.AreEqual(EIllustrationClick.NextPage, sequence.Click());
        }

        [Test]
        public void Click_OnLastPageAfterRevealed_Finishes()
        {
            var sequence = new IllustrationSequence(1);
            sequence.Click();

            Assert.AreEqual(EIllustrationClick.Finished, sequence.Click());
            Assert.IsTrue(sequence.IsFinished);
        }

        [Test]
        public void Click_AfterFinished_DoesNothing()
        {
            var sequence = new IllustrationSequence(1);
            sequence.Skip();

            Assert.AreEqual(EIllustrationClick.None, sequence.Click());
        }

        [Test]
        public void Click_StartRevealed_ClosesWithSingleClick()
        {
            var sequence = new IllustrationSequence(1, startRevealed: true);

            Assert.IsFalse(sequence.IsTyping);
            Assert.AreEqual(EIllustrationClick.Finished, sequence.Click());
        }

        [Test]
        public void Skip_Midway_FinishesAtOnce()
        {
            var sequence = new IllustrationSequence(4);
            sequence.Click();
            sequence.Click();

            sequence.Skip();

            Assert.IsTrue(sequence.IsFinished);
        }

        [Test]
        public void Constructor_NoPages_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IllustrationSequence(0));
        }
    }
}
