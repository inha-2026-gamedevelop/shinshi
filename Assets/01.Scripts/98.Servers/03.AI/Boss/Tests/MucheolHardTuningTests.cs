// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 무철 하드 전용 정의(mucheol_hard_...)가 이지 정의를 대신하는 구조와, 정의 값이 서버 실행 데이터 및 연출 크기와 어긋나지 않는지 본다.
    // 수치는 플레이 테스트 전 시작안이라 밸런스 값이 아니며, 여기서는 서로 맞물리는 값끼리만 비교한다
    public class MucheolHardTuningTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  TEST_HREF    = 3541f;
        private const float  ARENA        = 44f;
        private const float  TOLERANCE    = 0.001f;
        private const int    FIRST_PHASE  = 1;
        private const int    SECOND_PHASE = 2;
        private const float  FAN_RANGE_SCALE      = 1.4f; // 하드 부채꼴 사거리 / 이지 사거리
        private const float  CIRCLE_RANGE_SCALE   = 1.3f; // 하드 원형 반경 / 이지 반경
        private const float  SECOND_CIRCLE_SCALE  = 1.2f; // 2페이즈 둘째 원 반경 / 첫째 원 반경

        private static readonly string[] PhaseOneEasyIds =
        {
            MucheolPatternFactory.P01_ID, MucheolPatternFactory.P02_ID, MucheolPatternFactory.P03_ID,
            MucheolPatternFactory.P04_ID, MucheolPatternFactory.P05_ID, MucheolPatternFactory.P06_ID
        };

        private static readonly string[] PhaseTwoEasyIds =
        {
            MucheolPhase2PatternFactory.P01_ID, MucheolPhase2PatternFactory.P02_ID, MucheolPhase2PatternFactory.P03_ID,
            MucheolPhase2PatternFactory.P04_ID, MucheolPhase2PatternFactory.P05_ID, MucheolPhase2PatternFactory.P06_ID
        };

        /****************************************
        *               Helpers
        ****************************************/

        private static BossPatternCatalogSO LoadCatalog()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            return catalog;
        }

        private static PatternDefinition Get(BossPatternCatalogSO catalog, string id)
        {
            Assert.IsTrue(catalog.TryGetPattern(id, out PatternDefinition pattern), id);
            return pattern;
        }

        private static List<string> CollectIds(BossPatternCatalogSO catalog, int phase, EBossDifficulty difficulty)
        {
            var found = new List<PatternDefinition>();
            catalog.CollectPatterns(MucheolPatternFactory.BOSS_ID, phase, difficulty, found);
            var ids = new List<string>();
            for (int i = 0; i < found.Count; ++i)
            {
                ids.Add(found[i].DefinitionId);
            }

            return ids;
        }

        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void ToMucheolBaseId_HardIdsReturnEasyIdsAndOthersStay()
        {
            Assert.AreEqual("mucheol_p01", BossPatternCatalogSO.ToMucheolBaseId("mucheol_hard_p01"));
            Assert.AreEqual("mucheol_ph2_p05", BossPatternCatalogSO.ToMucheolBaseId("mucheol_hard_ph2_p05"));
            Assert.AreEqual("mucheol_ph2_p07", BossPatternCatalogSO.ToMucheolBaseId("mucheol_ph2_p07"));
            Assert.AreEqual("wolbaek_hard_p01", BossPatternCatalogSO.ToMucheolBaseId("wolbaek_hard_p01"));
            Assert.IsNull(BossPatternCatalogSO.ToMucheolBaseId(null));
        }

        [Test]
        public void ToMucheolHardId_RoundTripsAndRejectsOtherBosses()
        {
            for (int i = 0; i < PhaseOneEasyIds.Length; ++i)
            {
                string hardId = BossPatternCatalogSO.ToMucheolHardId(PhaseOneEasyIds[i]);
                Assert.AreEqual(PhaseOneEasyIds[i], BossPatternCatalogSO.ToMucheolBaseId(hardId), hardId);
            }

            Assert.AreEqual("mucheol_hard_ph2_p03", BossPatternCatalogSO.ToMucheolHardId("mucheol_ph2_p03"));
            Assert.IsNull(BossPatternCatalogSO.ToMucheolHardId("wolbaek_p01"));
            Assert.IsNull(BossPatternCatalogSO.ToMucheolHardId("mucheol_hard_p01"));
            Assert.IsNull(BossPatternCatalogSO.ToMucheolHardId(null));
        }

        [Test]
        public void Asset_HardCollect_ReplacesEasyP01ToP06WithHardAndKeepsP07()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            List<string> phaseOne = CollectIds(catalog, FIRST_PHASE, EBossDifficulty.Hard);
            Assert.AreEqual(PhaseOneEasyIds.Length, phaseOne.Count);

            List<string> phaseTwo = CollectIds(catalog, SECOND_PHASE, EBossDifficulty.Hard);
            Assert.AreEqual(PhaseTwoEasyIds.Length + 1, phaseTwo.Count);
            Assert.IsTrue(phaseTwo.Contains(MucheolPhase2PatternFactory.P07_ID));

            for (int i = 0; i < PhaseOneEasyIds.Length; ++i)
            {
                Assert.IsTrue(phaseOne.Contains(BossPatternCatalogSO.ToMucheolHardId(PhaseOneEasyIds[i])), PhaseOneEasyIds[i]);
                Assert.IsFalse(phaseOne.Contains(PhaseOneEasyIds[i]), PhaseOneEasyIds[i]);
                Assert.IsTrue(phaseTwo.Contains(BossPatternCatalogSO.ToMucheolHardId(PhaseTwoEasyIds[i])), PhaseTwoEasyIds[i]);
                Assert.IsFalse(phaseTwo.Contains(PhaseTwoEasyIds[i]), PhaseTwoEasyIds[i]);
            }
        }

        [Test]
        public void Asset_EasyCollect_NeverReceivesHardDefinitions()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            for (int phase = FIRST_PHASE; phase <= SECOND_PHASE; ++phase)
            {
                List<string> easy = CollectIds(catalog, phase, EBossDifficulty.Easy);
                Assert.AreEqual(PhaseOneEasyIds.Length, easy.Count, $"phase {phase}");
                for (int i = 0; i < easy.Count; ++i)
                {
                    Assert.IsFalse(easy[i].Contains("hard"), easy[i]);
                    Assert.AreNotEqual(MucheolPhase2PatternFactory.P07_ID, easy[i]);
                }
            }
        }

        [Test]
        public void Asset_HardIsTunedAndEveryHardDefinitionIsReady()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.IsTrue(catalog.IsPhaseTuned(MucheolPatternFactory.BOSS_ID, FIRST_PHASE, EBossDifficulty.Hard));
            Assert.IsTrue(catalog.IsPhaseTuned(MucheolPatternFactory.BOSS_ID, SECOND_PHASE, EBossDifficulty.Hard));
            Assert.IsTrue(catalog.IsPhaseTuned(MucheolPatternFactory.BOSS_ID, FIRST_PHASE, EBossDifficulty.Easy));
            Assert.IsTrue(catalog.IsPhaseTuned(MucheolPatternFactory.BOSS_ID, SECOND_PHASE, EBossDifficulty.Easy));

            for (int i = 0; i < PhaseOneEasyIds.Length; ++i)
            {
                Assert.IsTrue(catalog.IsPatternReady(MucheolPatternFactory.BOSS_ID,
                    Get(catalog, BossPatternCatalogSO.ToMucheolHardId(PhaseOneEasyIds[i]))), PhaseOneEasyIds[i]);
                Assert.IsTrue(catalog.IsPatternReady(MucheolPatternFactory.BOSS_ID,
                    Get(catalog, BossPatternCatalogSO.ToMucheolHardId(PhaseTwoEasyIds[i]))), PhaseTwoEasyIds[i]);
            }
        }

        [Test]
        public void Asset_HardFanAndCircleRanges_AreLargerThanEasyAndKeepTheirAngles()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            string[] fans =
            {
                MucheolPatternFactory.P01_ID, MucheolPatternFactory.P02_ID, MucheolPatternFactory.P05_ID,
                MucheolPhase2PatternFactory.P01_ID, MucheolPhase2PatternFactory.P02_ID, MucheolPhase2PatternFactory.P05_ID
            };

            for (int i = 0; i < fans.Length; ++i)
            {
                PatternDefinition easy = Get(catalog, fans[i]);
                PatternDefinition hard = Get(catalog, BossPatternCatalogSO.ToMucheolHardId(fans[i]));
                Assert.AreEqual(easy.Shape.Range * FAN_RANGE_SCALE, hard.Shape.Range, TOLERANCE, fans[i]);
                Assert.AreEqual(easy.Shape.ArcAngle, hard.Shape.ArcAngle, TOLERANCE, fans[i]);
            }

            foreach (string circle in new[] { MucheolPatternFactory.P04_ID, MucheolPhase2PatternFactory.P04_ID })
            {
                Assert.AreEqual(Get(catalog, circle).Shape.Range * CIRCLE_RANGE_SCALE,
                    Get(catalog, BossPatternCatalogSO.ToMucheolHardId(circle)).Shape.Range, TOLERANCE, circle);
            }
        }

        [Test]
        public void Asset_HardDefinitions_ShortenRecoveryAndKeepGroupAndDistanceBands()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            for (int i = 0; i < PhaseOneEasyIds.Length; ++i)
            {
                foreach (string easyId in new[] { PhaseOneEasyIds[i], PhaseTwoEasyIds[i] })
                {
                    PatternDefinition easy = Get(catalog, easyId);
                    PatternDefinition hard = Get(catalog, BossPatternCatalogSO.ToMucheolHardId(easyId));
                    Assert.Less(hard.RecoverySeconds, easy.RecoverySeconds, easyId);
                    Assert.AreEqual(easy.Phase, hard.Phase, easyId);
                    Assert.AreEqual(easy.Group, hard.Group, easyId);
                    Assert.AreEqual(easy.Guardable, hard.Guardable, easyId);
                    CollectionAssert.AreEqual(easy.DistanceMultipliers, hard.DistanceMultipliers, easyId);
                }
            }
        }

        [Test]
        public void HardPhaseTwoSpec_StepRangesMatchHardDefinitionsInAsset()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            MucheolPhase2Spec spec = MucheolDevCatalog.CreateHardPhaseTwoSpec();

            AssertFanSteps(spec.P01, Get(catalog, BossPatternCatalogSO.ToMucheolHardId(MucheolPhase2PatternFactory.P01_ID)));
            AssertFanSteps(spec.P05, Get(catalog, BossPatternCatalogSO.ToMucheolHardId(MucheolPhase2PatternFactory.P05_ID)));

            PatternDefinition p02 = Get(catalog, BossPatternCatalogSO.ToMucheolHardId(MucheolPhase2PatternFactory.P02_ID));
            Assert.AreEqual(p02.Shape.Range, spec.P02.Steps[0].Range, TOLERANCE);

            PatternDefinition p04 = Get(catalog, BossPatternCatalogSO.ToMucheolHardId(MucheolPhase2PatternFactory.P04_ID));
            Assert.AreEqual(p04.Shape.Range, spec.P04.Steps[0].Range, TOLERANCE);
            Assert.AreEqual(p04.Shape.Range * SECOND_CIRCLE_SCALE, spec.P04.Steps[1].Range, TOLERANCE);
        }

        // 첫 타의 사거리는 정의와 같고, 마지막 타의 지연은 정의의 실행 시간과 같아야 시전이 끝나기 전에 모든 타가 나간다
        private static void AssertFanSteps(MucheolSequenceSpec spec, PatternDefinition definition)
        {
            for (int i = 0; i < spec.StepCount; ++i)
            {
                Assert.AreEqual(definition.Shape.Range, spec.Steps[i].Range, TOLERANCE, $"{definition.DefinitionId} step {i}");
            }

            Assert.AreEqual(definition.ActiveSeconds, spec.Steps[spec.StepCount - 1].DelaySeconds, TOLERANCE, definition.DefinitionId);
        }

        [Test]
        public void HardComboSpec_RangeMatchesHardP05Definition()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            PatternDefinition hard = Get(catalog, BossPatternCatalogSO.ToMucheolHardId(MucheolPatternFactory.P05_ID));
            MucheolComboSpec spec = MucheolDevCatalog.CreateHardComboSpec();

            for (int i = 0; i < spec.HitCount; ++i)
            {
                Assert.AreEqual(hard.Shape.Range, spec.Hits[i].Range, TOLERANCE, $"hit {i}");
            }
        }

        [Test]
        public void Phase2Factory_HardDefinition_NeedsHardSpecAndUsesItWhenGiven()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            var devCatalog = new MucheolDevCatalog(ARENA, true);
            var vitals = new FakeBossVitals();
            PatternDefinition hardP01 = Get(catalog, BossPatternCatalogSO.ToMucheolHardId(MucheolPhase2PatternFactory.P01_ID));
            PatternDefinition easyP01 = Get(catalog, MucheolPhase2PatternFactory.P01_ID);

            var withoutHard = new MucheolPhase2PatternFactory(TEST_HREF, MucheolDevCatalog.CreatePhaseTwoSpec(), vitals, devCatalog);
            Assert.IsTrue(withoutHard.CanCreate(easyP01));
            Assert.IsFalse(withoutHard.CanCreate(hardP01), "하드 데이터 없이 하드 정의를 이지 데이터로 만들면 사거리가 어긋난다");
            Assert.IsNull(withoutHard.Create(hardP01));

            var withHard = new MucheolPhase2PatternFactory(TEST_HREF, MucheolDevCatalog.CreatePhaseTwoSpec(), vitals, devCatalog,
                hardSpec: MucheolDevCatalog.CreateHardPhaseTwoSpec());
            Assert.IsTrue(withHard.CanCreate(hardP01));
            Assert.IsInstanceOf<MucheolSequencePattern>(withHard.Create(hardP01));
        }

        [Test]
        public void Phase1Factory_HardP05_NeedsHardComboSpec()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            PatternDefinition hardP05 = Get(catalog, BossPatternCatalogSO.ToMucheolHardId(MucheolPatternFactory.P05_ID));
            PatternDefinition hardP01 = Get(catalog, BossPatternCatalogSO.ToMucheolHardId(MucheolPatternFactory.P01_ID));

            var withoutHard = new MucheolPatternFactory(TEST_HREF, comboSpec: new MucheolComboSpec(new MucheolComboHit(5f, 120f, 0.14f, 0f), new MucheolComboHit(5f, 70f, 0.24f, 0.5f)));
            Assert.IsFalse(withoutHard.CanCreate(hardP05));
            Assert.IsTrue(withoutHard.CanCreate(hardP01), "부채꼴은 정의의 사거리를 그대로 쓰므로 별도 데이터가 필요 없다");

            var withHard = new MucheolPatternFactory(TEST_HREF, comboSpec: new MucheolComboSpec(new MucheolComboHit(5f, 120f, 0.14f, 0f), new MucheolComboHit(5f, 70f, 0.24f, 0.5f)),
                hardComboSpec: MucheolDevCatalog.CreateHardComboSpec());
            Assert.IsTrue(withHard.CanCreate(hardP05));
            Assert.IsInstanceOf<MucheolComboPattern>(withHard.Create(hardP05));
        }

        [Test]
        public void VfxCatalog_HardIdsAreHandledAndScaledUpFromTheEasyCue()
        {
            string[] easyIds = new string[PhaseOneEasyIds.Length * 2];
            PhaseOneEasyIds.CopyTo(easyIds, 0);
            PhaseTwoEasyIds.CopyTo(easyIds, PhaseOneEasyIds.Length);

            for (int i = 0; i < easyIds.Length; ++i)
            {
                string hardId = BossPatternCatalogSO.ToMucheolHardId(easyIds[i]);
                Assert.IsTrue(MucheolVfxCatalog.Handles(hardId), hardId);
                Assert.IsTrue(MucheolMotionCatalog.Handles(hardId), hardId);
                Assert.AreEqual(MucheolVfxCatalog.CueCount(easyIds[i]), MucheolVfxCatalog.CueCount(hardId), hardId);

                Assert.IsTrue(MucheolVfxCatalog.TryGetCue(easyIds[i], 0, out MucheolVfxCue easyCue));
                Assert.IsTrue(MucheolVfxCatalog.TryGetCue(hardId, 0, out MucheolVfxCue hardCue));
                Assert.AreEqual(easyCue.Template, hardCue.Template, hardId);
                Assert.Greater(hardCue.Scale.sqrMagnitude, easyCue.Scale.sqrMagnitude, hardId);
            }

            Assert.IsTrue(MucheolVfxCatalog.TryGetCue(MucheolPatternFactory.P01_ID, 0, out MucheolVfxCue fan));
            Assert.IsTrue(MucheolVfxCatalog.TryGetCue("mucheol_hard_p01", 0, out MucheolVfxCue hardFan));
            Assert.AreEqual(fan.Scale.x * FAN_RANGE_SCALE, hardFan.Scale.x, TOLERANCE);
            Assert.AreEqual(fan.Scale.z * FAN_RANGE_SCALE, hardFan.Scale.z, TOLERANCE);
        }

        [Test]
        public void VfxCatalog_BattlefieldP07_IsNotScaled()
        {
            Assert.IsTrue(MucheolVfxCatalog.TryGetCue(MucheolPhase2PatternFactory.P07_ID, 0, out MucheolVfxCue cue));
            Assert.AreEqual(Vector3.one, cue.Scale);
        }
    }
}
