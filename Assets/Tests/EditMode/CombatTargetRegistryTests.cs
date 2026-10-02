// System
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class CombatTargetRegistryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float HEALTH        = 100f;
        private const float DEFENSE       = 0f;
        private const float RADIUS        = 5f;
        private const float INSIDE        = 4f;
        private const float OUTSIDE       = 6f;
        private const int   TINY_CAPACITY = 1;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void GetLivingInRadius_MixedTargets_FiltersFactionLifeAndDistance()
        {
            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var near     = Create(registry, ECombatFaction.Enemy, new Vector3(INSIDE, 0f, 0f));
            var far      = Create(registry, ECombatFaction.Enemy, new Vector3(OUTSIDE, 0f, 0f));
            var dead     = Create(registry, ECombatFaction.Enemy, Vector3.zero);
            var player   = Create(registry, ECombatFaction.Player, Vector3.zero);
            dead.LifeState = EParticipantLifeState.Dead;

            var results = new List<ICombatTarget>();
            int count   = registry.GetLivingInRadius(Vector3.zero, RADIUS, ECombatFaction.Enemy, true, results);

            Assert.AreEqual(1, count);
            Assert.AreSame(near, results[0]);
            Assert.AreEqual(1, registry.CountLivingInRadius(Vector3.zero, RADIUS, ECombatFaction.Enemy, true));
            Assert.AreNotSame(far, results[0]);
            Assert.AreNotSame(player, results[0]);
        }

        [Test]
        public void GetLivingInRadius_SummonsExcluded_ExcludesSummons()
        {
            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var summon   = Create(registry, ECombatFaction.Player, Vector3.zero);
            summon.IsSummon = true;

            var results = new List<ICombatTarget>();

            Assert.AreEqual(0, registry.GetLivingInRadius(Vector3.zero, RADIUS, ECombatFaction.Player, false, results));
            Assert.AreEqual(1, registry.GetLivingInRadius(Vector3.zero, RADIUS, ECombatFaction.Player, true, results));
            Assert.AreEqual(0, registry.GetLivingPlayers(results));
        }

        [Test]
        public void Register_OverCapacity_RejectsWithError()
        {
            var registry = new CombatTargetRegistry(TINY_CAPACITY);
            Create(registry, ECombatFaction.Enemy, Vector3.zero);

            LogAssert.Expect(LogType.Error, new Regex("상한"));
            bool registered = registry.Register(new FakeDamageable(registry.IssueCombatId(), ECombatFaction.Enemy, HEALTH, DEFENSE));

            Assert.IsFalse(registered);
            Assert.AreEqual(TINY_CAPACITY, registry.Count);
        }

        [Test]
        public void Unregister_RegisteredTarget_RemovesFromLookupAndQueries()
        {
            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var enemy    = Create(registry, ECombatFaction.Enemy, Vector3.zero);

            Assert.IsTrue(registry.Unregister(enemy.CombatId));
            Assert.IsFalse(registry.TryGet(enemy.CombatId, out _));
            Assert.AreEqual(0, registry.CountLivingInRadius(Vector3.zero, RADIUS, ECombatFaction.Enemy, true));
        }

        [Test]
        public void IssueCombatId_NewRegistry_NeverReturnsNone()
        {
            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);

            Assert.AreNotEqual(CombatIds.NONE, registry.IssueCombatId());
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static FakeDamageable Create(CombatTargetRegistry registry, ECombatFaction faction, Vector3 position)
        {
            var target = new FakeDamageable(registry.IssueCombatId(), faction, HEALTH, DEFENSE);
            target.Position = position;
            registry.Register(target);
            return target;
        }
    }
}
