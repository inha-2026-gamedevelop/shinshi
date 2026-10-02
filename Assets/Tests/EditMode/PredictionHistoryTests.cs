// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public class PredictionHistoryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    CAPACITY  = 4;
        private const double INTERVAL  = 0.01;
        private const float  TOLERANCE = 0.0001f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TrySample_BetweenRecords_Interpolates()
        {
            var history = new PredictionHistory(CAPACITY, INTERVAL);
            history.Record(0.0, Vector3.zero);
            history.Record(1.0, new Vector3(2f, 0f, 0f));

            bool found = history.TrySample(0.25, out Vector3 position);

            Assert.IsTrue(found);
            Assert.AreEqual(0.5f, position.x, TOLERANCE);
        }

        [Test]
        public void TrySample_BeforeOldest_ReturnsFalse()
        {
            var history = new PredictionHistory(CAPACITY, INTERVAL);
            history.Record(1.0, Vector3.zero);

            Assert.IsFalse(history.TrySample(0.5, out _));
        }

        [Test]
        public void TrySample_AfterNewest_ReturnsNewest()
        {
            var history = new PredictionHistory(CAPACITY, INTERVAL);
            history.Record(0.0, Vector3.zero);
            history.Record(1.0, Vector3.one);

            history.TrySample(5.0, out Vector3 position);

            Assert.AreEqual(Vector3.one, position);
        }

        [Test]
        public void Record_FasterThanInterval_OverwritesLastAndStillAdvances()
        {
            var history = new PredictionHistory(CAPACITY, INTERVAL);
            history.Record(0.0, Vector3.zero);
            history.Record(0.001, Vector3.one);
            history.Record(0.002, Vector3.one);
            int countBeforeInterval = history.Count;

            history.Record(0.011, Vector3.one);

            Assert.AreEqual(2, countBeforeInterval);
            Assert.AreEqual(3, history.Count);
        }

        [Test]
        public void Record_OverCapacity_DropsOldest()
        {
            var history = new PredictionHistory(CAPACITY, INTERVAL);
            for (int i = 0; i <= CAPACITY; ++i)
            {
                history.Record(i, new Vector3(i, 0f, 0f));
            }

            Assert.AreEqual(CAPACITY, history.Count);
            Assert.IsFalse(history.TrySample(0.5, out _));
            Assert.IsTrue(history.TrySample(1.0, out Vector3 oldest));
            Assert.AreEqual(1f, oldest.x, TOLERANCE);
        }

        [Test]
        public void Record_ClockGoesBack_ClearsHistory()
        {
            var history = new PredictionHistory(CAPACITY, INTERVAL);
            history.Record(1.0, Vector3.zero);
            history.Record(2.0, Vector3.one);

            history.Record(0.5, Vector3.one);

            Assert.AreEqual(1, history.Count);
        }

        [Test]
        public void Shift_PopulatedHistory_MovesEveryRecord()
        {
            var history = new PredictionHistory(CAPACITY, INTERVAL);
            history.Record(0.0, Vector3.zero);
            history.Record(1.0, Vector3.right);

            history.Shift(Vector3.forward);

            history.TrySample(0.0, out Vector3 first);
            history.TrySample(1.0, out Vector3 second);
            Assert.AreEqual(Vector3.forward, first);
            Assert.AreEqual(Vector3.right + Vector3.forward, second);
        }
    }
}
