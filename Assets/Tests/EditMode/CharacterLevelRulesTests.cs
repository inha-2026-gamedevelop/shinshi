// System
using System;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public sealed class CharacterLevelRulesTests
    {

        /****************************************
        *                Fields
        ****************************************/
        private CharacterLevelConfigSO _config;
        private CharacterLevelSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            _settings = _config.ToSettings();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
        }

        [TestCase(1, 0)]
        [TestCase(3, 875)]
        [TestCase(5, 2850)]
        [TestCase(10, 12600)]
        [TestCase(20, 52725)]
        [TestCase(30, 120350)]
        public void RequiredExperience_Level_ReturnsDesignThreshold(int level, long total)
        {
            Assert.That(CharacterLevelRules.RequiredExperience(_settings, level), Is.EqualTo(total));
            Assert.That(CharacterLevelRules.Level(_settings, total), Is.EqualTo(level));
            if (level > 1)
            {
                Assert.That(CharacterLevelRules.Level(_settings, total - 1), Is.EqualTo(level - 1));
            }
        }

        [Test]
        public void Add_OverflowingGrant_ClampsWithoutOverflow()
        {
            Assert.That(CharacterLevelRules.Add(_settings, 2850, long.MaxValue), Is.EqualTo(120350));
            Assert.That(CharacterLevelRules.Add(_settings, 120350, 5), Is.EqualTo(120350));
            Assert.That(CharacterLevelRules.Level(_settings, long.MaxValue), Is.EqualTo(30));
        }

        [Test]
        public void Add_MultipleThresholds_ReturnsFinalLevel()
        {
            long total = CharacterLevelRules.Add(_settings, 0, 2850);
            Assert.That(CharacterLevelRules.Level(_settings, total), Is.EqualTo(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => CharacterLevelRules.Add(_settings, 0, -1));
        }

        [TestCase("region_forest", 25, 30)]
        [TestCase("region_ruins", 30, 40)]
        [TestCase("region_altar", 35, 50)]
        public void GetGrant_Region_UsesSeparateEnemyAndWaveAmounts(string region, long elite, long wave)
        {
            Assert.That(_settings.GetGrant(region, CharacterExperienceSource.NORMAL_ENEMY), Is.EqualTo(5));
            Assert.That(_settings.GetGrant(region, CharacterExperienceSource.ELITE_ENEMY), Is.EqualTo(elite));
            Assert.That(_settings.GetGrant(region, CharacterExperienceSource.WAVE_CLEARED), Is.EqualTo(wave));
            Assert.That(_settings.GetGrant("unknown", CharacterExperienceSource.WAVE_CLEARED), Is.Zero);
        }

        [Test]
        public void IsValid_DuplicateRegionOrOverflow_ReturnsFalse()
        {
            Assert.That(_settings.IsValid(), Is.True);
            _settings.Regions[1].RegionId = _settings.Regions[0].RegionId;
            Assert.That(_settings.IsValid(), Is.False);
            _settings = _config.ToSettings();
            _settings.FirstLevelCost = long.MaxValue;
            Assert.That(_settings.IsValid(), Is.False);
        }
    }
}
