// System
using System;
using System.Collections.Generic;
using System.Reflection;

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
    // 월백 하드 2페이즈 P07(후방 우선 워프 후 충격파)을 에셋의 실제 정의와 실제 팩토리, 실제 공격 FSM, 실제 두뇌로 확인한다
    // 하드 P07은 이지 P07과 같은 BossWarpBlastPattern 실행이고 선택 가중치와 최근 사용 배율만 정의 값이 다르다
    // 워프 창구(IBossWarp), 전장 입력, 충격파 알림은 가짜이며 실제 서버 몸체의 순간 이동, 충돌, 경로 검사와 복제, 화면은 거치지 않는다
    // 수치는 Play Mode와 다인 접속 검증 전의 초기값이라 값 자체의 정답을 보장하지 않는다
    //
    // 시각(시전 시작 기준): 예고 1.5초 끝에 한 번 타격하고, 회복 1초 뒤 t=2.5에 다음 선택이 열린다. 시각은 이진 분수로만 넘긴다
    public class BossWolbaekHardPhaseTwoWarpTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string HARD_ID      = BossPatternCatalogSO.WOLBAEK_HARD_P2_P07_ID;
        private const string EASY_ID      = BossPatternCatalogSO.WOLBAEK_P2_P07_ID;
        private const float  HREF         = 1000f;
        private const float  TOLERANCE    = 0.001f;
        private const float  ARENA_RADIUS = 18f;
        private const float  RADIUS       = 5f;
        private const float  DAMAGE_REF   = 0.24f;
        private const int    MAX_CANDIDATES = 8;
        private const double TELEGRAPH    = 1.5;
        private const double COMPLETE     = 2.5;

        private static readonly Vector3 Origin     = Vector3.zero;
        private static readonly Vector3 TargetSpot = new Vector3(0f, 0f, 8f);

        private static readonly string[] HardOthers =
        {
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID, BossPatternCatalogSO.WOLBAEK_HARD_P2_P02_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P03_ID, BossPatternCatalogSO.WOLBAEK_HARD_P2_P04_ID,
            BossPatternCatalogSO.WOLBAEK_HARD_P2_P05_ID, BossPatternCatalogSO.WOLBAEK_HARD_P2_P06_ID
        };

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class PlayersQuery : ICombatTargetQuery
        {
            public List<ICombatTarget> Targets { get; } = new List<ICombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if ((Targets[i].LifeState == EParticipantLifeState.Alive) && (!Targets[i].IsSummon))
                    {
                        results.Add(Targets[i]);
                    }
                }

                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    ICombatTarget target = Targets[i];
                    if ((target.LifeState != EParticipantLifeState.Alive) || (target.Faction != faction)
                        || (target.IsSummon && (!includeSummons)))
                    {
                        continue;
                    }

                    Vector3 offset = target.Position - center;
                    offset.y = 0f;
                    if (offset.magnitude <= radius)
                    {
                        results.Add(target);
                    }
                }

                return results.Count;
            }

            public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
            {
                return GetLivingInRadius(center, radius, faction, includeSummons, new List<ICombatTarget>());
            }

            public bool TryGet(int combatId, out ICombatTarget target)
            {
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if (Targets[i].CombatId == combatId)
                    {
                        target = Targets[i];
                        return true;
                    }
                }

                target = null;
                return false;
            }
        }

        private sealed class FakeArena : IBossArenaSource
        {
            public bool    IsAvailable = true;
            public Vector3 Center      = Origin;
            public float   Radius      = ARENA_RADIUS;

            public bool TryGetArena(out Vector3 center, out float radius)
            {
                center = Center;
                radius = Radius;
                return IsAvailable;
            }
        }

        // 서버 몸체 대역, 성공하면 보스 대역의 위치를 바꾼다. 호출은 모두 기록해 검사 순서와 횟수를 본다
        private sealed class FakeWarp : IBossWarp
        {
            public FakeCombatTarget    Body;
            public List<string>        Log;
            public Func<Vector3, bool> CanStand = point => true;
            public Func<Vector3, bool> HasWalk  = point => true;
            public Func<Vector3, bool> Accepts  = point => true;
            public Vector3             SnapOffset = Vector3.zero;

            public readonly List<Vector3> StandCalls = new List<Vector3>();
            public readonly List<Vector3> PathCalls  = new List<Vector3>();
            public readonly List<Vector3> WarpCalls  = new List<Vector3>();

            public int TotalCalls => StandCalls.Count + PathCalls.Count + WarpCalls.Count;

            public bool CanStandAt(Vector3 point)
            {
                StandCalls.Add(point);
                Log?.Add("stand");
                return CanStand(point);
            }

            public bool HasPath(Vector3 from, Vector3 to)
            {
                PathCalls.Add(from);
                Log?.Add("path");
                return HasWalk(from);
            }

            public bool TryWarp(Vector3 point, out Vector3 landed)
            {
                WarpCalls.Add(point);
                if (!Accepts(point))
                {
                    landed = Body.Position;
                    return false;
                }

                landed        = point + SnapOffset;
                Body.Position = landed;
                Log?.Add("warp");
                return true;
            }
        }

        private sealed class RecordingListener : IBossCircleTelegraphListener
        {
            public List<string> Log;
            public int          StartedCount;
            public int          HitCalls;
            public int          CancelCalls;
            public int          LastHitCount;
            public Vector3      Center;
            public float        Radius;
            public float        Seconds;

            public void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds)
            {
                ++StartedCount;
                Center  = center;
                Radius  = radius;
                Seconds = telegraphSeconds;
                Log?.Add("telegraph");
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                ++HitCalls;
                LastHitCount = hitCount;
                Log?.Add("hit");
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                ++CancelCalls;
                Log?.Add("cancel");
            }
        }

        // 공격 FSM과 실제 정의, 실제 패턴 객체를 가짜 대상 조회와 가짜 피해 처리기에 이어 붙인 실행 단위다
        private sealed class Run
        {
            public FakeCombatTarget     Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public FakeCombatTarget     Target;
            public PlayersQuery         Query    = new PlayersQuery();
            public FakeDamageResolver   Resolver = new FakeDamageResolver();
            public BossDamageGate       Gate;
            public RecordingListener    Listener = new RecordingListener();
            public FakeWarp             Warp     = new FakeWarp();
            public FakeArena            Arena    = new FakeArena();
            public BossAttackFsm        Fsm      = new BossAttackFsm();
            public PatternDefinition    Definition;
            public BossWarpBlastPattern Pattern;
            public List<string>         Log      = new List<string>();

            public Run(PatternDefinition definition = null, Vector3? targetPosition = null, Vector3? targetForward = null)
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition     = definition ?? LoadDefinition(HARD_ID);
                Warp.Body      = Self;
                Warp.Log       = Log;
                Listener.Log   = Log;
                Target         = AddPlayer(10, targetPosition ?? TargetSpot);
                Target.Forward = targetForward ?? Vector3.forward;

                // 하드 P07도 팩토리가 만든 실행 객체를 쓴다, 이지와 같은 클래스여야 한다
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, circleListener: Listener, arena: Arena, warp: Warp);
                Assert.IsTrue(factory.CanCreate(Definition));
                Pattern = factory.Create(Definition) as BossWarpBlastPattern;
                Assert.IsNotNull(Pattern);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player,
                    Position = position, IsSummon = isSummon };
                Query.Targets.Add(player);
                return player;
            }

            public void Start(int selectedTargetId = 10, double startTime = 0.0)
            {
                BossPatternContext context = new BossPatternContext(Definition, 100, selectedTargetId, Self, Query, Gate);
                Fsm.Start(Definition, Pattern, in context, startTime);
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; ++i)
            {
                UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        private static BossPatternCatalogSO LoadCatalog()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);
            return catalog;
        }

        private static PatternDefinition LoadDefinition(string id)
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern(id, out PatternDefinition definition), id);
            return definition;
        }

        // 하드 에셋 정의와 같은 값에서 워프와 형태 수치만 바꾼 정의, 계약 검사와 후보 수 상한 검사에 쓴다
        private static PatternDefinition MakeHard(int count = MAX_CANDIDATES, float landing = 3f, float minDistance = 5f,
            EEnemyAttackShape shape = EEnemyAttackShape.Circle, bool guardable = true, float active = 0f)
        {
            EnemyCatalogSO.AttackShapeSpec spec = (shape == EEnemyAttackShape.Circle)
                ? EnemyCatalogSO.AttackShapeSpec.Circle(RADIUS)
                : EnemyCatalogSO.AttackShapeSpec.Fan(RADIUS, 90f);

            return new PatternDefinition(HARD_ID, WOLBAEK_ID, 2, 8f,
                new[] { 0f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 0.25f, 0.3f, 0.7f, 1f },
                1.5f, 0f, 0f, active, 1f, spec, DAMAGE_REF, guardable,
                group: EPatternGroup.Pressure, enhancedBaseWeight: 8.5f, difficulty: EBossDifficulty.Hard,
                warpMinTargetDistance: minDistance, warpLandingDistance: landing, warpCandidateCount: count);
        }

        private static bool HasTarget(FakeDamageResolver resolver, int targetId)
        {
            for (int i = 0; i < resolver.Requests.Count; ++i)
            {
                if (resolver.Requests[i].HitTargetId == targetId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, string message = null)
        {
            Assert.AreEqual(expected.x, actual.x, TOLERANCE, message);
            Assert.AreEqual(expected.y, actual.y, TOLERANCE, message);
            Assert.AreEqual(expected.z, actual.z, TOLERANCE, message);
        }

        private BossPatternCatalogSO CopyWithPatterns(Func<PatternDefinition, PatternDefinition> map)
        {
            BossPatternCatalogSO copy = UnityEngine.Object.Instantiate(LoadCatalog());
            _created.Add(copy);

            List<PatternDefinition> patterns = new List<PatternDefinition>();
            for (int i = 0; i < copy.Patterns.Count; ++i)
            {
                PatternDefinition mapped = map(copy.Patterns[i]);
                if (mapped != null)
                {
                    patterns.Add(mapped);
                }
            }

            const BindingFlags FLAGS = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(BossPatternCatalogSO).GetField("_patterns", FLAGS).SetValue(copy, patterns.ToArray());
            typeof(BossPatternCatalogSO).GetMethod("InvalidateLookups", FLAGS).Invoke(copy, null);
            return copy;
        }

        /****************************************
        *       Asset: Values And Selection
        ****************************************/

        [Test]
        public void Asset_HardP07_HasDesignValuesAndAHardOnlyId()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);

            Assert.AreEqual("wolbaek_hard_ph2_p07", p.DefinitionId);
            Assert.AreNotEqual(EASY_ID, p.DefinitionId);
            Assert.AreEqual(HARD_ID + ".name", p.DisplayKey);
            Assert.AreEqual(WOLBAEK_ID, p.BossId);
            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EBossDifficulty.Hard, p.Difficulty);
            Assert.AreEqual(EPatternGroup.Pressure, p.Group);
            Assert.AreEqual(1.5f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0f, p.ActiveSeconds);
            Assert.AreEqual(1f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Circle, p.Shape.Shape);
            Assert.AreEqual(RADIUS, p.Shape.Range, TOLERANCE);
            Assert.AreEqual(DAMAGE_REF, p.DamageRef, TOLERANCE);
            Assert.IsTrue(p.Guardable, "반격 가능");
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(5f, p.WarpMinTargetDistance, TOLERANCE);
            Assert.AreEqual(3f, p.WarpLandingDistance, TOLERANCE);
            Assert.AreEqual(MAX_CANDIDATES, p.WarpCandidateCount);
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_HardRecent, p.RecentUseMultipliers, "recent");
            BossWolbaekHardDataKit.AssertTable(new[] { 0f, 1f, 1f }, p.DistanceMultipliers, "distance");
            BossWolbaekHardDataKit.AssertTable(new[] { 1f, 1f, 1f }, p.DirectionMultipliers, "direction");
            BossWolbaekHardDataKit.AssertTable(new[] { 1f, 1f, 1f }, p.NearbyMultipliers, "nearby");
        }

        [Test]
        public void Asset_HardP07_ExecutionValuesMatchEasyAndOnlySelectionTuningDiffers()
        {
            PatternDefinition easy = LoadDefinition(EASY_ID);
            PatternDefinition hard = LoadDefinition(HARD_ID);

            Assert.AreEqual(easy.TelegraphSeconds, hard.TelegraphSeconds);
            Assert.AreEqual(easy.ActiveSeconds, hard.ActiveSeconds);
            Assert.AreEqual(easy.RecoverySeconds, hard.RecoverySeconds);
            Assert.AreEqual(easy.Shape.Shape, hard.Shape.Shape);
            Assert.AreEqual(easy.Shape.Range, hard.Shape.Range);
            Assert.AreEqual(easy.DamageRef, hard.DamageRef);
            Assert.AreEqual(easy.Guardable, hard.Guardable);
            Assert.AreEqual(easy.Group, hard.Group);
            Assert.AreEqual(easy.WarpMinTargetDistance, hard.WarpMinTargetDistance);
            Assert.AreEqual(easy.WarpLandingDistance, hard.WarpLandingDistance);
            Assert.AreEqual(easy.WarpCandidateCount, hard.WarpCandidateCount);
            BossWolbaekHardDataKit.AssertSameTable(easy.DistanceMultipliers, hard.DistanceMultipliers, "distance");
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_EasyRecent, easy.RecentUseMultipliers, "easy recent");
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_HardRecent, hard.RecentUseMultipliers, "hard recent");
        }

        [Test]
        public void Asset_HardP07_NearDistanceBoundaryMatchesMinimumTargetDistance()
        {
            Assert.IsTrue(LoadCatalog().TryGetBoss(WOLBAEK_ID, out BossDefinition boss));
            PatternDefinition p = LoadDefinition(HARD_ID);

            Assert.AreEqual(p.WarpMinTargetDistance, boss.NearDistanceMax, TOLERANCE, "근거리 배율 0과 최소 거리 5m가 같은 경계를 가리킨다");
            Assert.AreEqual(0f, p.DistanceMultipliers[0]);
        }

        // 설계 목표는 같은 조건에 적격(가중치가 양수)인 하드 P01~P06 평균 빈도의 약 절반. 실제 거리 배율과 방향, 주변 배율을 쓴 선택기 값으로 본다
        // 방향과 주변은 중간 구간, 직전 그룹은 없음, 최근 사용은 3회 안에 없음으로 둔다
        [TestCase(0.6f, 1)]
        [TestCase(0.6f, 2)]
        [TestCase(0.25f, 1)]
        [TestCase(0.25f, 2)]
        public void Asset_HardP07_SelectionWeightIsAboutHalfOfTheEligibleHardP01ToP06Average(float healthRate, int distanceBand)
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            Assert.IsTrue(catalog.TryGetBoss(WOLBAEK_ID, out BossDefinition boss));

            float sum   = 0f;
            int   count = 0;
            for (int i = 0; i < HardOthers.Length; ++i)
            {
                float weight = BossPatternSelector.ComputeSecondPhaseWeight(LoadDefinition(HardOthers[i]), boss, healthRate,
                    EPatternGroup.None, distanceBand, 1, 1, 3);
                if (weight > 0f)
                {
                    sum += weight;
                    ++count;
                }
            }

            float p07 = BossPatternSelector.ComputeSecondPhaseWeight(LoadDefinition(HARD_ID), boss, healthRate,
                EPatternGroup.None, distanceBand, 1, 1, 3);

            Assert.GreaterOrEqual(count, 5);
            float ratio = p07 / (sum / count);
            Assert.That(ratio, Is.InRange(0.4f, 0.6f), $"health {healthRate}, distance band {distanceBand}, ratio {ratio}");
        }

        [Test]
        public void Asset_HardP07_NearIsZeroAndRecentUseFollowsTheHardTable()
        {
            Assert.IsTrue(LoadCatalog().TryGetBoss(WOLBAEK_ID, out BossDefinition boss));
            PatternDefinition p = LoadDefinition(HARD_ID);

            Assert.AreEqual(0f, BossPatternSelector.ComputeSecondPhaseWeight(p, boss, 0.6f, EPatternGroup.None, 0, 1, 1, 3), TOLERANCE);
            Assert.AreEqual(8f * 0.25f, BossPatternSelector.ComputeSecondPhaseWeight(p, boss, 0.6f, EPatternGroup.None, 1, 1, 1, 0), TOLERANCE);
            Assert.AreEqual(8f * 0.3f, BossPatternSelector.ComputeSecondPhaseWeight(p, boss, 0.6f, EPatternGroup.None, 1, 1, 1, 1), TOLERANCE);
            Assert.AreEqual(8f * 0.7f, BossPatternSelector.ComputeSecondPhaseWeight(p, boss, 0.6f, EPatternGroup.None, 1, 1, 1, 2), TOLERANCE);
            Assert.AreEqual(8f, BossPatternSelector.ComputeSecondPhaseWeight(p, boss, 0.6f, EPatternGroup.None, 2, 1, 1, 3), TOLERANCE);
            Assert.AreEqual(8.5f, BossPatternSelector.ComputeSecondPhaseWeight(p, boss, 0.25f, EPatternGroup.None, 1, 1, 1, 3), TOLERANCE,
                "체력 25% 이하는 강화 기본값을 쓴다");
        }

        [Test]
        public void Asset_EasyP07AndHardP01ToP06_AreUnchanged()
        {
            PatternDefinition easy = LoadDefinition(EASY_ID);
            Assert.AreEqual(EBossDifficulty.Easy, easy.Difficulty);
            Assert.AreEqual(6.5f, easy.BaseWeight, TOLERANCE);
            Assert.AreEqual(7f, easy.EnhancedBaseWeight, TOLERANCE);
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_EasyRecent, easy.RecentUseMultipliers, "easy recent");

            float[] baseWeights     = { 10f, 10f, 12f, 16f, 22f, 8f };
            float[] enhancedWeights = { 12f, 12f, 14f, 18f, 18f, 9f };
            for (int i = 0; i < HardOthers.Length; ++i)
            {
                PatternDefinition p = LoadDefinition(HardOthers[i]);
                Assert.AreEqual(baseWeights[i], p.BaseWeight, TOLERANCE, HardOthers[i]);
                Assert.AreEqual(enhancedWeights[i], p.EnhancedBaseWeight, TOLERANCE, HardOthers[i]);
                Assert.AreEqual(0, p.WarpCandidateCount, HardOthers[i]);
            }
        }

        [Test]
        public void Asset_Catalog_StaysValid_AndHardPhaseTwoOpensOnlyWithAllSeven()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            var errors = new List<string>();
            catalog.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, 2, EBossDifficulty.Hard));
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, 2), "이지 2페이즈 준비 결과는 그대로다");
            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, LoadDefinition(HARD_ID)));

            // 일곱 개 검사는 풀리지 않았다, P07을 빼면 6 != 7로 다시 닫힌다
            BossPatternCatalogSO sixOnly = CopyWithPatterns(p => (p.DefinitionId == HARD_ID) ? null : p);
            var sixErrors = new List<string>();
            sixOnly.ValidateTuning(WOLBAEK_ID, 2, EBossDifficulty.Hard, sixErrors);
            Assert.IsFalse(sixOnly.IsPhaseTuned(WOLBAEK_ID, 2, EBossDifficulty.Hard));
            Assert.IsTrue(sixErrors.Exists(e => e.Contains("6 != 7")), string.Join("\n", sixErrors));
        }

        /****************************************
        *            Catalog Validation
        ****************************************/

        [Test]
        public void Validate_ValidHardWarpDefinition_Passes()
        {
            var errors = new List<string>();

            CopyWithPatterns(p => (p.DefinitionId == HARD_ID) ? MakeHard() : p).ValidateTuning(WOLBAEK_ID, 2, EBossDifficulty.Hard, errors);

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [TestCase(0, 3f, 5f, EEnemyAttackShape.Circle, true, 0f)]
        [TestCase(9, 3f, 5f, EEnemyAttackShape.Circle, true, 0f)]
        [TestCase(8, 0f, 5f, EEnemyAttackShape.Circle, true, 0f)]
        [TestCase(8, 18f, 5f, EEnemyAttackShape.Circle, true, 0f)]
        [TestCase(8, 3f, 0f, EEnemyAttackShape.Circle, true, 0f)]
        [TestCase(8, 3f, 5f, EEnemyAttackShape.Fan, true, 0f)]
        [TestCase(8, 3f, 5f, EEnemyAttackShape.Circle, false, 0f)]
        [TestCase(8, 3f, 5f, EEnemyAttackShape.Circle, true, 0.5f)]
        public void Validate_BrokenHardWarpContract_IsRejected(int count, float landing, float minDistance, EEnemyAttackShape shape,
            bool guardable, float active)
        {
            var errors = new List<string>();

            CopyWithPatterns(p => (p.DefinitionId == HARD_ID) ? MakeHard(count, landing, minDistance, shape, guardable, active) : p)
                .ValidateTuning(WOLBAEK_ID, 2, EBossDifficulty.Hard, errors);

            Assert.IsNotEmpty(errors);
        }

        /****************************************
        *      Factory: Wired Together Or Not At All
        ****************************************/

        [Test]
        public void Factory_HardP07_NeedsWarpArenaAndCircleListener_AndReadinessAgrees()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);
            FakeWarp warp = new FakeWarp();
            FakeArena arena = new FakeArena();
            RecordingListener listener = new RecordingListener();

            WolbaekPatternFactory[] incomplete =
            {
                new WolbaekPatternFactory(HREF),
                new WolbaekPatternFactory(HREF, warp: warp),
                new WolbaekPatternFactory(HREF, arena: arena),
                new WolbaekPatternFactory(HREF, circleListener: listener),
                new WolbaekPatternFactory(HREF, arena: arena, warp: warp),
                new WolbaekPatternFactory(HREF, circleListener: listener, warp: warp),
                new WolbaekPatternFactory(HREF, circleListener: listener, arena: arena)
            };

            for (int i = 0; i < incomplete.Length; ++i)
            {
                Assert.IsFalse(incomplete[i].CanCreate(p), $"factory {i}");
                Assert.IsNull(incomplete[i].Create(p), $"factory {i}");
            }

            WolbaekPatternFactory full = new WolbaekPatternFactory(HREF, circleListener: listener, arena: arena, warp: warp);
            Assert.IsTrue(full.CanCreate(p));
            Assert.IsInstanceOf<BossWarpBlastPattern>(full.Create(p));
            Assert.IsTrue(LoadCatalog().IsPatternReady(WOLBAEK_ID, p));
        }

        [Test]
        public void Factory_EasyP07KeepsItsOwnRule_AndHardIdDoesNotLeakToOtherPatterns()
        {
            FakeWarp warp = new FakeWarp();
            FakeArena arena = new FakeArena();
            RecordingListener listener = new RecordingListener();
            PatternDefinition easy = LoadDefinition(EASY_ID);

            // 이지 P07은 알림 수신처 없이도 만든다, 이지 동작은 그대로다
            Assert.IsTrue(new WolbaekPatternFactory(HREF, arena: arena, warp: warp).CanCreate(easy));
            Assert.IsInstanceOf<BossWarpBlastPattern>(new WolbaekPatternFactory(HREF, arena: arena, warp: warp).Create(easy));

            // 워프, 알림을 더해도 하드 P01~P06의 생성 가능 여부는 달라지지 않는다
            WolbaekPatternFactory without = new WolbaekPatternFactory(HREF, arena: arena);
            WolbaekPatternFactory with    = new WolbaekPatternFactory(HREF, arena: arena, warp: warp, circleListener: listener);
            for (int i = 0; i < HardOthers.Length; ++i)
            {
                PatternDefinition p = LoadDefinition(HardOthers[i]);
                Assert.AreEqual(without.CanCreate(p), with.CanCreate(p), HardOthers[i]);
                Assert.AreEqual(without.CanCreate(p), without.Create(p) != null, HardOthers[i]);
            }
        }

        /****************************************
        *      Search: Order, Limits, Failures
        ****************************************/

        [Test]
        public void Start_BehindIsFirstCandidate_WarpsThreeMetersBehindAndChecksOnlyThatOne()
        {
            Run r = new Run();
            r.Start();

            Assert.IsFalse(r.Pattern.IsStartRejected);
            Assert.AreEqual(0, r.Pattern.LandedIndex);
            Assert.AreEqual(1, r.Pattern.CheckedCandidates);
            Assert.AreEqual(1, r.Pattern.PathChecks);
            Assert.AreEqual(1, r.Warp.WarpCalls.Count);
            AssertNear(new Vector3(0f, 0f, 5f), r.Warp.WarpCalls[0], "대상 뒤 3m");
            AssertNear(new Vector3(0f, 0f, 5f), r.Self.Position);
        }

        [TestCase(0f, 0f, 1f, 0f, 0f, 5f)]
        [TestCase(0f, 0f, -1f, 0f, 0f, 11f)]
        [TestCase(1f, 0f, 0f, -3f, 0f, 8f)]
        public void Start_ReadsTargetServerFacingAtCastStart(float fx, float fy, float fz, float ex, float ey, float ez)
        {
            Run r = new Run(targetForward: new Vector3(fx, fy, fz));
            r.Start();

            AssertNear(new Vector3(ex, ey, ez), r.Warp.WarpCalls[0]);
        }

        [Test]
        public void Start_AllFail_ChecksTheEightCandidatesInOrderBehindDiagonalSideFront()
        {
            Run r = new Run();
            r.Warp.CanStand = point => false;
            r.Start();

            Assert.AreEqual(MAX_CANDIDATES, r.Warp.StandCalls.Count);
            for (int i = 0; i < MAX_CANDIDATES; ++i)
            {
                AssertNear(BossWarpBlastPattern.GetCandidate(TargetSpot, Vector3.forward, i, 3f), r.Warp.StandCalls[i], $"index {i}");
            }

            float d = 3f / Mathf.Sqrt(2f);
            AssertNear(new Vector3(0f, 0f, 5f), r.Warp.StandCalls[0]);                   // 후방
            AssertNear(new Vector3(d, 0f, 8f - d), r.Warp.StandCalls[1]);                // 후방 대각 오른쪽
            AssertNear(new Vector3(3f, 0f, 8f), r.Warp.StandCalls[3]);                   // 측면 오른쪽
            AssertNear(new Vector3(d, 0f, 8f + d), r.Warp.StandCalls[5]);                // 전방 대각 오른쪽
            AssertNear(new Vector3(0f, 0f, 11f), r.Warp.StandCalls[7]);                  // 전방
        }

        [Test]
        public void Start_BehindBlockedByCollision_UsesRightDiagonalNextAndSkipsPathCheckForBlocked()
        {
            Run r = new Run();
            r.Warp.CanStand = point => Mathf.Abs(point.x) > 0.01f;
            r.Start();

            Assert.AreEqual(1, r.Pattern.LandedIndex);
            Assert.AreEqual(2, r.Pattern.CheckedCandidates);
            Assert.AreEqual(2, r.Warp.StandCalls.Count);
            Assert.AreEqual(1, r.Pattern.PathChecks, "충돌에서 막힌 후보는 경로 검사를 받지 않는다");
        }

        [Test]
        public void Start_BehindFailsPath_ChecksNextAndOrdersStandBeforePathPerCandidate()
        {
            Run r = new Run();
            r.Warp.HasWalk = point => !Mathf.Approximately(point.z, 5f);
            r.Start();

            Assert.AreEqual(1, r.Pattern.LandedIndex);
            Assert.AreEqual(2, r.Pattern.PathChecks);
            CollectionAssert.AreEqual(new[] { "stand", "path", "stand", "path", "warp", "telegraph" }, r.Log);
        }

        [Test]
        public void Start_OutsideArena_SkipsWithoutCollisionOrPathCalls()
        {
            // 대상이 전장 가장자리에서 중심을 보고 서 있어 후방과 후방 대각이 전장 밖이다
            Run r = new Run(targetPosition: new Vector3(0f, 0f, 17f), targetForward: Vector3.back);
            r.Start();

            Assert.IsFalse(r.Pattern.IsStartRejected);
            Assert.AreEqual(3, r.Pattern.LandedIndex);
            Assert.AreEqual(4, r.Pattern.CheckedCandidates);
            Assert.AreEqual(1, r.Warp.StandCalls.Count, "경계에서 막힌 후보는 충돌 검사를 받지 않는다");
            Assert.AreEqual(1, r.Warp.PathCalls.Count);
        }

        [Test]
        public void Start_PathAlwaysFails_UsesAtMostEightCandidatesAndOnePathCheckEach_InTheStartTickOnly()
        {
            Run r = new Run();
            r.Warp.HasWalk = point => false;
            r.Start();

            Assert.IsTrue(r.Pattern.IsStartRejected);
            Assert.AreEqual(MAX_CANDIDATES, r.Pattern.CheckedCandidates);
            Assert.AreEqual(MAX_CANDIDATES, r.Pattern.PathChecks);
            Assert.AreEqual(0, r.Warp.WarpCalls.Count);
            Assert.LessOrEqual(r.Warp.TotalCalls, MAX_CANDIDATES * 2, "한 번의 시작에서 서버 창구 호출은 후보당 충돌과 경로 한 번씩이다");
        }

        [Test]
        public void Tick_AfterLanding_NeverCallsTheWarpWindowAgain()
        {
            Run r = new Run();
            r.Start();
            int callsAfterStart = r.Warp.TotalCalls;

            for (double t = 0.0625; t <= 3.0; t += 0.0625)
            {
                r.Fsm.Tick(t);
            }

            Assert.AreEqual(callsAfterStart, r.Warp.TotalCalls, "매 틱 전장 스캔이나 경로 계산을 하지 않는다");
        }

        [TestCase(12, 8)]
        [TestCase(8, 8)]
        [TestCase(3, 3)]
        [TestCase(1, 1)]
        public void Start_NeverChecksMoreThanDefinitionOrEightCandidates(int dataCount, int expectedChecked)
        {
            Run r = new Run(MakeHard(dataCount));
            r.Warp.CanStand = point => false;
            r.Start();

            Assert.AreEqual(expectedChecked, r.Pattern.CheckedCandidates);
            Assert.AreEqual(expectedChecked, r.Warp.StandCalls.Count);
        }

        [Test]
        public void Start_TryWarpRefusesBehind_ContinuesToNextCandidate()
        {
            Run r = new Run();
            r.Warp.Accepts = point => !Mathf.Approximately(point.z, 5f);
            r.Start();

            Assert.AreEqual(1, r.Pattern.LandedIndex);
            Assert.AreEqual(2, r.Warp.WarpCalls.Count);
            Assert.AreEqual(1, r.Listener.StartedCount);
        }

        [Test]
        public void Start_TargetDistanceBoundary_FivePointZeroPassesAndBelowRejects()
        {
            Run exact = new Run(targetPosition: new Vector3(0f, 0f, 5f));
            exact.Start();
            Assert.IsFalse(exact.Pattern.IsStartRejected, "5m는 선택 가능이다");

            Run below = new Run(targetPosition: new Vector3(0f, 0f, 4.99f));
            below.Start();
            Assert.IsTrue(below.Pattern.IsStartRejected);
            Assert.AreEqual(0, below.Warp.TotalCalls);
        }

        [Test]
        public void Start_MissingArenaOrInvalidTarget_RejectsWithoutAnyWarpCall()
        {
            Run noArena = new Run();
            noArena.Arena.IsAvailable = false;
            noArena.Start();

            Run dead = new Run();
            dead.Target.LifeState = EParticipantLifeState.Dead;
            dead.Start();

            Run summon = new Run();
            summon.Target.IsSummon = true;
            summon.Start();

            Run unknown = new Run();
            unknown.Start(selectedTargetId: 999);

            Assert.IsTrue(noArena.Pattern.IsStartRejected);
            Assert.IsTrue(dead.Pattern.IsStartRejected);
            Assert.IsTrue(summon.Pattern.IsStartRejected);
            Assert.IsTrue(unknown.Pattern.IsStartRejected);
            Assert.AreEqual(0, noArena.Warp.TotalCalls + dead.Warp.TotalCalls + summon.Warp.TotalCalls + unknown.Warp.TotalCalls);
        }

        [Test]
        public void Start_AllEightFail_RejectsWithoutWarpTelegraphOrCancelNotice()
        {
            Run r = new Run();
            r.Warp.CanStand = point => false;
            r.Start();

            Assert.IsTrue(r.Pattern.IsStartRejected);
            AssertNear(Origin, r.Self.Position, "보스는 움직이지 않는다");
            Assert.AreEqual(0, r.Listener.StartedCount);

            r.Fsm.Cancel(EBossInterruptReason.None);

            Assert.AreEqual(0, r.Listener.CancelCalls, "알린 예고가 없으므로 취소도 알리지 않는다");
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        /****************************************
        *      Telegraph: Before And After Warp
        ****************************************/

        [Test]
        public void Start_NoTelegraphBeforeWarp_ThenTelegraphAtActualLandedPosition()
        {
            Run r = new Run();
            r.Warp.SnapOffset = new Vector3(0.25f, 0f, 0f);

            r.Start();

            CollectionAssert.AreEqual(new[] { "stand", "path", "warp", "telegraph" }, r.Log, "착지 뒤에야 예고를 알린다");
            Vector3 landed = new Vector3(0.25f, 0f, 5f);
            AssertNear(landed, r.Self.Position);
            AssertNear(landed, r.Listener.Center, "서버가 돌려준 실제 위치가 중심이다");
            Assert.AreEqual(RADIUS, r.Listener.Radius, TOLERANCE);
            Assert.AreEqual(1.5f, r.Listener.Seconds, TOLERANCE);
            Assert.AreEqual(1, r.Listener.StartedCount);
        }

        [Test]
        public void Tick_BossMovesDuringTelegraph_StillStrikesAtLandedCenter()
        {
            Run r = new Run();
            r.AddPlayer(11, new Vector3(0f, 0f, 3f));
            r.AddPlayer(12, new Vector3(1f, 0f, 0f));
            r.Start();

            r.Self.Position = new Vector3(30f, 0f, 0f);
            r.Fsm.Tick(0.75);
            r.Fsm.Tick(TELEGRAPH);

            Assert.IsTrue(HasTarget(r.Resolver, 11));
            Assert.IsFalse(HasTarget(r.Resolver, 12), "워프 전 위치 주변은 맞지 않는다");
            AssertNear(new Vector3(0f, 0f, 5f), r.Resolver.Requests[0].SourcePosition);
        }

        /****************************************
        *     Strike: Targets, Damage, Timing
        ****************************************/

        [Test]
        public void Tick_JustBeforeTelegraphEnd_DoesNotStrike_AndAtEndStrikesOnce()
        {
            Run r = new Run();
            r.Start();

            r.Fsm.Tick(1.0);
            r.Fsm.Tick(1.4999);
            Assert.AreEqual(EBossAttackState.Telegraph, r.Fsm.State);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.HitCalls);

            r.Fsm.Tick(TELEGRAPH);
            Assert.AreEqual(1, r.Resolver.Requests.Count);
            Assert.AreEqual(1, r.Listener.HitCalls);
        }

        [Test]
        public void Tick_AtTelegraphEnd_StrikesEachPlayerInRangeOnceWithGuardableDamage()
        {
            Run r = new Run();
            r.AddPlayer(11, new Vector3(2f, 0f, 5f));
            r.AddPlayer(12, new Vector3(-3f, 0f, 3f));
            r.AddPlayer(13, new Vector3(0f, 0f, 30f));
            r.Start();

            r.Fsm.Tick(TELEGRAPH);
            r.Fsm.Tick(1.75);
            r.Fsm.Tick(COMPLETE);
            r.Fsm.Tick(3.0);

            Assert.AreEqual(3, r.Resolver.Requests.Count, "대상 포함 범위 안 세 명이 한 번씩");
            Assert.IsFalse(HasTarget(r.Resolver, 13));
            HashSet<int> hit = new HashSet<int>();
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                Assert.IsTrue(hit.Add(request.HitTargetId), "대상당 중복 피해 없음");
                Assert.AreEqual(DAMAGE_REF * HREF, request.RawDamage, TOLERANCE);
                Assert.IsFalse(request.IsUnblockable, "반격 가능");
                Assert.AreEqual(r.Self.CombatId, request.AttackerId);
                Assert.AreEqual(HARD_ID, request.SourceDefinitionId);
            }

            Assert.AreEqual(r.Resolver.Requests[0].ActionInstanceId, r.Resolver.Requests[1].ActionInstanceId);
            Assert.AreEqual(r.Resolver.Requests[1].ActionInstanceId, r.Resolver.Requests[2].ActionInstanceId, "한 동작 번호를 공유한다");
            Assert.AreEqual(1, r.Listener.HitCalls);
            Assert.AreEqual(3, r.Listener.LastHitCount);
        }

        [Test]
        public void Tick_RadiusBoundary_IncludesEdgeAndExcludesBeyond_SummonsHitDeadSkipped()
        {
            Run r = new Run();
            r.AddPlayer(11, new Vector3(RADIUS, 0f, 5f));
            r.AddPlayer(12, new Vector3(RADIUS + 0.01f, 0f, 5f));
            r.AddPlayer(13, new Vector3(1f, 0f, 5f), isSummon: true);
            FakeCombatTarget dead = r.AddPlayer(14, new Vector3(1f, 0f, 6f));
            dead.LifeState = EParticipantLifeState.Dead;
            r.Start();

            r.Fsm.Tick(TELEGRAPH);

            Assert.IsTrue(HasTarget(r.Resolver, 10));
            Assert.IsTrue(HasTarget(r.Resolver, 11));
            Assert.IsFalse(HasTarget(r.Resolver, 12));
            Assert.IsTrue(HasTarget(r.Resolver, 13));
            Assert.IsFalse(HasTarget(r.Resolver, 14));
        }

        [Test]
        public void Tick_SelectedTargetDiesAfterStart_StillStrikesOthersAtSameCenter()
        {
            Run r = new Run();
            r.AddPlayer(11, new Vector3(1f, 0f, 5f));
            r.Start();

            r.Fsm.Tick(0.75);
            r.Target.LifeState = EParticipantLifeState.Dead;
            r.Fsm.Tick(TELEGRAPH);

            Assert.AreEqual(1, r.Resolver.Requests.Count);
            Assert.AreEqual(11, r.Resolver.Requests[0].HitTargetId);
        }

        [Test]
        public void Tick_RecoveryIsOneSecond_NextSelectionOpensAtTwoPointFive()
        {
            Run r = new Run();
            r.Start();

            r.Fsm.Tick(TELEGRAPH);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State, "즉발이라 발동 단계를 바로 지난다");

            r.Fsm.Tick(2.25);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.Fsm.Tick(2.4999);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);

            r.Fsm.Tick(COMPLETE);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [Test]
        public void Tick_NonZeroStartTime_TimesFollowTheServerClockExactly()
        {
            Run r = new Run();
            r.Start(startTime: 8.0);

            r.Fsm.Tick(9.4999);
            Assert.AreEqual(0, r.Resolver.Requests.Count);

            r.Fsm.Tick(9.5);
            Assert.AreEqual(1, r.Resolver.Requests.Count);

            r.Fsm.Tick(10.4999);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);

            r.Fsm.Tick(10.5);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [Test]
        public void Tick_HugeStep_StrikesExactlyOnceAndFinishes()
        {
            Run r = new Run();
            r.AddPlayer(11, new Vector3(1f, 0f, 5f));
            r.Start();

            r.Fsm.Tick(100.0);
            r.Fsm.Tick(101.0);

            Assert.AreEqual(2, r.Resolver.Requests.Count, "대상 둘이 한 번씩");
            Assert.AreEqual(1, r.Listener.HitCalls);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        public void Cancel_AfterWarpBeforeStrike_StopsDamageButBossStaysAtLanding(EBossInterruptReason reason)
        {
            Run r = new Run();
            r.Start();

            r.Fsm.Tick(0.75);
            r.Fsm.Cancel(reason);
            r.Fsm.Tick(TELEGRAPH);
            r.Fsm.Tick(100.0);

            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.HitCalls);
            Assert.AreEqual(1, r.Listener.CancelCalls);
            AssertNear(new Vector3(0f, 0f, 5f), r.Self.Position, "이미 한 워프는 되돌리지 않는다");
        }

        [Test]
        public void Cancel_AfterStrike_DoesNotNotifyCancelAndDoesNotHitAgain()
        {
            Run r = new Run();
            r.Start();

            r.Fsm.Tick(TELEGRAPH);
            r.Fsm.Cancel(EBossInterruptReason.CombatEnd);
            r.Fsm.Tick(100.0);

            Assert.AreEqual(1, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.CancelCalls);
        }

        [Test]
        public void Tick_GateClosedBeforeStrike_RequestsNothing()
        {
            Run r = new Run();
            r.Start();

            r.Fsm.Tick(0.75);
            r.Gate.CloseForGood();
            r.Fsm.Tick(TELEGRAPH);

            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        /****************************************
        *     Brain: Selection, Reject, Retry
        ****************************************/

        // 하드 2페이즈 후보를 P07 하나(혹은 P07과 P01)만 남기고 실제 하드 카탈로그와 실제 팩토리로 두뇌를 돌린다
        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public FakeCombatTarget   Player   = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = TargetSpot };
            public PlayersQuery       Query    = new PlayersQuery();
            public FakeDamageResolver Damage   = new FakeDamageResolver();
            public FakeBossVitals     Vitals   = new FakeBossVitals();
            public FakeEffectLifetime Effects  = new FakeEffectLifetime();
            public FakeBossRandom     Random   = new FakeBossRandom();
            public RecordingListener  Listener = new RecordingListener();
            public FakeWarp           Warp     = new FakeWarp();
            public FakeArena          Arena    = new FakeArena();
            public BossBrain          Brain;

            // keepStar가 참이면 하드 P01(전장 입력만 있으면 만들어지는 단계형 별 빔)을 남겨 P07이 거절됐을 때 고를 대안으로 쓴다
            public BrainRig(bool keepStar = false, bool wireListener = true)
            {
                Query.Targets.Add(Player);
                Warp.Body = Self;

                WolbaekPatternFactory inner = new WolbaekPatternFactory(HREF, circleListener: wireListener ? Listener : null,
                    arena: Arena, warp: Warp);

                List<string> excluded = new List<string>();
                for (int i = 0; i < HardOthers.Length; ++i)
                {
                    if (!(keepStar && (HardOthers[i] == BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID)))
                    {
                        excluded.Add(HardOthers[i]);
                    }
                }

                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog(), EBossDifficulty.Hard),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = new ExcludingPatternFactory(inner, excluded.ToArray()),
                    Effects  = Effects,
                    Control  = new FakeControlSource(),
                    Random   = Random
                });
            }

            public void TransitionAndHandoff()
            {
                Assert.IsTrue(Brain.Begin(0.0));
                Vitals.HealthRate = 0.5f;
                Brain.Tick(0.25);
                Assert.AreEqual(EBossPhaseState.AwaitingHandoff, Brain.PhaseState);
                Assert.IsTrue(Brain.CompleteHandoff());
            }

            public bool TickUntilAttack(double from, double to)
            {
                for (double t = from; t <= to; t += 0.25)
                {
                    Brain.Tick(t);
                    if (Brain.AttackState != EBossAttackState.Ready)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        [Test]
        public void Brain_FarTarget_SelectsHardP07AndWarpsBehindTarget()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            Assert.AreEqual(EBossPhaseState.Active, r.Brain.PhaseState);

            Assert.IsTrue(r.TickUntilAttack(0.5, 10.0));

            Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId);
            AssertNear(new Vector3(0f, 0f, 5f), r.Self.Position);
            AssertNear(new Vector3(0f, 0f, 5f), r.Listener.Center);
            Assert.AreEqual(1, r.Listener.StartedCount);
        }

        [Test]
        public void Brain_TargetNearerThanFiveMeters_NeverSelectsHardP07_FiveMetersDoes()
        {
            BrainRig near = new BrainRig();
            near.Player.Position = new Vector3(0f, 0f, 4.99f);
            near.TransitionAndHandoff();
            Assert.IsFalse(near.TickUntilAttack(0.5, 20.0));
            Assert.AreEqual(0, near.Warp.TotalCalls);

            BrainRig exact = new BrainRig();
            exact.Player.Position = new Vector3(0f, 0f, 5f);
            exact.TransitionAndHandoff();
            Assert.IsTrue(exact.TickUntilAttack(0.5, 10.0));
            Assert.AreEqual(HARD_ID, exact.Brain.ActivePatternId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Brain_WithoutCircleListener_NeverSelectsHardP07_AndNothingHitsUnseen()
        {
            BrainRig r = new BrainRig(wireListener: false);
            r.TransitionAndHandoff();

            Assert.IsFalse(r.TickUntilAttack(0.5, 20.0), "알림 수신처가 없으면 하드 P07은 후보에서 빠진다");
            Assert.AreEqual(0, r.Warp.TotalCalls);
            Assert.AreEqual(0, r.Damage.Requests.Count);
            AssertNear(Origin, r.Self.Position);
        }

        // 착지 후보가 모두 실패하면 같은 틱에 P07을 뺀 다른 패턴(하드 P01)이 시작한다. 거절된 P07은 보스를 옮기지도 예고하지도 않는다
        [Test]
        public void Brain_NoLandingAnywhere_ReselectsAnotherPatternInTheSameTick()
        {
            BrainRig r = new BrainRig(keepStar: true);
            r.Random.Roll = 0.999f; // 마지막 양수 가중치(P07)를 먼저 뽑는다
            r.Warp.CanStand = point => false;
            r.TransitionAndHandoff();

            r.Brain.Tick(0.5);

            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID, r.Brain.ActivePatternId, "같은 틱에 대안이 시작한다");
            Assert.AreEqual(MAX_CANDIDATES, r.Warp.StandCalls.Count, "P07은 후보 8곳을 한 번만 검사했다");
            Assert.AreEqual(0, r.Warp.WarpCalls.Count);
            AssertNear(Origin, r.Self.Position, "보스는 워프하지 않았다");
            Assert.AreEqual(0, r.Listener.StartedCount, "거절된 P07의 예고는 없다");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Brain_NoLandingAndNoAlternative_WaitsAndRetriesOnlyAtRetryInterval()
        {
            BrainRig r = new BrainRig();
            r.Warp.CanStand = point => false;
            r.TransitionAndHandoff();

            // 1/16초 간격으로 0.5초부터 2.0초까지 25틱을 돈다, 재조회 간격 0.25초마다 한 번씩 7번만 탐색한다
            double t = 0.5;
            for (int i = 0; i <= 24; ++i)
            {
                r.Brain.Tick(t + (i / 16.0));
                Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState, "거절된 시전은 진행 중으로 남지 않는다");
            }

            Assert.AreEqual(7 * MAX_CANDIDATES, r.Warp.StandCalls.Count, "시도 7번 x 후보 8곳");
            Assert.AreEqual(0, r.Warp.WarpCalls.Count);
            Assert.AreEqual(0, r.Listener.StartedCount);
            LogAssert.NoUnexpectedReceived();

            // 거절이 사용 기록을 남기지 않았으므로 유효 착지가 생기면 바로 선택된다
            r.Warp.CanStand = point => true;
            Assert.IsTrue(r.TickUntilAttack(2.0, 4.0));
            Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId);
        }

        [Test]
        public void Brain_FullCycle_StrikesAtOnePointFiveAndOpensNextSelectionAtTwoPointFive()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();

            // 인계 다음 첫 틱(t=0.5)에 시전이 시작한다
            r.Brain.Tick(0.5);
            Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId);
            Assert.AreEqual(1, r.Listener.StartedCount);

            r.Brain.Tick(1.9999);
            Assert.AreEqual(0, r.Damage.Requests.Count, "예고 1.5초 전에는 타격이 없다");

            r.Brain.Tick(2.0);
            Assert.AreEqual(1, r.Damage.Requests.Count);
            Assert.AreNotEqual(EBossAttackState.Ready, r.Brain.AttackState);

            r.Brain.Tick(2.9999);
            Assert.AreNotEqual(EBossAttackState.Ready, r.Brain.AttackState, "후딜 1초가 끝나기 전에는 다음 선택이 없다");
            Assert.AreEqual(1, r.Warp.WarpCalls.Count);

            // 착지한 보스가 대상과 3m라 근거리 배율 0이어서 다음 선택은 P07을 고르지 않는다
            r.Brain.Tick(3.0);
            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState);
            Assert.AreEqual(1, r.Damage.Requests.Count, "대상당 한 번만 맞는다");
            Assert.AreEqual(1, r.Warp.WarpCalls.Count);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
