// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class DungeonEntryRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CHAOS_COUNT = 3;
        private const int BOSS_COUNT  = 4;

        private ProgressionCatalogSO _catalog = null;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<ProgressionCatalogSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
        }

        [Test]
        public void Build_DefaultCatalog_ListsChaosDungeonsBeforeBossMaps()
        {
            var entries = new List<DungeonEntryData>();

            DungeonEntryRules.Build(_catalog, entries);

            Assert.That(entries.Count, Is.EqualTo(CHAOS_COUNT + BOSS_COUNT));
            for (int i = 0; i < CHAOS_COUNT; ++i)
            {
                Assert.That(entries[i].Mode, Is.EqualTo(EPartyMode.Normal));
            }

            for (int i = CHAOS_COUNT; i < entries.Count; ++i)
            {
                Assert.That(entries[i].Mode, Is.EqualTo(EPartyMode.Boss));
            }

            Assert.That(entries[0].ContentId, Is.EqualTo("region_forest"));
            Assert.That(entries[CHAOS_COUNT].ContentId, Is.EqualTo("battle_mucheol_easy"));
        }

        [Test]
        public void Build_NoWaveRegion_IsNotListedAsChaosDungeon()
        {
            var entries = new List<DungeonEntryData>();

            DungeonEntryRules.Build(_catalog, entries);

            Assert.That(entries.Exists(entry => entry.ContentId == "region_wolbaek_barrier"), Is.False);
        }

        [Test]
        public void Build_TrialEdition_OnlyTrialContentIsAvailable()
        {
            var entries = new List<DungeonEntryData>();

            DungeonEntryRules.Build(_catalog, entries);

            Assert.That(entries.Find(entry => entry.ContentId == "region_forest").IsAvailable, Is.True);
            Assert.That(entries.Find(entry => entry.ContentId == "region_ruins").IsAvailable, Is.False);
            Assert.That(entries.Find(entry => entry.ContentId == "region_ruins").UnavailableReason, Is.Not.Empty);
            Assert.That(entries.Find(entry => entry.ContentId == "battle_wolbaek_easy").IsAvailable, Is.True);
            Assert.That(entries.Find(entry => entry.ContentId == "battle_mucheol_easy").IsAvailable, Is.False);
        }

        [Test]
        public void Build_FullEdition_EverythingIsAvailable()
        {
            var serialized = new SerializedObject(_catalog);
            serialized.FindProperty("_trialEdition").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var entries = new List<DungeonEntryData>();

            DungeonEntryRules.Build(_catalog, entries);

            Assert.That(entries.TrueForAll(entry => entry.IsAvailable), Is.True);
        }

        [Test]
        public void Build_NullCatalog_ClearsList()
        {
            var entries = new List<DungeonEntryData> { default };

            DungeonEntryRules.Build(null, entries);

            Assert.That(entries, Is.Empty);
        }

        [Test]
        public void ContentName_KnownIds_ReturnsDisplayNames()
        {
            Assert.That(DungeonEntryRules.ContentName(_catalog, EPartyMode.Normal, "region_forest"), Is.EqualTo("숲"));
            Assert.That(DungeonEntryRules.ContentName(_catalog, EPartyMode.Boss, "battle_wolbaek_hard"), Is.EqualTo("월백 (어려움)"));
            Assert.That(DungeonEntryRules.ContentName(_catalog, EPartyMode.Boss, "battle_mucheol_easy"), Is.EqualTo("무철 (쉬움)"));
        }

        [Test]
        public void ContentName_UnknownId_ReturnsIdAsIs()
        {
            Assert.That(DungeonEntryRules.ContentName(_catalog, EPartyMode.Normal, "region_unknown"), Is.EqualTo("region_unknown"));
            Assert.That(DungeonEntryRules.ContentName(_catalog, EPartyMode.Boss, "battle_unknown"), Is.EqualTo("battle_unknown"));
            Assert.That(DungeonEntryRules.ContentName(_catalog, EPartyMode.Normal, null), Is.Empty);
        }

        [Test]
        public void Build_Requirements_DescribeCatalogConditions()
        {
            var entries = new List<DungeonEntryData>();

            DungeonEntryRules.Build(_catalog, entries);

            Assert.That(entries.Find(entry => entry.ContentId == "region_forest").Requirement, Is.EqualTo("신시 안내 완료"));
            Assert.That(entries.Find(entry => entry.ContentId == "region_ruins").Requirement, Is.EqualTo("숲 카오스 던전 성공"));
            Assert.That(entries.Find(entry => entry.ContentId == "battle_mucheol_hard").Requirement, Is.EqualTo("무철 (쉬움) 처치"));
            Assert.That(entries.Find(entry => entry.ContentId == "battle_wolbaek_easy").Requirement,
                Is.EqualTo("월백 결계 개방, 무철 (어려움) 처치"));
        }

        [Test]
        public void Build_BossEntry_ShowsTicketCost()
        {
            var entries = new List<DungeonEntryData>();

            DungeonEntryRules.Build(_catalog, entries);

            Assert.That(entries.Find(entry => entry.ContentId == "battle_wolbaek_easy").Cost, Does.Contain("1"));
        }
    }
}
