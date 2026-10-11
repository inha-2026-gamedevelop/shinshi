// System
using System.Reflection;

// Unity
using UnityEngine;

// Third Party
using Mirror;
using NUnit.Framework;

// Project
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public sealed class PlayerExperienceGrowthTests
    {
        private GameObject _object;
        private PlayerConfigSO _config;
        private CharacterLevelConfigSO _levels;
        private PlayerServer _player;
        private PlayerCombatState _combat;
        private CharacterLevelSettings _settings;
        private bool _serverActive;

        [SetUp]
        public void SetUp()
        {
            _serverActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty("active").SetValue(null, true);
            _config = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _levels = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            _settings = _levels.ToSettings();
            _object = new GameObject("ExperienceGrowthTest", typeof(NetworkIdentity));
            PlayerInputState input = _object.AddComponent<PlayerInputState>();
            _combat = _object.GetComponent<PlayerCombatState>();
            NetworkIdentity identity = _object.GetComponent<NetworkIdentity>();
            PropertyInfo property = typeof(NetworkBehaviour).GetProperty("netIdentity", BindingFlags.Instance | BindingFlags.Public);
            property.SetValue(input, identity);
            property.SetValue(_combat, identity);
            CharacterStats stats = CharacterStatsRules.Base(_config, _settings, 2850);
            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY,
                Constants.Combat.HIT_LEDGER_RETENTION_SECONDS, Constants.Combat.LONGEST_ACTION_SECONDS);
            var resolver = new DamageResolver(registry, ledger, _config.DefenseReductionConstant, _config.DamageReductionCap);
            _player = new PlayerServer(input, _combat, EPlayerClass.Warrior, 1, 1, 1, _config, in stats,
                resolver, registry, new CombatLineOfSight(0, 1f), 0);
            _combat.SetSkillResources(23, stats.MaxEnergy, 17);
        }

        [TearDown]
        public void TearDown()
        {
            _player?.Shutdown();
            Object.DestroyImmediate(_object);
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_levels);
            typeof(NetworkServer).GetProperty("active").SetValue(null, _serverActive);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ApplyGrowth_LevelIncrease_PreservesCurrentHealthEnergyAndDeath(bool dead)
        {
            _player.ApplyDamage(dead ? _player.MaxHealth : 37, dead, 4);
            float health = _player.Health;
            float energy = _combat.Energy;
            EParticipantLifeState life = _player.LifeState;
            double deathTime = _player.DeathTime;
            CharacterStats grown = CharacterStatsRules.Base(_config, _settings,
                CharacterLevelRules.RequiredExperience(_settings, 8));
            Assert.Greater(grown.Attack, _player.AttackPower);
            Assert.Greater(grown.MaxHealth, _player.MaxHealth);

            _player.ApplyGrowth(in grown);

            Assert.AreEqual(grown.Attack, _player.AttackPower);
            Assert.AreEqual(grown.MaxHealth, _player.MaxHealth);
            Assert.AreEqual(health, _player.Health);
            Assert.AreEqual(health, _combat.Health);
            Assert.AreEqual(energy, _combat.Energy);
            Assert.AreEqual(life, _player.LifeState);
            Assert.AreEqual(deathTime, _player.DeathTime);
        }
    }
}
