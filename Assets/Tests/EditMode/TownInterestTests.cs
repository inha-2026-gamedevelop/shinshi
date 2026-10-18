// System
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class TownInterestTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float RADIUS = 10f;
        private const float CELL   = 10f;

        private sealed class FakeParty : ITownPartyPeers
        {
            public readonly Dictionary<int, int[]> Peers = new Dictionary<int, int[]>();

            public bool TryGetPeers(int id, List<int> into)
            {
                if (!Peers.TryGetValue(id, out int[] peers))
                {
                    return false;
                }

                into.AddRange(peers);
                return true;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private static List<int> ObserversOf(TownVisibilitySolver solver, int id)
        {
            var list = new List<int>();
            solver.GetObserversOf(id, list);
            return list;
        }

        [Test]
        public void SeesOnlyInsideRadiusAndSelfAlways()
        {
            var solver = new TownVisibilitySolver(RADIUS, CELL, 30, 8);
            solver.Add(1, 0f, 0f);
            solver.Add(2, 9f, 0f);
            solver.Add(3, 11f, 0f);
            solver.Compute(null);

            Assert.IsTrue(solver.IsVisible(1, 1));
            Assert.IsTrue(solver.IsVisible(1, 2));
            Assert.IsFalse(solver.IsVisible(1, 3));
        }

        [Test]
        public void FindsNeighborAcrossCellBoundaryAndNegativeCoordinates()
        {
            var solver = new TownVisibilitySolver(RADIUS, CELL, 30, 8);
            solver.Add(1, -0.5f, -0.5f);
            solver.Add(2, 4f, 4f);
            solver.Add(3, -9f, 0f);
            solver.Compute(null);

            Assert.IsTrue(solver.IsVisible(1, 2));
            Assert.IsTrue(solver.IsVisible(1, 3));
        }

        [Test]
        public void KeepsNearestWhenOverLimit()
        {
            var solver = new TownVisibilitySolver(RADIUS, CELL, 3, 8);
            solver.Add(1, 0f, 0f);
            solver.Add(2, 5f, 0f);
            solver.Add(3, 1f, 0f);
            solver.Add(4, 3f, 0f);
            solver.Compute(null);

            // 본인 포함 3명, 가까운 순으로 3번(1m)과 4번(3m)만 남는다
            Assert.IsTrue(solver.IsVisible(1, 3));
            Assert.IsTrue(solver.IsVisible(1, 4));
            Assert.IsFalse(solver.IsVisible(1, 2));
        }

        [Test]
        public void PartyMembersAreAlwaysVisibleEvenFarAndOverLimit()
        {
            var party = new FakeParty();
            party.Peers[1] = new[] { 1, 9 };
            party.Peers[9] = new[] { 1, 9 };

            var solver = new TownVisibilitySolver(RADIUS, CELL, 2, 16);
            solver.Add(1, 0f, 0f);
            solver.Add(2, 1f, 0f);
            solver.Add(3, 2f, 0f);
            solver.Add(9, 500f, 500f);
            solver.Compute(party);

            // 상한 2는 본인과 파티원이 채운다. 가까운 낯선 사람은 자리가 없다
            Assert.IsTrue(solver.IsVisible(1, 9));
            Assert.IsTrue(solver.IsVisible(9, 1));
            Assert.IsFalse(solver.IsVisible(1, 2));
            Assert.IsFalse(solver.IsVisible(1, 3));
        }

        [Test]
        public void DuplicatePartyPeersAndRepeatedComputeKeepVisibleSetUnique()
        {
            var party = new FakeParty();
            party.Peers[1] = new[] { 1, 2, 2, 3, 2 };

            var solver = new TownVisibilitySolver(RADIUS, CELL, 30, 8);
            solver.Add(1, 0f, 0f);
            solver.Add(2, 1f, 0f);
            solver.Add(3, 500f, 500f);

            // 표시 버퍼를 재사용하므로 두 번째 계산에서도 결과가 같아야 한다
            for (int round = 0; round < 2; ++round)
            {
                solver.Compute(party);

                var observers = ObserversOf(solver, 2);
                Assert.That(observers.FindAll(id => id == 1).Count, Is.EqualTo(1));
                Assert.IsTrue(solver.IsVisible(1, 3));
                Assert.That(ObserversOf(solver, 3).FindAll(id => id == 1).Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void UnknownPartyPeerIsIgnored()
        {
            var party = new FakeParty();
            party.Peers[1] = new[] { 77 };

            var solver = new TownVisibilitySolver(RADIUS, CELL, 30, 8);
            solver.Add(1, 0f, 0f);
            Assert.DoesNotThrow(() => solver.Compute(party));
            Assert.IsTrue(solver.IsVisible(1, 1));
        }

        [Test]
        public void VisibilityCanBeAsymmetricAndObserversAreTheInverse()
        {
            // 2번 주변이 붐벼 2번은 멀리 있는 1번을 못 보지만, 1번은 외톨이라 2번을 본다
            var solver = new TownVisibilitySolver(RADIUS, CELL, 2, 8);
            solver.Add(1, 0f, 0f);
            solver.Add(2, 8f, 0f);
            solver.Add(3, 9f, 0f);
            solver.Add(4, 10f, 0f);
            solver.Compute(null);

            Assert.IsTrue(solver.IsVisible(1, 2));
            Assert.IsFalse(solver.IsVisible(2, 1));

            List<int> observersOfTwo = ObserversOf(solver, 2);
            Assert.Contains(1, observersOfTwo);
            Assert.Contains(2, observersOfTwo);
            CollectionAssert.DoesNotContain(ObserversOf(solver, 1), 2);
        }

        [Test]
        public void ObserversMatchIsVisibleForEveryPair()
        {
            var solver = new TownVisibilitySolver(RADIUS, CELL, 4, 64);
            var random = new System.Random(12345);
            for (int i = 0; i < 40; ++i)
            {
                solver.Add(i, (float)(random.NextDouble() * 30.0), (float)(random.NextDouble() * 30.0));
            }

            solver.Compute(null);

            for (int target = 0; target < 40; ++target)
            {
                List<int> observers = ObserversOf(solver, target);
                for (int viewer = 0; viewer < 40; ++viewer)
                {
                    Assert.AreEqual(solver.IsVisible(viewer, target), observers.Contains(viewer), $"viewer {viewer}, target {target}");
                }
            }
        }

        [Test]
        public void NeverExceedsLimitForNonParty()
        {
            var solver = new TownVisibilitySolver(RADIUS, CELL, 5, 128);
            for (int i = 0; i < 100; ++i)
            {
                solver.Add(i, (i % 10) * 0.1f, (i / 10) * 0.1f);
            }

            solver.Compute(null);

            for (int viewer = 0; viewer < 100; ++viewer)
            {
                int seen = 0;
                for (int target = 0; target < 100; ++target)
                {
                    if (solver.IsVisible(viewer, target))
                    {
                        ++seen;
                    }
                }

                Assert.AreEqual(5, seen);
            }
        }

        [Test]
        public void ViewersNearReturnsOnlyThoseInsideRadius()
        {
            var solver = new TownVisibilitySolver(RADIUS, CELL, 30, 8);
            solver.Add(1, 0f, 0f);
            solver.Add(2, 6f, 0f);
            solver.Add(3, 20f, 0f);
            solver.Compute(null);

            var near = new List<int>();
            solver.GetViewersNear(5f, 0f, near);

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, near);
        }

        [Test]
        public void ClearAndReuseGivesFreshResult()
        {
            var solver = new TownVisibilitySolver(RADIUS, CELL, 30, 8);
            solver.Add(1, 0f, 0f);
            solver.Add(2, 1f, 0f);
            solver.Compute(null);
            Assert.IsTrue(solver.IsVisible(1, 2));

            solver.Clear();
            solver.Add(1, 0f, 0f);
            solver.Add(2, 50f, 0f);
            solver.Compute(null);

            Assert.IsFalse(solver.IsVisible(1, 2));
            Assert.AreEqual(2, solver.Count);
            Assert.IsFalse(solver.IsVisible(1, 99));
        }

        [Test]
        public void DuplicateIdIsIgnoredAndCellSmallerThanRadiusIsRejected()
        {
            var solver = new TownVisibilitySolver(RADIUS, CELL, 30, 8);
            solver.Add(1, 0f, 0f);
            solver.Add(1, 99f, 99f);
            Assert.AreEqual(1, solver.Count);

            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TownVisibilitySolver(RADIUS, RADIUS - 1f, 30, 8));
        }
    }
}
