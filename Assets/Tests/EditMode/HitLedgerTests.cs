// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public class HitLedgerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    SMALL_CAPACITY = 2;
        private const double RETENTION      = 10.0;
        private const double START_TIME     = 0.0;
        private const double HALF_RETENTION = RETENTION * 0.5;

        // 가장 긴 단일 동작 시간. 보관 시간의 절반보다 길게 잡아 절반 시각에서는 회수하지 않는다
        private const double FORCE_EVICT_AGE = 6.0;
        private const double PAST_FORCE_AGE  = 7.0;
        private const int    TARGET          = 7;
        private const int    NEGATIVE_ID     = -1;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryRecord_SamePairTwice_SecondIsDuplicate()
        {
            var ledger = new HitLedger(SMALL_CAPACITY, RETENTION, FORCE_EVICT_AGE);

            Assert.AreEqual(EHitRecord.Recorded, ledger.TryRecord(1, TARGET, START_TIME));
            Assert.AreEqual(EHitRecord.Duplicate, ledger.TryRecord(1, TARGET, START_TIME));
        }

        [Test]
        public void TryRecord_AtCapacityWithinRetention_ReturnsFull()
        {
            var ledger = new HitLedger(SMALL_CAPACITY, RETENTION, FORCE_EVICT_AGE);
            ledger.TryRecord(1, TARGET, START_TIME);
            ledger.TryRecord(2, TARGET, START_TIME);

            Assert.AreEqual(EHitRecord.Full, ledger.TryRecord(3, TARGET, HALF_RETENTION));
        }

        [Test]
        public void TryRecord_AtCapacityAfterRetention_PrunesAndRecords()
        {
            var ledger = new HitLedger(SMALL_CAPACITY, RETENTION, FORCE_EVICT_AGE);
            ledger.TryRecord(1, TARGET, START_TIME);
            ledger.TryRecord(2, TARGET, START_TIME);

            Assert.AreEqual(EHitRecord.Recorded, ledger.TryRecord(3, TARGET, RETENTION));
            Assert.IsFalse(ledger.Contains(1, TARGET));
        }

        [Test]
        public void TryRecord_AtCapacityWithinRetention_CountsFull()
        {
            var ledger = new HitLedger(SMALL_CAPACITY, RETENTION, FORCE_EVICT_AGE);
            ledger.TryRecord(1, TARGET, START_TIME);
            ledger.TryRecord(2, TARGET, START_TIME);

            ledger.TryRecord(3, TARGET, HALF_RETENTION);
            ledger.TryRecord(4, TARGET, HALF_RETENTION);

            Assert.AreEqual(2, ledger.FullCount);
            Assert.AreEqual(0, ledger.ForcedEvictionCount);
        }

        [Test]
        public void TryRecord_FullWithRecordsOlderThanLongestAction_ForceEvictsAndRecords()
        {
            var ledger = new HitLedger(SMALL_CAPACITY, RETENTION, FORCE_EVICT_AGE);
            ledger.TryRecord(1, TARGET, START_TIME);
            ledger.TryRecord(2, TARGET, START_TIME);

            Assert.AreEqual(EHitRecord.Recorded, ledger.TryRecord(3, TARGET, PAST_FORCE_AGE));
            Assert.AreEqual(SMALL_CAPACITY, ledger.ForcedEvictionCount);
            Assert.AreEqual(0, ledger.FullCount);
            Assert.IsFalse(ledger.Contains(1, TARGET));
            Assert.IsTrue(ledger.Contains(3, TARGET));
        }

        [Test]
        public void TryRecord_NotFull_DoesNotForceEvictBeforeRetention()
        {
            var ledger = new HitLedger(SMALL_CAPACITY, RETENTION, FORCE_EVICT_AGE);
            ledger.TryRecord(1, TARGET, START_TIME);

            ledger.TryRecord(2, TARGET, PAST_FORCE_AGE);

            Assert.IsTrue(ledger.Contains(1, TARGET));
            Assert.AreEqual(0, ledger.ForcedEvictionCount);
        }

        [Test]
        public void Prune_MixedRecordAges_KeepsRecordsInsideRetention()
        {
            var ledger = new HitLedger(SMALL_CAPACITY, RETENTION, FORCE_EVICT_AGE);
            ledger.TryRecord(1, TARGET, START_TIME);
            ledger.TryRecord(2, TARGET, HALF_RETENTION);

            ledger.Prune(RETENTION);

            Assert.IsFalse(ledger.Contains(1, TARGET));
            Assert.IsTrue(ledger.Contains(2, TARGET));
            Assert.AreEqual(1, ledger.Count);
        }

        [Test]
        public void TryRecord_NegativeTargetId_DoesNotCollideWithOtherAction()
        {
            var ledger = new HitLedger(SMALL_CAPACITY, RETENTION, FORCE_EVICT_AGE);
            ledger.TryRecord(1, NEGATIVE_ID, START_TIME);

            Assert.IsFalse(ledger.Contains(2, NEGATIVE_ID));
            Assert.IsFalse(ledger.Contains(0, NEGATIVE_ID));
        }
    }
}
