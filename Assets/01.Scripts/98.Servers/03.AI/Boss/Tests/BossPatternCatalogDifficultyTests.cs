// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 패턴 정의의 난이도 구분이 기존 12개 이지 패턴과 난이도 없는 호출의 결과를 바꾸지 않는지, 하드 시험 정의가 이지 수집,
    // 튜닝 검증, 월백 1페이즈 6개 검사에 섞이지 않는지 확인한다. 하드 정의는 시험용 복사본에만 더하고 에셋은 건드리지 않는다
    public class BossPatternCatalogDifficultyTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string COUNT_ERROR  = "월백 1페이즈 패턴 수";
        private const string HARD_COUNT_ERROR = "월백 하드";
        private const string TUNED_PHASE_ONE_SOURCE = "wolbaek_p02";
        private const string TUNED_PHASE_TWO_SOURCE = "wolbaek_ph2_p06";
        private const string OTHER_BOSS_ID = "boss_mucheol";
        private const int    HARD_PHASE_ONE_COUNT = 6;
        private const int    HARD_PHASE_TWO_COUNT = 7;
        private const string HARD_P1_ID   = "hardtest_ph1_a";
        private const string HARD_P2_ID   = "hardtest_ph2_a";
        private const int    FIRST_PHASE  = 1;
        private const int    SECOND_PHASE = 2;
        private const int    PHASE_ONE_COUNT = 6;
        private const int    PHASE_TWO_COUNT = 7;

        private static readonly string[] G_ExistingIds =
        {
            "wolbaek_p01", "wolbaek_p02", "wolbaek_p03", "wolbaek_p04", "wolbaek_p05", "wolbaek_p06",
            "wolbaek_ph2_p01", "wolbaek_ph2_p02", "wolbaek_ph2_p03", "wolbaek_ph2_p04", "wolbaek_ph2_p05", "wolbaek_ph2_p06", "wolbaek_ph2_p07"
        };

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<Object> _created = new List<Object>();

        /****************************************
        *               Helpers
        ****************************************/

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; ++i)
            {
                Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        private static BossPatternCatalogSO LoadAsset()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            return catalog;
        }

        // 에셋은 읽기만 하고 복사본의 패턴 배열을 갈아 끼워 시험한다. 에셋에 실제로 들어간 하드 정의는 빼서 시험용 정의만 하드로 남는다
        private BossPatternCatalogSO CopyWith(params PatternDefinition[] extra)
        {
            return CopyWithout(null, extra);
        }

        private BossPatternCatalogSO CopyWithout(string removedId, params PatternDefinition[] extra)
        {
            BossPatternCatalogSO copy = Object.Instantiate(LoadAsset());
            _created.Add(copy);

            var patterns = new List<PatternDefinition>();
            IReadOnlyList<PatternDefinition> source = copy.Patterns;
            for (int i = 0; i < source.Count; ++i)
            {
                if ((source[i].DefinitionId != removedId) && (source[i].Difficulty != EBossDifficulty.Hard))
                {
                    patterns.Add(source[i]);
                }
            }

            patterns.AddRange(extra);

            const BindingFlags FLAGS = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(BossPatternCatalogSO).GetField("_patterns", FLAGS).SetValue(copy, patterns.ToArray());
            typeof(BossPatternCatalogSO).GetMethod("InvalidateLookups", FLAGS).Invoke(copy, null);
            return copy;
        }

        private static PatternDefinition HardPattern(string id, int phase)
        {
            return new PatternDefinition(id, WOLBAEK_ID, phase, EEnemyAttackShape.Circle, guardable: true,
                difficulty: EBossDifficulty.Hard);
        }

        private static List<string> Ids(List<PatternDefinition> patterns)
        {
            var ids = new List<string>();
            for (int i = 0; i < patterns.Count; ++i)
            {
                ids.Add(patterns[i].DefinitionId);
            }

            return ids;
        }

        private static List<string> Collect(BossPatternCatalogSO catalog, int phase)
        {
            var results = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, phase, results);
            return Ids(results);
        }

        private static List<string> Collect(BossPatternCatalogSO catalog, int phase, EBossDifficulty difficulty)
        {
            var results = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, phase, difficulty, results);
            return Ids(results);
        }

        private static List<string> Tuning(BossPatternCatalogSO catalog, int phase)
        {
            var errors = new List<string>();
            catalog.ValidateTuning(WOLBAEK_ID, phase, errors);
            return errors;
        }

        private static List<string> Tuning(BossPatternCatalogSO catalog, int phase, EBossDifficulty difficulty)
        {
            var errors = new List<string>();
            catalog.ValidateTuning(WOLBAEK_ID, phase, difficulty, errors);
            return errors;
        }

        private static bool HasCountError(BossPatternCatalogSO catalog)
        {
            var errors = new List<string>();
            catalog.Validate(errors);
            for (int i = 0; i < errors.Count; ++i)
            {
                if (errors[i].Contains(COUNT_ERROR))
                {
                    return true;
                }
            }

            return false;
        }

        /****************************************
        *      Existing Asset Reads As Easy
        ****************************************/

        [Test]
        public void Asset_ExistingTwelvePatterns_AreAllEasyWithSameIds()
        {
            BossPatternCatalogSO catalog = LoadAsset();

            for (int i = 0; i < G_ExistingIds.Length; ++i)
            {
                Assert.IsTrue(catalog.TryGetPattern(G_ExistingIds[i], out PatternDefinition pattern), G_ExistingIds[i]);
                Assert.AreEqual(EBossDifficulty.Easy, pattern.Difficulty, G_ExistingIds[i]);
                Assert.AreEqual(WOLBAEK_ID, pattern.BossId, G_ExistingIds[i]);
            }

            // 월백은 기존 12개 밖의 정의가 모두 하드 정의이고, 기존 12개는 하나도 하드가 되지 않는다
            var existing = new List<string>(G_ExistingIds);
            for (int i = 0; i < catalog.Patterns.Count; ++i)
            {
                PatternDefinition pattern = catalog.Patterns[i];
                if (pattern.BossId != WOLBAEK_ID)
                {
                    continue;
                }

                EBossDifficulty expected = existing.Contains(pattern.DefinitionId) ? EBossDifficulty.Easy : EBossDifficulty.Hard;
                Assert.AreEqual(expected, pattern.Difficulty, pattern.DefinitionId);
            }
        }

        // 무철은 현재 운영 에셋 기준으로 이지 P01~P06(1페이즈)과 이지 2페이즈 P01~P06, 하드 전용 2페이즈 P07이다
        [Test]
        public void Asset_MucheolPatterns_HaveExplicitPhaseAndDifficulty()
        {
            BossPatternCatalogSO catalog = LoadAsset();

            var expected = new Dictionary<string, KeyValuePair<int, EBossDifficulty>>();
            for (int n = 1; n <= 6; ++n)
            {
                expected["mucheol_p0" + n] = new KeyValuePair<int, EBossDifficulty>(FIRST_PHASE, EBossDifficulty.Easy);
                expected["mucheol_ph2_p0" + n] = new KeyValuePair<int, EBossDifficulty>(SECOND_PHASE, EBossDifficulty.Easy);
            }

            expected["mucheol_ph2_p07"] = new KeyValuePair<int, EBossDifficulty>(SECOND_PHASE, EBossDifficulty.Hard);

            int found = 0;
            for (int i = 0; i < catalog.Patterns.Count; ++i)
            {
                PatternDefinition pattern = catalog.Patterns[i];
                if (pattern.BossId != OTHER_BOSS_ID)
                {
                    continue;
                }

                ++found;
                Assert.IsTrue(expected.TryGetValue(pattern.DefinitionId, out KeyValuePair<int, EBossDifficulty> want), "예상 밖 무철 정의 " + pattern.DefinitionId);
                Assert.AreEqual(want.Key, pattern.Phase, pattern.DefinitionId);
                Assert.AreEqual(want.Value, pattern.Difficulty, pattern.DefinitionId);
            }

            Assert.AreEqual(expected.Count, found, "무철 정의 수");
        }

        [Test]
        public void Asset_CollectPatterns_WithoutDifficultyEqualsEasyAndHardHasOnlyHardPatterns()
        {
            BossPatternCatalogSO catalog = LoadAsset();

            for (int phase = FIRST_PHASE; phase <= SECOND_PHASE; ++phase)
            {
                List<string> legacy = Collect(catalog, phase);
                CollectionAssert.AreEqual(legacy, Collect(catalog, phase, EBossDifficulty.Easy), $"phase {phase}");
            }

            // 하드 수집은 에셋에서 난이도가 Hard인 정의만 돌려준다. 에셋에 하드 정의가 있든 없든 성립한다
            for (int phase = FIRST_PHASE; phase <= SECOND_PHASE; ++phase)
            {
                var expectedHard = new List<string>();
                for (int i = 0; i < catalog.Patterns.Count; ++i)
                {
                    PatternDefinition pattern = catalog.Patterns[i];
                    if ((pattern.BossId == WOLBAEK_ID) && (pattern.Phase == phase) && (pattern.Difficulty == EBossDifficulty.Hard))
                    {
                        expectedHard.Add(pattern.DefinitionId);
                    }
                }

                CollectionAssert.AreEquivalent(expectedHard, Collect(catalog, phase, EBossDifficulty.Hard), $"phase {phase} hard");
            }

            Assert.AreEqual(PHASE_ONE_COUNT, Collect(catalog, FIRST_PHASE).Count);
            Assert.AreEqual(PHASE_TWO_COUNT, Collect(catalog, SECOND_PHASE).Count);
        }

        [Test]
        public void Asset_TuningChecks_WithoutDifficultyEqualEasyAndHardPhaseTwoIsTunedWithSeven()
        {
            BossPatternCatalogSO catalog = LoadAsset();

            for (int phase = FIRST_PHASE; phase <= SECOND_PHASE; ++phase)
            {
                CollectionAssert.AreEqual(Tuning(catalog, phase), Tuning(catalog, phase, EBossDifficulty.Easy), $"phase {phase}");
                Assert.AreEqual(catalog.IsPhaseTuned(WOLBAEK_ID, phase), catalog.IsPhaseTuned(WOLBAEK_ID, phase, EBossDifficulty.Easy));
            }

            // 하드 1페이즈는 여섯 개, 하드 2페이즈는 일곱 개가 들어와 통과한다. IsTuned는 1페이즈만 본다
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, FIRST_PHASE, EBossDifficulty.Hard), "phase 1 hard");
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, SECOND_PHASE, EBossDifficulty.Hard), "phase 2 hard");
            Assert.AreEqual(catalog.IsTuned(WOLBAEK_ID), catalog.IsTuned(WOLBAEK_ID, EBossDifficulty.Easy));
            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID, EBossDifficulty.Hard));

            var legacy = new List<string>();
            catalog.ValidateTuning(WOLBAEK_ID, legacy);
            CollectionAssert.AreEqual(Tuning(catalog, FIRST_PHASE, EBossDifficulty.Easy), legacy);
        }

        [Test]
        public void Asset_Validate_HasNoPhaseOneCountError()
        {
            Assert.IsFalse(HasCountError(LoadAsset()));
        }

        /****************************************
        *     Hard Test Definitions Stay Apart
        ****************************************/

        [Test]
        public void CollectPatterns_WithHardDefinitions_KeepsEasyListAndSeparatesHard()
        {
            BossPatternCatalogSO baseline = LoadAsset();
            BossPatternCatalogSO mixed = CopyWith(HardPattern(HARD_P1_ID, FIRST_PHASE), HardPattern(HARD_P2_ID, SECOND_PHASE));

            for (int phase = FIRST_PHASE; phase <= SECOND_PHASE; ++phase)
            {
                CollectionAssert.AreEqual(Collect(baseline, phase), Collect(mixed, phase), $"phase {phase} legacy");
                CollectionAssert.AreEqual(Collect(baseline, phase), Collect(mixed, phase, EBossDifficulty.Easy), $"phase {phase} easy");
            }

            CollectionAssert.AreEqual(new[] { HARD_P1_ID }, Collect(mixed, FIRST_PHASE, EBossDifficulty.Hard));
            CollectionAssert.AreEqual(new[] { HARD_P2_ID }, Collect(mixed, SECOND_PHASE, EBossDifficulty.Hard));
        }

        [Test]
        public void ValidateTuning_WithHardDefinitions_KeepsEasyResultAndReportsOnlyHardIds()
        {
            BossPatternCatalogSO baseline = LoadAsset();
            BossPatternCatalogSO mixed = CopyWith(HardPattern(HARD_P1_ID, FIRST_PHASE), HardPattern(HARD_P2_ID, SECOND_PHASE));

            for (int phase = FIRST_PHASE; phase <= SECOND_PHASE; ++phase)
            {
                CollectionAssert.AreEqual(Tuning(baseline, phase), Tuning(mixed, phase), $"phase {phase} legacy");
                CollectionAssert.AreEqual(Tuning(baseline, phase), Tuning(mixed, phase, EBossDifficulty.Easy), $"phase {phase} easy");
                Assert.AreEqual(baseline.IsPhaseTuned(WOLBAEK_ID, phase), mixed.IsPhaseTuned(WOLBAEK_ID, phase), $"phase {phase} tuned");
            }

            Assert.AreEqual(baseline.IsTuned(WOLBAEK_ID), mixed.IsTuned(WOLBAEK_ID));
            Assert.IsFalse(mixed.IsTuned(WOLBAEK_ID, EBossDifficulty.Hard));

            // 하드 검사는 하드 정의의 미지정 수치와 하드 개수만 말하고 이지 정의 ID는 한 줄도 나오지 않는다
            List<string> hardErrors = Tuning(mixed, FIRST_PHASE, EBossDifficulty.Hard);
            Assert.IsNotEmpty(hardErrors);
            for (int i = 0; i < hardErrors.Count; ++i)
            {
                Assert.IsTrue((hardErrors[i].Contains(HARD_P1_ID)) || (hardErrors[i].Contains(HARD_COUNT_ERROR)), hardErrors[i]);
                StringAssert.DoesNotContain("wolbaek_p0", hardErrors[i]);
                StringAssert.DoesNotContain("wolbaek_ph2", hardErrors[i]);
            }
        }

        [Test]
        public void Validate_WithHardDefinitions_DoesNotCountThemInEasyPhaseOne()
        {
            BossPatternCatalogSO mixed = CopyWith(HardPattern(HARD_P1_ID, FIRST_PHASE));

            Assert.IsFalse(HasCountError(mixed));
        }

        // 하드가 이지 자리를 대신 채우지 못한다. 이지 한 개를 빼고 하드 한 개를 더해 총 6개가 돼도 이지 구성 오류로 남아야 한다
        [Test]
        public void Validate_EasyPatternMissingButHardAdded_StillReportsPhaseOneCountError()
        {
            BossPatternCatalogSO mixed = CopyWithout("wolbaek_p06", HardPattern(HARD_P1_ID, FIRST_PHASE));

            Assert.IsTrue(HasCountError(mixed));
        }

        /****************************************
        *       Wolbaek Hard Pattern Counts
        ****************************************/

        // 이지 정의의 수치를 그대로 옮긴 하드 정의, 수치 검사는 통과하므로 개수 검사만 따로 시험할 수 있다
        private static PatternDefinition TunedHardCopyOf(PatternDefinition source, string id)
        {
            return new PatternDefinition(id, source.BossId, source.Phase, source.BaseWeight,
                new List<float>(source.DistanceMultipliers).ToArray(), new List<float>(source.DirectionMultipliers).ToArray(),
                new List<float>(source.NearbyMultipliers).ToArray(), new List<float>(source.RecentUseMultipliers).ToArray(),
                source.TelegraphSeconds, source.TrackingSeconds, source.TurnRate, source.ActiveSeconds, source.RecoverySeconds,
                source.Shape, source.DamageRef, source.Guardable,
                group: source.Group, enhancedBaseWeight: source.EnhancedBaseWeight, difficulty: EBossDifficulty.Hard);
        }

        private BossPatternCatalogSO CopyWithTunedHard(int phaseOneCount, int phaseTwoCount)
        {
            BossPatternCatalogSO asset = LoadAsset();
            Assert.IsTrue(asset.TryGetPattern(TUNED_PHASE_ONE_SOURCE, out PatternDefinition phaseOne));
            Assert.IsTrue(asset.TryGetPattern(TUNED_PHASE_TWO_SOURCE, out PatternDefinition phaseTwo));

            var extra = new List<PatternDefinition>();
            for (int i = 0; i < phaseOneCount; ++i)
            {
                extra.Add(TunedHardCopyOf(phaseOne, $"hardtest_ph1_{i}"));
            }

            for (int i = 0; i < phaseTwoCount; ++i)
            {
                extra.Add(TunedHardCopyOf(phaseTwo, $"hardtest_ph2_{i}"));
            }

            return CopyWith(extra.ToArray());
        }

        // 수치가 모두 맞는 하드 정의로만 시험하므로, 실패 원인은 개수뿐이다
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(5, false)]
        [TestCase(6, true)]
        [TestCase(7, false)]
        public void IsPhaseTuned_HardPhaseOneCount_PassesOnlyAtSix(int count, bool expected)
        {
            BossPatternCatalogSO catalog = CopyWithTunedHard(count, HARD_PHASE_TWO_COUNT);

            Assert.AreEqual(expected, catalog.IsPhaseTuned(WOLBAEK_ID, FIRST_PHASE, EBossDifficulty.Hard));
            Assert.AreEqual(expected, catalog.IsTuned(WOLBAEK_ID, EBossDifficulty.Hard));
            Assert.AreEqual(expected, Tuning(catalog, FIRST_PHASE, EBossDifficulty.Hard).Count == 0);
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(6, false)]
        [TestCase(7, true)]
        [TestCase(8, false)]
        public void IsPhaseTuned_HardPhaseTwoCount_PassesOnlyAtSeven(int count, bool expected)
        {
            BossPatternCatalogSO catalog = CopyWithTunedHard(HARD_PHASE_ONE_COUNT, count);

            Assert.AreEqual(expected, catalog.IsPhaseTuned(WOLBAEK_ID, SECOND_PHASE, EBossDifficulty.Hard));
            Assert.AreEqual(expected, Tuning(catalog, SECOND_PHASE, EBossDifficulty.Hard).Count == 0);
        }

        [TestCase(5, 6, "1페이즈 패턴 수 5 != 6")]
        [TestCase(7, 6, "1페이즈 패턴 수 7 != 6")]
        [TestCase(6, 6, "2페이즈 패턴 수 6 != 7")]
        [TestCase(6, 8, "2페이즈 패턴 수 8 != 7")]
        public void ValidateTuning_HardCountMismatch_LeavesDiagnosticWithBothCounts(int phaseOne, int phaseTwo, string expected)
        {
            BossPatternCatalogSO catalog = CopyWithTunedHard(phaseOne, phaseTwo);
            var errors = new List<string>();
            catalog.ValidateTuning(WOLBAEK_ID, FIRST_PHASE, EBossDifficulty.Hard, errors);
            catalog.ValidateTuning(WOLBAEK_ID, SECOND_PHASE, EBossDifficulty.Hard, errors);

            Assert.IsTrue(errors.Exists(e => (e.Contains(HARD_COUNT_ERROR)) && (e.Contains(expected))), string.Join("\n", errors));
        }

        // 페이즈마다 따로 본다. 2페이즈가 모자란 동안에도 1페이즈가 맞으면 1페이즈 검증은 통과한다
        [Test]
        public void IsPhaseTuned_HardPhasesAreIndependent()
        {
            BossPatternCatalogSO catalog = CopyWithTunedHard(HARD_PHASE_ONE_COUNT, HARD_PHASE_TWO_COUNT - 1);

            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, FIRST_PHASE, EBossDifficulty.Hard));
            Assert.IsFalse(catalog.IsPhaseTuned(WOLBAEK_ID, SECOND_PHASE, EBossDifficulty.Hard));
        }

        // 하드 개수 검사가 이지와 다른 보스의 결과를 바꾸지 않는다
        [Test]
        public void ValidateTuning_HardCounts_DoNotChangeEasyOrOtherBossResults()
        {
            BossPatternCatalogSO baseline = LoadAsset();
            BossPatternCatalogSO partial = CopyWithTunedHard(3, 2);

            for (int phase = FIRST_PHASE; phase <= SECOND_PHASE; ++phase)
            {
                CollectionAssert.AreEqual(Tuning(baseline, phase, EBossDifficulty.Easy), Tuning(partial, phase, EBossDifficulty.Easy), $"phase {phase}");
            }

            foreach (EBossDifficulty difficulty in new[] { EBossDifficulty.Easy, EBossDifficulty.Hard })
            {
                var expected = new List<string>();
                var actual   = new List<string>();
                baseline.ValidateTuning(OTHER_BOSS_ID, FIRST_PHASE, difficulty, expected);
                partial.ValidateTuning(OTHER_BOSS_ID, FIRST_PHASE, difficulty, actual);

                CollectionAssert.AreEqual(expected, actual, difficulty.ToString());
                Assert.IsFalse(actual.Exists(e => e.Contains(HARD_COUNT_ERROR)), difficulty.ToString());
            }

            Assert.IsFalse(Tuning(partial, FIRST_PHASE, EBossDifficulty.Easy).Exists(e => e.Contains(HARD_COUNT_ERROR)));
        }

        [Test]
        public void PatternDefinition_WithoutDifficultyArgument_IsEasy()
        {
            var shortForm = new PatternDefinition("easytest_a", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Circle, guardable: true);

            Assert.AreEqual(EBossDifficulty.Easy, shortForm.Difficulty);
            Assert.AreEqual(EBossDifficulty.Hard, HardPattern(HARD_P1_ID, FIRST_PHASE).Difficulty);
        }
    }
}
