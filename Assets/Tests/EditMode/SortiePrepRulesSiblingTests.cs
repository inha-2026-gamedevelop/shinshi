// System
using System.Reflection;

// Unity
using UnityEngine;

// 테스트
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class SortiePrepRulesSiblingTests
    {
        private ProgressionCatalogSO _catalog;

        [TearDown]
        public void TearDown()
        {
            if (_catalog != null)
            {
                Object.DestroyImmediate(_catalog);
            }
        }

        [Test]
        public void TryFindSibling_TrialHardNotAvailable_ReturnsFalse()
        {
            Build(true, new ProgressionCatalogSO.BossBattleDefinition("muc_easy", "muc", EBossDifficulty.Easy, 1, true, true, default),
                new ProgressionCatalogSO.BossBattleDefinition("muc_hard", "muc", EBossDifficulty.Hard, 1, false, false, default));

            Assert.IsFalse(SortiePrepRules.TryFindSibling(_catalog, "muc_easy", EBossDifficulty.Hard, out var sibling));
            Assert.IsNull(sibling);
        }

        [Test]
        public void TryFindSibling_NonTrialCatalog_AllowsDefinedHard()
        {
            Build(false, new ProgressionCatalogSO.BossBattleDefinition("muc_easy", "muc", EBossDifficulty.Easy, 1, true, true, default),
                new ProgressionCatalogSO.BossBattleDefinition("muc_hard", "muc", EBossDifficulty.Hard, 1, false, false, default));

            Assert.IsTrue(SortiePrepRules.TryFindSibling(_catalog, "muc_easy", EBossDifficulty.Hard, out var sibling));
            Assert.AreEqual("muc_hard", sibling.DefinitionId);
        }

        [Test]
        public void TryFindSibling_AvailableHard_ReturnsSameBossBattleNotFirstOfDifficulty()
        {
            Build(true, new ProgressionCatalogSO.BossBattleDefinition("other_hard", "other", EBossDifficulty.Hard, 1, true, true, default),
                new ProgressionCatalogSO.BossBattleDefinition("muc_easy", "muc", EBossDifficulty.Easy, 1, true, true, default),
                new ProgressionCatalogSO.BossBattleDefinition("muc_hard", "muc", EBossDifficulty.Hard, 1, true, true, default));

            Assert.IsTrue(SortiePrepRules.TryFindSibling(_catalog, "muc_easy", EBossDifficulty.Hard, out var sibling));
            Assert.AreEqual("muc_hard", sibling.DefinitionId, "TrySelectBattle의 첫 Hard(other_hard)가 아니라 같은 보스여야 한다");
        }

        [Test]
        public void TryFindSibling_UnknownBattleOrNullCatalog_ReturnsFalse()
        {
            Build(true, new ProgressionCatalogSO.BossBattleDefinition("muc_easy", "muc", EBossDifficulty.Easy, 1, true, true, default));

            Assert.IsFalse(SortiePrepRules.TryFindSibling(_catalog, "none", EBossDifficulty.Hard, out _));
            Assert.IsFalse(SortiePrepRules.TryFindSibling(null, "muc_easy", EBossDifficulty.Hard, out _));
        }

        private void Build(bool trial, params ProgressionCatalogSO.BossBattleDefinition[] battles)
        {
            _catalog = ScriptableObject.CreateInstance<ProgressionCatalogSO>();
            SetField("_trialEdition", trial);
            SetField("_bossBattles", battles);
            typeof(ProgressionCatalogSO).GetMethod("InvalidateLookups", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_catalog, null);
        }

        private void SetField(string name, object value)
        {
            typeof(ProgressionCatalogSO).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_catalog, value);
        }
    }
}
