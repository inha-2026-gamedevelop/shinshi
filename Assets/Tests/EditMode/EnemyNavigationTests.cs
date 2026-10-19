// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public sealed class EnemyNavigationTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int CLOSE_CAP      = 3;
        private const int PROJECTILE_CAP = 2;

        /****************************************
        *                Types
        ****************************************/

        private sealed class FakeNavigation : IEnemyNavigation
        {
            public readonly Dictionary<Vector3, float> Lengths = new Dictionary<Vector3, float>();

            public bool TryMeasurePath(Vector3 from, Vector3 to, out float length)
            {
                return Lengths.TryGetValue(to, out length);
            }

            public bool TryGetSteerPoint(Vector3 from, Vector3 to, out Vector3 point)
            {
                point = to;
                return Lengths.ContainsKey(to);
            }

            public bool IsWalkable(Vector3 point)
            {
                return true;
            }
        }

        private sealed class PlayerStub : IDamageable
        {
            public int                   CombatId        { get; set; }
            public ECombatFaction        Faction         => ECombatFaction.Player;
            public EParticipantLifeState LifeState       { get; set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; set; }
            public Vector3               Forward         => Vector3.forward;
            public float                 BodyRadius      => 0.5f;
            public float                 Health          => 100f;
            public float                 MaxHealth       => 100f;
            public float                 Defense         => 0f;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;

            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
            {
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private CombatTargetRegistry _registry;
        private FakeNavigation       _navigation;
        private PlayerStub           _a;
        private PlayerStub           _b;

        /****************************************
        *              Test Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _registry   = new CombatTargetRegistry(64);
            _navigation = new FakeNavigation();
            _a          = AddPlayer(new Vector3(10f, 0f, 0f));
            _b          = AddPlayer(new Vector3(0f, 0f, 12f));
        }

        /****************************************
        *            Telegraph Limiter
        ****************************************/

        [Test]
        public void Limiter_CloseCap_DeniesFourthAndReadmitsAfterRelease()
        {
            var limiter = new EnemyTelegraphLimiter(CLOSE_CAP, PROJECTILE_CAP);

            Assert.That(limiter.TryAcquire(1, 100, false), Is.True);
            Assert.That(limiter.TryAcquire(2, 100, false), Is.True);
            Assert.That(limiter.TryAcquire(3, 100, false), Is.True);
            Assert.That(limiter.TryAcquire(4, 100, false), Is.False);
            Assert.That(limiter.Count(100, false), Is.EqualTo(CLOSE_CAP));

            limiter.Release(2);
            Assert.That(limiter.TryAcquire(4, 100, false), Is.True);
        }

        [Test]
        public void Limiter_ProjectileCap_DeniesThirdForSameTarget()
        {
            var limiter = new EnemyTelegraphLimiter(CLOSE_CAP, PROJECTILE_CAP);

            Assert.That(limiter.TryAcquire(1, 100, true), Is.True);
            Assert.That(limiter.TryAcquire(2, 100, true), Is.True);
            Assert.That(limiter.TryAcquire(3, 100, true), Is.False);
            Assert.That(limiter.TryAcquire(3, 200, true), Is.True);
        }

        [Test]
        public void Limiter_CloseAndProjectileCaps_AreIndependent()
        {
            var limiter = new EnemyTelegraphLimiter(CLOSE_CAP, PROJECTILE_CAP);

            for (int i = 1; i <= CLOSE_CAP; ++i)
            {
                Assert.That(limiter.TryAcquire(i, 100, false), Is.True);
            }

            Assert.That(limiter.TryAcquire(10, 100, true), Is.True);
            Assert.That(limiter.TryAcquire(11, 100, true), Is.True);
            Assert.That(limiter.Count(100, false), Is.EqualTo(CLOSE_CAP));
            Assert.That(limiter.Count(100, true), Is.EqualTo(PROJECTILE_CAP));
        }

        [Test]
        public void Limiter_ReacquireWithOtherTarget_FreesOldHold()
        {
            var limiter = new EnemyTelegraphLimiter(CLOSE_CAP, PROJECTILE_CAP);

            Assert.That(limiter.TryAcquire(1, 100, false), Is.True);
            Assert.That(limiter.TryAcquire(1, 200, false), Is.True);

            Assert.That(limiter.Count(100, false), Is.Zero);
            Assert.That(limiter.Count(200, false), Is.EqualTo(1));
        }

        /****************************************
        *              Target Selector
        ****************************************/

        [Test]
        public void Selector_UnreachableNearestTarget_IsSkipped()
        {
            _navigation.Lengths[_b.Position] = 30f;
            var selector = new EnemyTargetSelector(_registry, _navigation);

            ICombatTarget target = selector.Resolve(Vector3.zero, 0.0);

            Assert.That(target, Is.Not.Null);
            Assert.That(target.CombatId, Is.EqualTo(_b.CombatId));
            Assert.That(selector.TargetId, Is.EqualTo(_b.CombatId));
        }

        [Test]
        public void Selector_PicksShortestPathNotStraightDistance()
        {
            _navigation.Lengths[_a.Position] = 40f;
            _navigation.Lengths[_b.Position] = 15f;
            var selector = new EnemyTargetSelector(_registry, _navigation);

            Assert.That(selector.Resolve(Vector3.zero, 0.0).CombatId, Is.EqualTo(_b.CombatId));
        }

        [Test]
        public void Selector_HoldBlocksSwitchBeforeTwoSeconds_ThenAllowsIt()
        {
            _navigation.Lengths[_a.Position] = 10f;
            _navigation.Lengths[_b.Position] = 12f;
            var selector = new EnemyTargetSelector(_registry, _navigation);
            Assert.That(selector.Resolve(Vector3.zero, 0.0).CombatId, Is.EqualTo(_a.CombatId));

            _navigation.Lengths[_b.Position] = 6f;
            Assert.That(selector.Resolve(Vector3.zero, 0.5).CombatId, Is.EqualTo(_a.CombatId));
            Assert.That(selector.Resolve(Vector3.zero, 1.5).CombatId, Is.EqualTo(_a.CombatId));
            Assert.That(selector.Resolve(Vector3.zero, EnemyTargetSelector.HOLD_SECONDS).CombatId, Is.EqualTo(_b.CombatId));
        }

        [Test]
        public void Selector_MarginBlocksSwitchWhenNewPathIsNotThreeMetersShorter()
        {
            _navigation.Lengths[_a.Position] = 10f;
            _navigation.Lengths[_b.Position] = 12f;
            var selector = new EnemyTargetSelector(_registry, _navigation);
            selector.Resolve(Vector3.zero, 0.0);

            _navigation.Lengths[_b.Position] = 8f;
            Assert.That(selector.Resolve(Vector3.zero, 2.0).CombatId, Is.EqualTo(_a.CombatId));
            Assert.That(selector.Resolve(Vector3.zero, 10.0).CombatId, Is.EqualTo(_a.CombatId));

            _navigation.Lengths[_b.Position] = 7f;
            Assert.That(selector.Resolve(Vector3.zero, 11.0).CombatId, Is.EqualTo(_b.CombatId));
        }

        [Test]
        public void Selector_UnreachableCurrentTarget_IsDroppedDespiteHold()
        {
            _navigation.Lengths[_a.Position] = 10f;
            _navigation.Lengths[_b.Position] = 12f;
            var selector = new EnemyTargetSelector(_registry, _navigation);
            selector.Resolve(Vector3.zero, 0.0);

            _navigation.Lengths.Remove(_a.Position);

            Assert.That(selector.Resolve(Vector3.zero, 0.5).CombatId, Is.EqualTo(_b.CombatId));
        }

        [Test]
        public void Selector_DeadCurrentTarget_IsDroppedDespiteHold()
        {
            _navigation.Lengths[_a.Position] = 10f;
            _navigation.Lengths[_b.Position] = 12f;
            var selector = new EnemyTargetSelector(_registry, _navigation);
            selector.Resolve(Vector3.zero, 0.0);

            _a.LifeState = EParticipantLifeState.Dead;

            Assert.That(selector.Resolve(Vector3.zero, 0.5).CombatId, Is.EqualTo(_b.CombatId));
        }

        [Test]
        public void Selector_RequestReevaluate_IgnoresHoldAndTimer()
        {
            _navigation.Lengths[_a.Position] = 10f;
            _navigation.Lengths[_b.Position] = 12f;
            var selector = new EnemyTargetSelector(_registry, _navigation);
            selector.Resolve(Vector3.zero, 0.0);

            _navigation.Lengths[_b.Position] = 6f;
            Assert.That(selector.Resolve(Vector3.zero, 0.1).CombatId, Is.EqualTo(_a.CombatId));

            selector.RequestReevaluate();
            Assert.That(selector.Resolve(Vector3.zero, 0.2).CombatId, Is.EqualTo(_b.CombatId));
        }

        [Test]
        public void Selector_RequestReevaluate_StillRespectsMargin()
        {
            _navigation.Lengths[_a.Position] = 10f;
            _navigation.Lengths[_b.Position] = 12f;
            var selector = new EnemyTargetSelector(_registry, _navigation);
            selector.Resolve(Vector3.zero, 0.0);

            _navigation.Lengths[_b.Position] = 8f;
            selector.RequestReevaluate();

            Assert.That(selector.Resolve(Vector3.zero, 0.2).CombatId, Is.EqualTo(_a.CombatId));
        }

        [Test]
        public void Selector_NoReachableTarget_ReturnsNullAndClearsTargetId()
        {
            var selector = new EnemyTargetSelector(_registry, _navigation);

            Assert.That(selector.Resolve(Vector3.zero, 0.0), Is.Null);
            Assert.That(selector.TargetId, Is.EqualTo(CombatIds.NONE));
        }

        [Test]
        public void Selector_EqualPathLength_PicksLowerCombatId()
        {
            _navigation.Lengths[_a.Position] = 9f;
            _navigation.Lengths[_b.Position] = 9f;
            var selector = new EnemyTargetSelector(_registry, _navigation);

            Assert.That(selector.Resolve(Vector3.zero, 0.0).CombatId, Is.EqualTo(Mathf.Min(_a.CombatId, _b.CombatId)));
        }

        [Test]
        public void Selector_WithoutNavigation_KeepsStraightLineBehavior()
        {
            var selector = new EnemyTargetSelector(_registry);

            Assert.That(selector.Resolve(Vector3.zero, 0.0).CombatId, Is.EqualTo(_a.CombatId));

            _b.Position = new Vector3(1f, 0f, 0f);
            Assert.That(selector.Resolve(Vector3.zero, 5.0).CombatId, Is.EqualTo(_a.CombatId));
            Assert.That(selector.Resolve(Vector3.zero).CombatId, Is.EqualTo(_a.CombatId));
        }

        /****************************************
        *                Methods
        ****************************************/

        private PlayerStub AddPlayer(Vector3 position)
        {
            var player = new PlayerStub { CombatId = _registry.IssueCombatId(), Position = position };
            Assert.That(_registry.Register(player), Is.True);
            return player;
        }
    }
}
