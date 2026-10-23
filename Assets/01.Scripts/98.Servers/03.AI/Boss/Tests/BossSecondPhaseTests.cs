// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using EPatternGroup     = Shinshi.Data.BossPatternCatalogSO.EPatternGroup;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 월백 2페이즈 전환과 선택 기반을 확인한다. 공격은 구현하지 않았으므로 가짜 패턴 팩토리를 쓰고, 에셋 표는 기획안 값을 옮겨 적은 아래 상수와 대조한다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 시각은 이진 분수(0.25 단위)로만 넘긴다
    public class BossSecondPhaseTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const float  TOLERANCE    = 0.001f;
        private const float  HP_ABOVE     = 0.6f;
        private const float  HP_LOW       = 0.2f;

        private const int NEAR = 0;
        private const int MID  = 1;
        private const int FAR  = 2;
        private const int FRONT = 0;
        private const int SIDE  = 1;
        private const int ONE   = 0;
        private const int THREE = 2;
        private const int RECENT_NONE = 3;

        private static readonly string[] Ids =
        {
            BossPatternCatalogSO.WOLBAEK_P2_P01_ID, BossPatternCatalogSO.WOLBAEK_P2_P02_ID, BossPatternCatalogSO.WOLBAEK_P2_P03_ID,
            BossPatternCatalogSO.WOLBAEK_P2_P04_ID, BossPatternCatalogSO.WOLBAEK_P2_P05_ID, BossPatternCatalogSO.WOLBAEK_P2_P06_ID
        };

        // 가중치 기획안 5~10장 표를 그대로 옮긴 값, 행 순서는 P01~P06
        private static readonly float[]   Base      = { 10f, 10f, 12f, 13f, 22f, 8f };
        private static readonly float[]   Enhanced  = { 12f, 12f, 14f, 15f, 18f, 9f };
        private static readonly float[][] Distance  =
        {
            new[] { 1.0f, 1.1f, 1.1f }, new[] { 1.0f, 1.1f, 1.1f }, new[] { 1.3f, 1.2f, 0.7f },
            new[] { 0.9f, 1.2f, 1.4f }, new[] { 0.5f, 1.4f, 1.7f }, new[] { 1.7f, 0.4f, 0f }
        };
        private static readonly float[][] Direction =
        {
            new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f },
            new[] { 1f, 1f, 1f }, new[] { 1.2f, 1.0f, 0.7f }, new[] { 1f, 1f, 1f }
        };
        private static readonly float[][] Nearby    =
        {
            new[] { 1.0f, 1.1f, 1.2f }, new[] { 1.0f, 1.1f, 1.2f }, new[] { 1.0f, 1.15f, 1.3f },
            new[] { 1.0f, 1.25f, 1.5f }, new[] { 1.2f, 0.9f, 0.6f }, new[] { 0.5f, 1.6f, 2.4f }
        };
        private static readonly float[] Recent = { 0f, 0.3f, 0.7f, 1f };

        private static readonly EPatternGroup[] Groups =
        {
            EPatternGroup.Large, EPatternGroup.Large, EPatternGroup.Large,
            EPatternGroup.Pressure, EPatternGroup.Basic, EPatternGroup.Pressure
        };

        // 12장과 13장, 체력 구간(초과, 이하) x 직전(Large, Pressure, Basic) x 다음(Large, Pressure, Basic)
        private static readonly float[] Flow =
        {
            0.25f, 1.25f, 1.6f,  1.15f, 0.6f, 1.2f,  1.3f, 1.1f, 1.0f,
            0.45f, 1.35f, 1.25f, 1.15f, 0.6f, 1.2f,  1.3f, 1.1f, 1.0f
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

        private static PatternDefinition AssetPattern(BossPatternCatalogSO catalog, int index)
        {
            Assert.IsTrue(catalog.TryGetPattern(Ids[index], out PatternDefinition pattern));
            return pattern;
        }

        private static void AssertTable(float[] expected, IReadOnlyList<float> actual, string label)
        {
            Assert.AreEqual(expected.Length, actual.Count, label);
            for (int i = 0; i < expected.Length; ++i)
            {
                Assert.AreEqual(expected[i], actual[i], TOLERANCE, $"{label}[{i}]");
            }
        }

        private static float AssetWeight(BossPatternCatalogSO catalog, int index, float hp, EPatternGroup previous,
            int distance, int direction, int nearby, int recent)
        {
            Assert.IsTrue(catalog.TryGetBoss(WOLBAEK_ID, out BossDefinition boss));
            return BossPatternSelector.ComputeSecondPhaseWeight(AssetPattern(catalog, index), boss, hp, previous, distance,
                direction, nearby, recent);
        }

        private static BossDefinition MakeBoss(float phaseRate = 0.5f)
        {
            return new BossDefinition(BossTestFixtures.BOSS_ID, 36f, 700f, 200f, phaseRate, 0f, 5f, 12f, 60f, 120f, 5f, 0.25f,
                1f, 2f, 0f, 0.25f, (float[])Flow.Clone());
        }

        private static PatternDefinition MakePhaseOne(string id)
        {
            return BossTestFixtures.MakePattern(id);
        }

        // 선택 데이터만 둔 2페이즈 패턴, 시간은 1페이즈 시험 패턴과 같은 가짜 값이다
        private static PatternDefinition MakePhaseTwo(string id, EPatternGroup group, float baseWeight, float enhancedWeight)
        {
            return new PatternDefinition(id, BossTestFixtures.BOSS_ID, 2, baseWeight,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, (float[])Recent.Clone(),
                1f, 0f, 0f, 0.5f, 0.5f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 1f, true,
                group: group, enhancedBaseWeight: enhancedWeight);
        }

        private sealed class ReadinessCatalog : IBossPatternCatalog, IBossPatternReadiness
        {
            public List<PatternDefinition> Patterns { get; } = new List<PatternDefinition>();
            public BossDefinition          Boss     { get; set; }
            public HashSet<string>         NotReady { get; } = new HashSet<string>();

            public bool TryGetBoss(string bossId, out BossDefinition boss)
            {
                boss = Boss;
                return boss != null;
            }

            public void CollectPatterns(string bossId, int phase, List<PatternDefinition> results)
            {
                results.Clear();
                for (int i = 0; i < Patterns.Count; ++i)
                {
                    if (Patterns[i].Phase == phase)
                    {
                        results.Add(Patterns[i]);
                    }
                }
            }

            public bool IsTuned(string bossId)
            {
                return true;
            }

            public bool IsPatternReady(string bossId, PatternDefinition pattern)
            {
                return !NotReady.Contains(pattern.DefinitionId);
            }
        }

        private sealed class GatedFactory : IBossPatternFactory, IBossPatternAvailability
        {
            public HashSet<string> Blocked { get; } = new HashSet<string>();
            public List<string>    Created { get; } = new List<string>();

            public bool CanCreate(PatternDefinition definition)
            {
                return !Blocked.Contains(definition.DefinitionId);
            }

            public IBossPattern Create(PatternDefinition definition)
            {
                if (Blocked.Contains(definition.DefinitionId))
                {
                    return null;
                }

                Created.Add(definition.DefinitionId);
                return new ScheduledHitPattern();
            }
        }

        private sealed class Rig
        {
            public FakeCombatTarget  Self    = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget  Player  = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public FakeTargetQuery   Query   = new FakeTargetQuery();
            public FakeDamageResolver Damage = new FakeDamageResolver();
            public FakeBossVitals    Vitals  = new FakeBossVitals();
            public FakeEffectLifetime Effects = new FakeEffectLifetime();
            public FakeBossRandom    Random  = new FakeBossRandom();
            public ReadinessCatalog  Catalog = new ReadinessCatalog();
            public GatedFactory      Factory = new GatedFactory();
            public BossBrain         Brain;

            public Rig(params PatternDefinition[] patterns)
            {
                Query.Players.Add(Player);
                Catalog.Boss = MakeBoss();
                Catalog.Patterns.AddRange(patterns);

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = Catalog,
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = Factory,
                    Effects  = Effects,
                    Control  = new FakeControlSource(),
                    Random   = Random
                });
            }

            // 1페이즈 공격 하나를 시작해 둔 채 체력 50%로 전환시키고 인계 대기에 둔다
            public void TransitionMidAttack()
            {
                Brain.Begin(0.0);
                Brain.Tick(0.0);
                Vitals.HealthRate = 0.5f;
                Brain.Tick(0.25);
            }
        }

        private static Rig MakeRig()
        {
            return new Rig(MakePhaseOne("p1"),
                MakePhaseTwo("large", EPatternGroup.Large, 10f, 12f),
                MakePhaseTwo("basic", EPatternGroup.Basic, 22f, 18f));
        }

        /****************************************
        *      Asset Values Equal The Plan
        ****************************************/

        [Test]
        public void Asset_SecondPhaseTables_EqualPlanTables()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            for (int i = 0; i < Ids.Length; ++i)
            {
                PatternDefinition pattern = AssetPattern(catalog, i);
                Assert.AreEqual(2, pattern.Phase, Ids[i]);
                Assert.AreEqual(WOLBAEK_ID, pattern.BossId, Ids[i]);
                Assert.AreEqual(Groups[i], pattern.Group, Ids[i] + " group");
                Assert.AreEqual(Base[i], pattern.BaseWeight, TOLERANCE, Ids[i] + " base");
                Assert.AreEqual(Enhanced[i], pattern.EnhancedBaseWeight, TOLERANCE, Ids[i] + " enhanced");
                AssertTable(Distance[i], pattern.DistanceMultipliers, Ids[i] + " distance");
                AssertTable(Direction[i], pattern.DirectionMultipliers, Ids[i] + " direction");
                AssertTable(Nearby[i], pattern.NearbyMultipliers, Ids[i] + " nearby");
                AssertTable(Recent, pattern.RecentUseMultipliers, Ids[i] + " recent");
            }
        }

        [Test]
        public void Asset_FlowTableAndEnhancedRate_EqualPlan()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            Assert.IsTrue(catalog.TryGetBoss(WOLBAEK_ID, out BossDefinition boss));

            Assert.AreEqual(0.25f, boss.EnhancedHealthRate, TOLERANCE);
            AssertTable(Flow, boss.FlowMultipliers, "flow");
        }

        [Test]
        public void Asset_PhaseOnePatternsAndIds_AreKeptAndDoNotOverlap()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            List<PatternDefinition> first = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, 1, first);

            Assert.AreEqual(6, first.Count);
            for (int i = 0; i < first.Count; ++i)
            {
                Assert.AreEqual(EPatternGroup.None, first[i].Group);
                Assert.IsFalse(System.Array.IndexOf(Ids, first[i].DefinitionId) >= 0, first[i].DefinitionId);
            }
        }

        // 기획안 14장 예시 A, 직전 Large, 중거리, 체력 60%. 기획안은 나머지 배율을 1로 가정하므로 방향 측면과 주변 1명 열을 쓰고 1이 아닌 열은 곱해서 비교한다
        [Test]
        public void SecondPhaseWeight_ExampleA_MatchesPlan()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.AreEqual(2.75f, AssetWeight(catalog, 1, HP_ABOVE, EPatternGroup.Large, MID, SIDE, ONE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(3.6f, AssetWeight(catalog, 2, HP_ABOVE, EPatternGroup.Large, MID, SIDE, ONE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(19.5f, AssetWeight(catalog, 3, HP_ABOVE, EPatternGroup.Large, MID, SIDE, ONE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(49.28f * Nearby[4][ONE], AssetWeight(catalog, 4, HP_ABOVE, EPatternGroup.Large, MID, SIDE, ONE, RECENT_NONE), TOLERANCE);
        }

        // 예시 B, 근거리, 주변 3명, 흐름 영향 1(첫 선택)
        [Test]
        public void SecondPhaseWeight_ExampleB_MatchesPlan()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.AreEqual(32.64f, AssetWeight(catalog, 5, HP_ABOVE, EPatternGroup.None, NEAR, FRONT, THREE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(6.6f, AssetWeight(catalog, 4, HP_ABOVE, EPatternGroup.None, NEAR, SIDE, THREE, RECENT_NONE), TOLERANCE);
        }

        // 예시 C, 체력 20%, 직전 Large, 중거리
        [Test]
        public void SecondPhaseWeight_ExampleC_MatchesPlan()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.AreEqual(5.94f, AssetWeight(catalog, 1, HP_LOW, EPatternGroup.Large, MID, SIDE, ONE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(24.3f, AssetWeight(catalog, 3, HP_LOW, EPatternGroup.Large, MID, SIDE, ONE, RECENT_NONE), TOLERANCE);
            Assert.AreEqual(31.5f * Nearby[4][ONE], AssetWeight(catalog, 4, HP_LOW, EPatternGroup.Large, MID, SIDE, ONE, RECENT_NONE), TOLERANCE);
        }

        [Test]
        public void SecondPhaseWeight_FirstSelection_FlowMultiplierIsOne()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            for (int i = 0; i < Ids.Length; ++i)
            {
                float expected = Base[i] * Distance[i][MID] * Direction[i][SIDE] * Nearby[i][ONE];
                Assert.AreEqual(expected, AssetWeight(catalog, i, HP_ABOVE, EPatternGroup.None, MID, SIDE, ONE, RECENT_NONE), TOLERANCE, Ids[i]);
            }
        }

        [Test]
        public void SecondPhaseWeight_RecentUse_UsesZeroPointThreeSevenOne()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            float none = AssetWeight(catalog, 4, HP_ABOVE, EPatternGroup.None, MID, SIDE, ONE, RECENT_NONE);

            Assert.AreEqual(0f, AssetWeight(catalog, 4, HP_ABOVE, EPatternGroup.None, MID, SIDE, ONE, 0), TOLERANCE);
            Assert.AreEqual(none * 0.3f, AssetWeight(catalog, 4, HP_ABOVE, EPatternGroup.None, MID, SIDE, ONE, 1), TOLERANCE);
            Assert.AreEqual(none * 0.7f, AssetWeight(catalog, 4, HP_ABOVE, EPatternGroup.None, MID, SIDE, ONE, 2), TOLERANCE);
        }

        [Test]
        public void SecondPhaseWeight_FlowTable_EveryCellMatchesPlan()
        {
            Assert.IsTrue(LoadCatalog().TryGetBoss(WOLBAEK_ID, out BossDefinition boss));

            for (int band = 0; band < 2; ++band)
            {
                for (int previous = 1; previous <= 3; ++previous)
                {
                    for (int next = 1; next <= 3; ++next)
                    {
                        float expected = Flow[(((band * 3) + (previous - 1)) * 3) + (next - 1)];
                        Assert.AreEqual(expected, boss.GetFlowMultiplier(band == 1, (EPatternGroup)previous, (EPatternGroup)next), TOLERANCE);
                    }
                }
            }
        }

        [Test]
        public void SecondPhaseWeight_Boundary25Percent_IsEnhanced()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            float atBoundary = AssetWeight(catalog, 4, 0.25f, EPatternGroup.None, MID, SIDE, ONE, RECENT_NONE);
            float justAbove  = AssetWeight(catalog, 4, 0.2501f, EPatternGroup.None, MID, SIDE, ONE, RECENT_NONE);

            Assert.AreEqual(Enhanced[4] * Distance[4][MID] * Nearby[4][ONE], atBoundary, TOLERANCE);
            Assert.AreEqual(Base[4] * Distance[4][MID] * Nearby[4][ONE], justAbove, TOLERANCE);
        }

        [Test]
        public void SecondPhaseWeight_Selection_IsProportionalNotFixedToHighest()
        {
            List<float> weights = new List<float> { 10f, 30f, 60f };

            Assert.AreEqual(0, BossPatternSelector.Pick(weights, 0.05f));
            Assert.AreEqual(1, BossPatternSelector.Pick(weights, 0.2f));
            Assert.AreEqual(2, BossPatternSelector.Pick(weights, 0.9f));
        }

        /****************************************
        *        Tuning And Readiness Data
        ****************************************/

        // 1페이즈 시작 검사는 2페이즈 데이터가 들어와도 같은 기준이다. 공격을 구현하고 수치를 넣은 P03~P06만 준비됐고 나머지 둘은 수치가 비어 준비되지 않은 것으로 남는다
        [Test]
        public void Asset_PhaseOneStaysTuned_AndOnlyImplementedPhaseTwoPatternsAreReady()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID));
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, 2), "여섯 패턴 모두 실행 수치가 들어 있다");
            for (int i = 0; i < Ids.Length; ++i)
            {
                // 여섯 패턴 모두 실행 수치가 들어 있다(P01 별형 빔은 시작안이며 테스트로 조정한다)
                Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, AssetPattern(catalog, i)), Ids[i]);
            }

            List<PatternDefinition> first = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, 1, first);
            for (int i = 0; i < first.Count; ++i)
            {
                Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, first[i]), first[i].DefinitionId);
            }
        }

        [Test]
        public void Asset_SecondPhaseValidation_PassesNowThatAllSixPatternsHaveExecutionValues()
        {
            List<string> errors = new List<string>();

            LoadCatalog().ValidateTuning(WOLBAEK_ID, 2, errors);

            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        }

        [Test]
        public void Factory_SecondPhaseIds_OnlyImplementedPatternsAreCreatable()
        {
            // 투사체와 장판 패턴은 발사 창구와 장판 창구가 있어야 만들 수 있어 지속물 관리자를 넘긴다
            BossProjectileManager projectiles = new BossProjectileManager(new FakeTargetQuery());
            BossAreaManager areas = new BossAreaManager(new FakeTargetQuery());
            WolbaekPatternFactory factory = new WolbaekPatternFactory(1000f, projectiles: projectiles, areas: areas);

            for (int i = 0; i < Ids.Length; ++i)
            {
                PatternDefinition pattern = AssetPattern(LoadCatalog(), i);
                bool implemented = i >= 2;
                Assert.AreEqual(implemented, factory.CanCreate(pattern), Ids[i]);
                Assert.AreEqual(implemented, factory.Create(pattern) != null, Ids[i]);
            }
        }

        /****************************************
        *         Transition And Handoff
        ****************************************/

        [Test]
        public void Tick_JustAboveBoundary_DoesNotTransition()
        {
            Rig r = MakeRig();
            r.Brain.Begin(0.0);

            r.Vitals.HealthRate = 0.5001f;
            r.Brain.Tick(0.25);

            Assert.AreEqual(1, r.Brain.Phase);
            Assert.AreEqual(EBossPhaseState.Active, r.Brain.PhaseState);
        }

        [Test]
        public void Tick_AtBoundary_TransitionsAndReportsPhaseTwo()
        {
            Rig r = MakeRig();

            r.TransitionMidAttack();

            Assert.AreEqual(2, r.Brain.Phase);
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, r.Brain.PhaseState);
        }

        [Test]
        public void Tick_Transition_CleansAttackAndEffectsOnce()
        {
            Rig r = MakeRig();

            r.TransitionMidAttack();

            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState);
            Assert.AreEqual(1, r.Effects.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.PhaseTransition, r.Effects.Cancels[0].Key);
            Assert.AreEqual(0, r.Damage.Requests.Count);
        }

        [Test]
        public void Tick_AfterTransition_DoesNotTransitionAgainAsHealthFalls()
        {
            Rig r = MakeRig();
            r.TransitionMidAttack();
            r.Brain.CompleteHandoff();

            r.Vitals.HealthRate = 0.2f;
            for (double t = 0.5; t < 10.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(2, r.Brain.Phase);
            Assert.AreEqual(1, r.Effects.Cancels.Count, "전환 정리는 한 번뿐이다");
        }

        [Test]
        public void CompleteHandoff_BeforeTransitionOrTwice_IsRejected()
        {
            Rig r = MakeRig();
            r.Brain.Begin(0.0);

            Assert.IsFalse(r.Brain.CompleteHandoff());

            r.TransitionMidAttack();
            Assert.IsTrue(r.Brain.CompleteHandoff());
            Assert.IsFalse(r.Brain.CompleteHandoff());
        }

        [Test]
        public void Tick_AwaitingHandoff_NeverSelectsUntilCompleted()
        {
            Rig r = MakeRig();
            r.TransitionMidAttack();
            int before = r.Factory.Created.Count;

            for (double t = 0.5; t < 10.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(before, r.Factory.Created.Count);
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, r.Brain.PhaseState);
        }

        /****************************************
        *       History Reset And First Flow
        ****************************************/

        [Test]
        public void Blackboard_ResetHistory_ClearsRecentAndGroup()
        {
            BossBlackboard board = new BossBlackboard();
            board.RecordUse("large", EPatternGroup.Large);
            Assert.AreEqual(0, board.GetRecentSlot("large"));
            Assert.AreEqual(EPatternGroup.Large, board.LastGroup);

            board.ResetHistory();

            Assert.AreEqual(BossBlackboard.RecentSlotNone, board.GetRecentSlot("large"));
            Assert.AreEqual(EPatternGroup.None, board.LastGroup);
        }

        // 1페이즈에서 쓴 패턴이 전환 뒤 기록에 남아 있으면 같은 ID는 직전 x0이 되지만, 초기화됐으므로 2페이즈 첫 선택에서 그대로 후보다
        [Test]
        public void Tick_AfterTransition_FirstSelectionIgnoresPhaseOneHistoryAndFlow()
        {
            Rig r = new Rig(MakePhaseOne("shared"), MakePhaseTwo("shared", EPatternGroup.Large, 10f, 12f));
            r.TransitionMidAttack();
            r.Brain.CompleteHandoff();
            r.Random.Roll = 0f;

            r.Brain.Tick(0.5);

            Assert.AreEqual("shared", r.Brain.ActivePatternId);
            Assert.AreEqual(2, r.Brain.Phase);
        }

        [Test]
        public void Tick_SecondPhase_UsesRecentExclusionAndFlowAfterFirstUse()
        {
            Rig r = MakeRig();
            r.TransitionMidAttack();
            r.Brain.CompleteHandoff();

            // 첫 선택은 흐름 1이라 두 후보의 기본 가중치 10, 22 비율이다. 0.1은 앞쪽(large)을 뽑는다
            r.Random.Roll = 0.1f;
            r.Brain.Tick(0.5);
            Assert.AreEqual("large", r.Brain.ActivePatternId);

            // 예고 1초, 발동 0.5초, 회복 0.5초 뒤 다음 선택, 직전 large는 x0이라 남은 후보는 basic뿐이다
            for (double t = 0.75; t <= 3.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual("basic", r.Brain.ActivePatternId);
        }

        /****************************************
        *      Not Ready, No Null Error Loop
        ****************************************/

        [Test]
        public void CompleteHandoff_NoExecutablePattern_WaitsWithoutSelectingOrLogging()
        {
            Rig r = MakeRig();
            r.Factory.Blocked.Add("large");
            r.Factory.Blocked.Add("basic");
            r.TransitionMidAttack();

            Assert.IsTrue(r.Brain.CompleteHandoff());
            Assert.AreEqual(EBossPhaseState.AwaitingReadiness, r.Brain.PhaseState);

            int before = r.Factory.Created.Count;
            for (double t = 0.5; t < 20.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(before, r.Factory.Created.Count);
            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState);
            Assert.AreEqual(EBossPhaseState.AwaitingReadiness, r.Brain.PhaseState);
            Assert.AreEqual(EBossFlowState.Combat, r.Brain.FlowState);
        }

        [Test]
        public void Tick_AwaitingReadiness_ResumesWhenDataOrImplementationBecomesReady()
        {
            Rig r = MakeRig();
            r.Factory.Blocked.Add("large");
            r.Catalog.NotReady.Add("basic");
            r.TransitionMidAttack();
            r.Brain.CompleteHandoff();
            for (double t = 0.5; t < 3.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(EBossPhaseState.AwaitingReadiness, r.Brain.PhaseState);

            r.Catalog.NotReady.Remove("basic");
            r.Random.Roll = 0f;
            r.Brain.Tick(3.5);
            r.Brain.Tick(3.75);

            Assert.AreEqual(EBossPhaseState.Active, r.Brain.PhaseState);
            Assert.AreEqual("basic", r.Brain.ActivePatternId);
        }

        [Test]
        public void Tick_SecondPhase_NotReadyPatternIsNeverCreated()
        {
            Rig r = MakeRig();
            r.Factory.Blocked.Add("large");
            r.TransitionMidAttack();
            r.Brain.CompleteHandoff();
            r.Random.Roll = 0f;

            r.Brain.Tick(0.5);

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual("basic", r.Brain.ActivePatternId);
            Assert.IsFalse(r.Factory.Created.Contains("large"));
        }

        /****************************************
        *        In-Progress Attack Is Stable
        ****************************************/

        [Test]
        public void Tick_HealthCrosses25DuringAttack_KeepsPatternAndTiming()
        {
            Rig r = MakeRig();
            r.TransitionMidAttack();
            r.Brain.CompleteHandoff();
            r.Random.Roll = 0.1f;
            r.Brain.Tick(0.5);
            Assert.AreEqual("large", r.Brain.ActivePatternId);
            Assert.AreEqual(EBossAttackState.Telegraph, r.Brain.AttackState);

            r.Vitals.HealthRate = HP_LOW;
            r.Brain.Tick(1.0);
            Assert.AreEqual(EBossAttackState.Telegraph, r.Brain.AttackState);
            Assert.AreEqual("large", r.Brain.ActivePatternId);

            r.Brain.Tick(1.5);
            Assert.AreEqual(EBossAttackState.Execute, r.Brain.AttackState, "예고 1초는 체력 변화와 무관하다");
            Assert.AreEqual(2, r.Brain.Phase);
        }

        [Test]
        public void Tick_NextSelectionAfterCrossing_UsesEnhancedWeights()
        {
            Rig r = MakeRig();
            r.TransitionMidAttack();
            r.Brain.CompleteHandoff();
            r.Random.Roll = 0.1f;
            r.Brain.Tick(0.5);
            r.Vitals.HealthRate = 0.25f;

            // 직전 large(x0)라 후보는 basic뿐이지만, 강화 구간 가중치(18)로 계산돼 0이 아니므로 선택된다
            for (double t = 0.75; t <= 3.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual("basic", r.Brain.ActivePatternId);
        }

        /****************************************
        *       Death And Combat End Priority
        ****************************************/

        [Test]
        public void Tick_DeathAndTransitionSameTick_DeathWinsWithoutTransition()
        {
            FakeDeathPolicy death = new FakeDeathPolicy { Confirm = true };
            Rig r = MakeRig();
            r.Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
            {
                Catalog = r.Catalog, Self = r.Self, Vitals = r.Vitals, Targets = r.Query, Damage = r.Damage,
                Patterns = r.Factory, Effects = r.Effects, Control = new FakeControlSource(), Random = r.Random,
                DeathPolicy = death
            });
            r.Brain.Begin(0.0);
            r.Brain.Tick(0.0);

            r.Vitals.HealthRate = 0f;
            r.Vitals.IsHealthZero = true;
            r.Brain.Tick(0.25);

            Assert.AreEqual(EBossFlowState.Defeated, r.Brain.FlowState);
            Assert.AreEqual(1, r.Brain.Phase, "사망이 전환보다 먼저다");
            Assert.AreEqual(EBossInterruptReason.Death, r.Effects.Cancels[r.Effects.Cancels.Count - 1].Key);
        }

        [Test]
        public void Tick_CombatEndAndTransitionSameTick_CombatEndWins()
        {
            Rig r = MakeRig();
            r.Brain.Begin(0.0);
            r.Brain.Tick(0.0);

            r.Vitals.HealthRate = 0.4f;
            r.Brain.RequestCombatEnd();
            r.Brain.Tick(0.25);

            Assert.AreEqual(EBossFlowState.Ended, r.Brain.FlowState);
            Assert.AreEqual(1, r.Brain.Phase);
        }

        [Test]
        public void CompleteHandoff_AfterCombatEnd_IsRejected()
        {
            Rig r = MakeRig();
            r.TransitionMidAttack();

            r.Brain.RequestCombatEnd();
            r.Brain.Tick(0.5);

            Assert.IsFalse(r.Brain.CompleteHandoff());
            Assert.AreEqual(EBossFlowState.Ended, r.Brain.FlowState);
        }
    }
}
