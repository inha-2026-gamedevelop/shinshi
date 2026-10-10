// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEditor;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // BossPatternCatalogSource가 만들 때 받은 난이도의 패턴만 수집하고 그 난이도의 튜닝을 확인하는지, 난이도를 주지 않는
    // 생성자는 이지로 동작하는지 확인한다. 하드 정의는 에셋 복사본에만 더하고 에셋은 건드리지 않는다
    public class BossPatternCatalogSourceDifficultyTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string CLONE_SOURCE = "wolbaek_p02";
        private const string HARD_P1_ID   = "hardtest_ph1_a";
        private const string HARD_P2_ID   = "hardtest_ph2_a";
        private const int    HARD_PHASE_ONE_COUNT = 6;
        private const int    FIRST_PHASE  = 1;
        private const int    SECOND_PHASE = 2;

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        /****************************************
        *               Helpers
        ****************************************/

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; ++i)
            {
                UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        private static BossPatternCatalogSO LoadAsset()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            return catalog;
        }

        // 에셋은 읽기만 하고 복사본의 패턴 배열에 하드 정의를 더해 시험한다
        private BossPatternCatalogSO CopyWith(params PatternDefinition[] extra)
        {
            BossPatternCatalogSO copy = UnityEngine.Object.Instantiate(LoadAsset());
            _created.Add(copy);

            // 에셋에 실제로 들어간 하드 정의는 빼서 시험용 정의만 하드로 남는다
            var patterns = new List<PatternDefinition>();
            for (int i = 0; i < copy.Patterns.Count; ++i)
            {
                if (copy.Patterns[i].Difficulty != EBossDifficulty.Hard)
                {
                    patterns.Add(copy.Patterns[i]);
                }
            }

            patterns.AddRange(extra);

            const BindingFlags FLAGS = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(BossPatternCatalogSO).GetField("_patterns", FLAGS).SetValue(copy, patterns.ToArray());
            typeof(BossPatternCatalogSO).GetMethod("InvalidateLookups", FLAGS).Invoke(copy, null);
            return copy;
        }

        // 수치가 비어 있는 하드 정의, 하드 튜닝 검사에서는 미지정 오류가 난다
        private static PatternDefinition UntunedHard(string id, int phase)
        {
            return new PatternDefinition(id, WOLBAEK_ID, phase, EEnemyAttackShape.Circle, guardable: true,
                difficulty: EBossDifficulty.Hard);
        }

        // 이지 정의의 수치를 그대로 옮긴 하드 정의, 하드 튜닝 검사를 통과한다
        private static PatternDefinition TunedHardCopyOf(PatternDefinition source, string id)
        {
            return new PatternDefinition(id, source.BossId, source.Phase, source.BaseWeight,
                new List<float>(source.DistanceMultipliers).ToArray(), new List<float>(source.DirectionMultipliers).ToArray(),
                new List<float>(source.NearbyMultipliers).ToArray(), new List<float>(source.RecentUseMultipliers).ToArray(),
                source.TelegraphSeconds, source.TrackingSeconds, source.TurnRate, source.ActiveSeconds, source.RecoverySeconds,
                source.Shape, source.DamageRef, source.Guardable, difficulty: EBossDifficulty.Hard);
        }

        private static PatternDefinition[] TunedHardCopies(BossPatternCatalogSO asset, int count)
        {
            Assert.IsTrue(asset.TryGetPattern(CLONE_SOURCE, out PatternDefinition source));

            var copies = new PatternDefinition[count];
            for (int i = 0; i < count; ++i)
            {
                copies[i] = TunedHardCopyOf(source, $"{HARD_P1_ID}_{i}");
            }

            return copies;
        }

        private static List<string> Collect(IBossPatternCatalog catalog, int phase)
        {
            var results = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, phase, results);

            var ids = new List<string>();
            for (int i = 0; i < results.Count; ++i)
            {
                ids.Add(results[i].DefinitionId);
            }

            return ids;
        }

        private static void AssertAllDifficulty(IBossPatternCatalog catalog, int phase,
            EBossDifficulty expected)
        {
            var results = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, phase, results);

            Assert.IsNotEmpty(results, $"{expected} phase {phase}");
            for (int i = 0; i < results.Count; ++i)
            {
                Assert.AreEqual(expected, results[i].Difficulty, results[i].DefinitionId);
            }
        }

        /****************************************
        *       Adapter Follows Its Difficulty
        ****************************************/

        [Test]
        public void CollectPatterns_EasyAdapter_ReturnsOnlyEasyPatterns()
        {
            BossPatternCatalogSO mixed = CopyWith(UntunedHard(HARD_P1_ID, FIRST_PHASE), UntunedHard(HARD_P2_ID, SECOND_PHASE));
            var easy = new BossPatternCatalogSource(mixed, EBossDifficulty.Easy);

            AssertAllDifficulty(easy, FIRST_PHASE, EBossDifficulty.Easy);
            AssertAllDifficulty(easy, SECOND_PHASE, EBossDifficulty.Easy);
            CollectionAssert.DoesNotContain(Collect(easy, FIRST_PHASE), HARD_P1_ID);
            CollectionAssert.DoesNotContain(Collect(easy, SECOND_PHASE), HARD_P2_ID);
        }

        [Test]
        public void CollectPatterns_HardAdapter_ReturnsOnlyHardPatterns()
        {
            BossPatternCatalogSO mixed = CopyWith(UntunedHard(HARD_P1_ID, FIRST_PHASE), UntunedHard(HARD_P2_ID, SECOND_PHASE));
            var hard = new BossPatternCatalogSource(mixed, EBossDifficulty.Hard);

            CollectionAssert.AreEqual(new[] { HARD_P1_ID }, Collect(hard, FIRST_PHASE));
            CollectionAssert.AreEqual(new[] { HARD_P2_ID }, Collect(hard, SECOND_PHASE));
        }

        [Test]
        public void CollectPatterns_HardAdapterWithoutHardPatterns_ReturnsEmpty()
        {
            var hard = new BossPatternCatalogSource(CopyWith(), EBossDifficulty.Hard);

            Assert.IsEmpty(Collect(hard, FIRST_PHASE));
            Assert.IsEmpty(Collect(hard, SECOND_PHASE));
        }

        [Test]
        public void Constructor_WithoutDifficulty_BehavesAsEasy()
        {
            BossPatternCatalogSO mixed = CopyWith(UntunedHard(HARD_P1_ID, FIRST_PHASE), UntunedHard(HARD_P2_ID, SECOND_PHASE));
            var legacy = new BossPatternCatalogSource(mixed);
            var easy   = new BossPatternCatalogSource(mixed, EBossDifficulty.Easy);

            for (int phase = FIRST_PHASE; phase <= SECOND_PHASE; ++phase)
            {
                CollectionAssert.AreEqual(Collect(easy, phase), Collect(legacy, phase), $"phase {phase}");
            }

            Assert.AreEqual(easy.IsTuned(WOLBAEK_ID), legacy.IsTuned(WOLBAEK_ID));
            CollectionAssert.AreEqual(Collect(new BossPatternCatalogSource(LoadAsset()), FIRST_PHASE), Collect(legacy, FIRST_PHASE));
        }

        /****************************************
        *       Tuning Follows Its Difficulty
        ****************************************/

        [Test]
        public void IsTuned_UntunedHardDefinition_FailsHardOnlyAndKeepsEasy()
        {
            BossPatternCatalogSO baseline = LoadAsset();
            BossPatternCatalogSO mixed = CopyWith(UntunedHard(HARD_P1_ID, FIRST_PHASE));

            Assert.AreEqual(baseline.IsTuned(WOLBAEK_ID), new BossPatternCatalogSource(mixed, EBossDifficulty.Easy).IsTuned(WOLBAEK_ID));
            Assert.IsFalse(new BossPatternCatalogSource(mixed, EBossDifficulty.Hard).IsTuned(WOLBAEK_ID));
        }

        // 수치가 맞는 하드 정의라도 월백 하드 1페이즈 여섯 개가 모두 있어야 통과하고, 일부만 있으면 전투 가능으로 판정되지 않는다
        [Test]
        public void IsTuned_PartialTunedHard_FailsHardAndKeepsEasy()
        {
            BossPatternCatalogSO baseline = LoadAsset();
            BossPatternCatalogSO mixed = CopyWith(TunedHardCopies(baseline, HARD_PHASE_ONE_COUNT - 1));

            Assert.AreEqual(baseline.IsTuned(WOLBAEK_ID), new BossPatternCatalogSource(mixed, EBossDifficulty.Easy).IsTuned(WOLBAEK_ID));
            Assert.IsFalse(new BossPatternCatalogSource(mixed, EBossDifficulty.Hard).IsTuned(WOLBAEK_ID));
        }

        [Test]
        public void IsTuned_FullTunedHard_PassesHardAndKeepsEasy()
        {
            BossPatternCatalogSO baseline = LoadAsset();
            BossPatternCatalogSO mixed = CopyWith(TunedHardCopies(baseline, HARD_PHASE_ONE_COUNT));

            Assert.AreEqual(baseline.IsTuned(WOLBAEK_ID), new BossPatternCatalogSource(mixed, EBossDifficulty.Easy).IsTuned(WOLBAEK_ID));
            Assert.IsTrue(new BossPatternCatalogSource(mixed, EBossDifficulty.Hard).IsTuned(WOLBAEK_ID));
        }

        // 이지만 있는 에셋에서 하드 어댑터는 이지 수치가 지정돼 있어도 통과하면 안 된다
        [Test]
        public void IsTuned_HardAdapterOnEasyOnlyAsset_IsFalse()
        {
            BossPatternCatalogSO asset = CopyWith();

            Assert.AreEqual(asset.IsTuned(WOLBAEK_ID), new BossPatternCatalogSource(asset).IsTuned(WOLBAEK_ID));
            Assert.IsFalse(new BossPatternCatalogSource(asset, EBossDifficulty.Hard).IsTuned(WOLBAEK_ID));
        }

        /****************************************
        *               Guards
        ****************************************/

        [Test]
        public void Constructor_NullCatalog_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BossPatternCatalogSource(null));
            Assert.Throws<ArgumentNullException>(() => new BossPatternCatalogSource(null, EBossDifficulty.Hard));
        }
    }
}
