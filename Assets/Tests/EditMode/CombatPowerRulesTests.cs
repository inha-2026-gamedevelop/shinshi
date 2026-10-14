// System
using System;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public class CombatPowerRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float ATTACK     = 644.32f;
        private const float MAX_HEALTH = 3541f;
        private const float DEFENSE    = 326.8f;
        private const float CRIT       = 0.07f;
        private const float ENERGY     = 100f;
        private const float MOVE_SPEED = 5f;
        private const float LIFE_STEAL = 0.015f;
        private const float TOLERANCE  = 0.01f;

        private PlayerConfigSO _config;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PlayerConfigSO>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Offense_MatchesExpectedDamagePerCoefficient()
        {
            float expected = ATTACK * (1f + (CRIT * (_config.CritDamageMultiplier - 1f)));

            Assert.AreEqual(expected, CombatPowerRules.Offense(Stats(), _config), TOLERANCE);
        }

        [Test]
        public void Offense_CritAboveCap_ClampsToCap()
        {
            float expected = ATTACK * (1f + (_config.CritChanceCap * (_config.CritDamageMultiplier - 1f)));

            Assert.AreEqual(expected, CombatPowerRules.Offense(Stats(crit: 1f), _config), TOLERANCE);
        }

        [Test]
        public void Survival_UsesPlayerDefenseConstantAndLifeSteal()
        {
            float reduction = DEFENSE / (DEFENSE + _config.PlayerDefenseReductionConstant);
            float expected  = (MAX_HEALTH / (1f - reduction)) * (1f + (_config.CombatPowerLifeStealFactor * LIFE_STEAL));

            Assert.AreEqual(expected, CombatPowerRules.Survival(Stats(), _config), 1f);
        }

        [Test]
        public void Survival_HugeDefense_StopsAtReductionCap()
        {
            float atCap   = CombatPowerRules.Survival(Stats(defense: 1000000f), _config);
            float beyond  = CombatPowerRules.Survival(Stats(defense: 2000000f), _config);

            Assert.AreEqual(atCap, beyond, TOLERANCE);
        }

        [Test]
        public void Compute_MatchesWeightedGeometricMean()
        {
            CharacterStats stats = Stats();
            double weight   = _config.CombatPowerOffenseWeight;
            double expected = _config.CombatPowerScale * Math.Pow(CombatPowerRules.Offense(in stats, _config), weight)
                * Math.Pow(CombatPowerRules.Survival(in stats, _config), 1.0 - weight);

            Assert.AreEqual((int)Math.Floor(expected), CombatPowerRules.Compute(in stats, _config));
        }

        [Test]
        public void Compute_EachStatIncrease_RaisesPower()
        {
            int baseline = CombatPowerRules.Compute(Stats(), _config);

            Assert.Greater(CombatPowerRules.Compute(Stats(attack: ATTACK * 1.1f), _config), baseline);
            Assert.Greater(CombatPowerRules.Compute(Stats(maxHealth: MAX_HEALTH * 1.1f), _config), baseline);
            Assert.Greater(CombatPowerRules.Compute(Stats(defense: DEFENSE * 1.1f), _config), baseline);
            Assert.Greater(CombatPowerRules.Compute(Stats(crit: CRIT + 0.05f), _config), baseline);
            Assert.Greater(CombatPowerRules.Compute(Stats(lifeSteal: LIFE_STEAL + 0.01f), _config), baseline);
        }

        [Test]
        public void Compute_EnergyAndMoveSpeed_DoNotChangePower()
        {
            var changed = new CharacterStats(ATTACK, MAX_HEALTH, DEFENSE, CRIT, ENERGY * 2f, MOVE_SPEED * 2f, LIFE_STEAL);

            Assert.AreEqual(CombatPowerRules.Compute(Stats(), _config), CombatPowerRules.Compute(in changed, _config));
        }

        [Test]
        public void Compute_InvalidInput_ReturnsZero()
        {
            Assert.AreEqual(0, CombatPowerRules.Compute(Stats(), null));
            Assert.AreEqual(0, CombatPowerRules.Compute(default, _config));
            Assert.AreEqual(0, CombatPowerRules.Compute(Stats(attack: float.NaN), _config));
            Assert.AreEqual(0, CombatPowerRules.Compute(Stats(maxHealth: 0f), _config));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static CharacterStats Stats(float attack = ATTACK, float maxHealth = MAX_HEALTH, float defense = DEFENSE,
            float crit = CRIT, float lifeSteal = LIFE_STEAL)
        {
            return new CharacterStats(attack, maxHealth, defense, crit, ENERGY, MOVE_SPEED, lifeSteal);
        }
    }
}
