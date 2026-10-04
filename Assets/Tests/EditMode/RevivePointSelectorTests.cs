// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class RevivePointSelectorTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  CLEARANCE   = 6f;
        private const float  JUST_INSIDE = 5.99f;
        private const double NOW         = 10.0;

        private FakeTargetQuery _targets;
        private FakeHazardQuery _hazards;

        /****************************************
        *              Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _targets = new FakeTargetQuery();
            _hazards = new FakeHazardQuery();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void IsSafe_EnemyExactlyAtClearance_IsSafe()
        {
            RevivePointSelector selector = CreateSelector();
            _targets.AddEnemy(new Vector3(CLEARANCE, 0f, 0f), true);

            Assert.IsTrue(selector.IsSafe(Vector3.zero, NOW));
        }

        [Test]
        public void IsSafe_EnemyJustInsideClearance_IsUnsafe()
        {
            RevivePointSelector selector = CreateSelector();
            _targets.AddEnemy(new Vector3(JUST_INSIDE, 0f, 0f), true);

            Assert.IsFalse(selector.IsSafe(Vector3.zero, NOW));
        }

        [Test]
        public void IsSafe_EnemyAboveWithinHorizontalClearance_IsUnsafe()
        {
            RevivePointSelector selector = CreateSelector();
            _targets.AddEnemy(new Vector3(1f, 20f, 0f), true);

            Assert.IsFalse(selector.IsSafe(Vector3.zero, NOW));
        }

        [Test]
        public void IsSafe_DeadEnemyNearby_IsSafe()
        {
            RevivePointSelector selector = CreateSelector();
            _targets.AddEnemy(new Vector3(1f, 0f, 0f), false);

            Assert.IsTrue(selector.IsSafe(Vector3.zero, NOW));
        }

        [Test]
        public void TrySelect_PicksNearestSafeToDeathPosition()
        {
            RevivePointSelector selector = CreateSelector();
            selector.AddCandidate(new Vector3(30f, 0f, 0f));
            selector.AddCandidate(new Vector3(10f, 0f, 0f));
            selector.AddCandidate(new Vector3(-20f, 0f, 0f));

            Assert.IsTrue(selector.TrySelect(Vector3.zero, NOW, out Vector3 point));
            Assert.AreEqual(new Vector3(10f, 0f, 0f), point);
        }

        [Test]
        public void TrySelect_NearestNearEnemy_SkipsToNextSafe()
        {
            RevivePointSelector selector = CreateSelector();
            selector.AddCandidate(new Vector3(30f, 0f, 0f));
            selector.AddCandidate(new Vector3(10f, 0f, 0f));
            _targets.AddEnemy(new Vector3(12f, 0f, 0f), true);

            Assert.IsTrue(selector.TrySelect(Vector3.zero, NOW, out Vector3 point));
            Assert.AreEqual(new Vector3(30f, 0f, 0f), point);
        }

        [Test]
        public void TrySelect_HazardousNearest_IsExcluded()
        {
            RevivePointSelector selector = CreateSelector();
            selector.AddCandidate(new Vector3(10f, 0f, 0f));
            selector.AddCandidate(new Vector3(30f, 0f, 0f));
            _hazards.Hazardous.Add(new Vector3(10f, 0f, 0f));

            Assert.IsTrue(selector.TrySelect(Vector3.zero, NOW, out Vector3 point));
            Assert.AreEqual(new Vector3(30f, 0f, 0f), point);
        }

        [Test]
        public void TrySelect_AllHazardousOrNearEnemy_ReturnsFalse()
        {
            RevivePointSelector selector = CreateSelector();
            selector.AddCandidate(new Vector3(10f, 0f, 0f));
            selector.AddCandidate(new Vector3(30f, 0f, 0f));
            _hazards.Hazardous.Add(new Vector3(10f, 0f, 0f));
            _targets.AddEnemy(new Vector3(31f, 0f, 0f), true);

            Assert.IsFalse(selector.TrySelect(Vector3.zero, NOW, out _));
        }

        [Test]
        public void TrySelect_NoCandidates_ReturnsFalse()
        {
            RevivePointSelector selector = CreateSelector();

            Assert.IsFalse(selector.TrySelect(Vector3.zero, NOW, out _));
        }

        [Test]
        public void TrySelect_EqualDistance_KeepsListOrder()
        {
            RevivePointSelector selector = CreateSelector();
            selector.AddCandidate(new Vector3(0f, 0f, 10f));
            selector.AddCandidate(new Vector3(10f, 0f, 0f));

            Assert.IsTrue(selector.TrySelect(Vector3.zero, NOW, out Vector3 point));
            Assert.AreEqual(new Vector3(0f, 0f, 10f), point);
        }

        [Test]
        public void ClearCandidates_RemovesAll()
        {
            RevivePointSelector selector = CreateSelector();
            selector.AddCandidate(Vector3.one);

            selector.ClearCandidates();

            Assert.AreEqual(0, selector.CandidateCount);
        }

        [Test]
        public void CombatReviveHazardQuery_HasNoHazardSourceYet()
        {
            Assert.IsFalse(new CombatReviveHazardQuery().IsHazardous(Vector3.zero, NOW));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private RevivePointSelector CreateSelector()
        {
            return new RevivePointSelector(_targets, _hazards, CLEARANCE);
        }

        private sealed class FakeHazardQuery : IReviveHazardQuery
        {
            public readonly List<Vector3> Hazardous = new List<Vector3>();

            public bool IsHazardous(Vector3 point, double serverTime) { return Hazardous.Contains(point); }
        }

        private sealed class FakeTarget : ICombatTarget
        {
            public int                   CombatId      { get; set; }
            public ECombatFaction        Faction       { get; set; }
            public EParticipantLifeState LifeState     { get; set; }
            public bool                  IsSummon      => false;
            public int                   OwnerCombatId => CombatIds.NONE;
            public Vector3               Position      { get; set; }
            public Vector3               Forward       => Vector3.forward;
            public float                 BodyRadius    => 0.5f;
        }

        // 실제 레지스트리와 같은 계약: 수평 거리 반경 이하(경계 포함), 살아 있는 같은 소속만 넣는다
        private sealed class FakeTargetQuery : ICombatTargetQuery
        {
            private readonly List<FakeTarget> _targets = new List<FakeTarget>();

            public void AddEnemy(Vector3 position, bool alive)
            {
                _targets.Add(new FakeTarget
                {
                    CombatId  = _targets.Count + 1,
                    Faction   = ECombatFaction.Enemy,
                    LifeState = alive ? EParticipantLifeState.Alive : EParticipantLifeState.Dead,
                    Position  = position
                });
            }

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                return 0;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < _targets.Count; ++i)
                {
                    FakeTarget target = _targets[i];
                    float deltaX = target.Position.x - center.x;
                    float deltaZ = target.Position.z - center.z;
                    if ((target.Faction == faction) && (target.LifeState == EParticipantLifeState.Alive)
                        && (((deltaX * deltaX) + (deltaZ * deltaZ)) <= (radius * radius)))
                    {
                        results.Add(target);
                    }
                }

                return results.Count;
            }

            public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
            {
                return GetLivingInRadius(center, radius, faction, includeSummons, new List<ICombatTarget>());
            }

            public bool TryGet(int combatId, out ICombatTarget target)
            {
                target = null;
                return false;
            }
        }
    }
}
