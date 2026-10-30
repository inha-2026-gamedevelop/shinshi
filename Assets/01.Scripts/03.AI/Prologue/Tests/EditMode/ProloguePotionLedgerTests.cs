// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class ProloguePotionLedgerTests
    {
        [Test]
        public void StartForest_Called_GivesThree()
        {
            var ledger = new ProloguePotionLedger();

            ledger.StartForest();

            Assert.AreEqual(3, ledger.Count);
        }

        [Test]
        public void StartForest_ResumedAfterUse_ResetsToThreeNotAdds()
        {
            var ledger = new ProloguePotionLedger();
            ledger.StartForest();
            ledger.TryUse();

            ledger.StartForest();

            Assert.AreEqual(3, ledger.Count);
        }

        [Test]
        public void EnterWolbaek_AfterOneUse_KeepsRemainingAndRecordsEntry()
        {
            var ledger = new ProloguePotionLedger();
            ledger.StartForest();
            ledger.TryUse();

            ledger.EnterWolbaek();

            Assert.AreEqual(2, ledger.Count);
            Assert.AreEqual(2, ledger.WolbaekEntryCount);
        }

        [Test]
        public void ResumeWolbaek_AfterUsingInside_RestoresEntryCount()
        {
            var ledger = new ProloguePotionLedger();
            ledger.StartForest();
            ledger.TryUse();
            ledger.EnterWolbaek();
            ledger.TryUse();

            ledger.ResumeWolbaek(ledger.WolbaekEntryCount);

            Assert.AreEqual(2, ledger.Count);
        }

        [Test]
        public void ResumeWolbaek_NegativeStored_ClampsToZero()
        {
            var ledger = new ProloguePotionLedger();

            ledger.ResumeWolbaek(-5);

            Assert.AreEqual(0, ledger.Count);
        }

        [Test]
        public void TryUse_NoPotions_ReturnsFalse()
        {
            var ledger = new ProloguePotionLedger();

            Assert.IsFalse(ledger.TryUse());
            Assert.AreEqual(0, ledger.Count);
        }

        [Test]
        public void Clear_AfterPrologue_RemovesAll()
        {
            var ledger = new ProloguePotionLedger();
            ledger.StartForest();

            ledger.Clear();

            Assert.AreEqual(0, ledger.Count);
        }

        [TestCase(true, 2, true)]
        [TestCase(false, 2, false)]
        [TestCase(true, 0, false)]
        public void ShouldHintUse_DamagedAndHasPotion_OnlyWhenBoth(bool damaged, int potions, bool expected)
        {
            var ledger = new ProloguePotionLedger();
            ledger.ResumeWolbaek(potions);

            Assert.AreEqual(expected, ledger.ShouldHintUse(damaged));
        }
    }
}
