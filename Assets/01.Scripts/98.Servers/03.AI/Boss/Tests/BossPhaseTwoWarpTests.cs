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
    // 월백 이지 2페이즈 P07(후방 우선 워프 후 충격파)을 에셋의 실제 정의와 실제 팩토리로 확인한다. 워프 창구(IBossWarp)와 전장 입력은 가짜이며
    // 실제 서버 몸체의 순간 이동, 충돌, 경로 검사와 복제, 화면은 거치지 않는다. 수치는 하드 설계안에서 옮긴 초기 시험값이다.
    // 시각은 이진 분수로만 넘긴다
    public class BossPhaseTwoWarpTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string P07_ID       = BossPatternCatalogSO.WOLBAEK_P2_P07_ID;
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
            public FakeCombatTarget  Body;
            public List<string>      Log;
            public Func<Vector3, bool> CanStand = point => true;
            public Func<Vector3, bool> HasWalk  = point => true;
            public Func<Vector3, bool> Accepts  = point => true;
            public Vector3           SnapOffset = Vector3.zero;

            public readonly List<Vector3> StandCalls = new List<Vector3>();
            public readonly List<Vector3> PathCalls  = new List<Vector3>();
            public readonly List<Vector3> WarpCalls  = new List<Vector3>();

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

        // 가드 여부와 정면을 바꿀 수 있는 플레이어 대역, 실제 DamageResolver 시험에 쓴다
        private sealed class GuardPlayer : IDamageable
        {
            public int                   CombatId        { get; set; }
            public ECombatFaction        Faction         => ECombatFaction.Player;
            public EParticipantLifeState LifeState       { get; private set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; set; }
            public Vector3               Forward         { get; set; } = Vector3.forward;
            public float                 BodyRadius      => 0.5f;
            public float                 Health          { get; private set; } = 10000f;
            public float                 MaxHealth       => 10000f;
            public float                 Defense         => 0f;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront { get; set; }

            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
            {
                Health = isLethal ? 0f : Mathf.Max(0f, Health - appliedAmount);
                if (isLethal)
                {
                    LifeState = EParticipantLifeState.Dead;
                }
            }
        }

        // 공격 FSM과 실제 팩토리 구성 요소를 가짜 대상 조회와 가짜 피해 처리기에 이어 붙인 실행 단위다
        private sealed class Run
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public FakeCombatTarget   Target;
            public PlayersQuery       Query    = new PlayersQuery();
            public FakeDamageResolver Resolver = new FakeDamageResolver();
            public BossDamageGate     Gate;
            public RecordingListener  Listener = new RecordingListener();
            public FakeWarp           Warp     = new FakeWarp();
            public FakeArena          Arena    = new FakeArena();
            public BossAttackFsm      Fsm      = new BossAttackFsm();
            public PatternDefinition  Definition;
            public BossWarpBlastPattern Pattern;
            public List<string>       Log      = new List<string>();

            public Run(PatternDefinition definition = null, Vector3? targetPosition = null, Vector3? targetForward = null)
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition    = definition ?? LoadDefinition();
                Warp.Body     = Self;
                Warp.Log      = Log;
                Listener.Log  = Log;
                Target        = AddPlayer(10, targetPosition ?? TargetSpot);
                Target.Forward = targetForward ?? Vector3.forward;
                Pattern       = new BossWarpBlastPattern(HREF, Warp, Arena, Listener);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player,
                    Position = position, IsSummon = isSummon };
                Query.Targets.Add(player);
                return player;
            }

            public void Start(int selectedTargetId = 10)
            {
                BossPatternContext context = new BossPatternContext(Definition, 100, selectedTargetId, Self, Query, Gate);
                Fsm.Start(Definition, Pattern, in context, 0.0);
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

        private static PatternDefinition LoadDefinition()
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern(P07_ID, out PatternDefinition definition));
            return definition;
        }

        // 에셋 정의와 같은 값에서 워프 수치만 바꾼 정의, 이지와 하드를 따로 조정할 수 있는지 보는 데도 쓴다
        private static PatternDefinition MakeDefinition(int count = MAX_CANDIDATES, float landing = 3f, float minDistance = 5f,
            EEnemyAttackShape shape = EEnemyAttackShape.Circle, EBossDifficulty difficulty = EBossDifficulty.Easy)
        {
            EnemyCatalogSO.AttackShapeSpec spec = (shape == EEnemyAttackShape.Circle)
                ? EnemyCatalogSO.AttackShapeSpec.Circle(RADIUS)
                : EnemyCatalogSO.AttackShapeSpec.Fan(RADIUS, 90f);

            return new PatternDefinition(P07_ID, WOLBAEK_ID, 2, 6.5f,
                new[] { 0f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 0f, 0.3f, 0.7f, 1f },
                1.5f, 0f, 0f, 0f, 1f, spec, DAMAGE_REF, true,
                group: EPatternGroup.Pressure, enhancedBaseWeight: 7f, difficulty: difficulty,
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

        /****************************************
        *      Asset, Factory, Readiness Agree
        ****************************************/

        [Test]
        public void Asset_P07_HasInitialTrialValuesAndSelectionData()
        {
            PatternDefinition p = LoadDefinition();

            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EBossDifficulty.Easy, p.Difficulty);
            Assert.AreEqual(EPatternGroup.Pressure, p.Group);
            Assert.AreEqual(1.5f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0f, p.ActiveSeconds);
            Assert.AreEqual(1f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Circle, p.Shape.Shape);
            Assert.AreEqual(RADIUS, p.Shape.Range, TOLERANCE);
            Assert.AreEqual(DAMAGE_REF, p.DamageRef, TOLERANCE);
            Assert.IsTrue(p.Guardable);
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(5f, p.WarpMinTargetDistance, TOLERANCE);
            Assert.AreEqual(3f, p.WarpLandingDistance, TOLERANCE);
            Assert.AreEqual(MAX_CANDIDATES, p.WarpCandidateCount);
            Assert.AreEqual(6.5f, p.BaseWeight, TOLERANCE);
            Assert.AreEqual(7f, p.EnhancedBaseWeight, TOLERANCE);
            CollectionAssert.AreEqual(new[] { 0f, 1f, 1f }, new List<float>(p.DistanceMultipliers), "5m 미만은 선택하지 않는다");
            CollectionAssert.AreEqual(new[] { 0f, 0.3f, 0.7f, 1f }, new List<float>(p.RecentUseMultipliers), "P01~P06과 같은 이지 최근 배율");
        }

        [Test]
        public void Asset_P07_NearDistanceBoundaryMatchesMinimumTargetDistance()
        {
            Assert.IsTrue(LoadCatalog().TryGetBoss(WOLBAEK_ID, out BossDefinition boss));

            Assert.AreEqual(LoadDefinition().WarpMinTargetDistance, boss.NearDistanceMax, TOLERANCE,
                "근거리 배율 0과 최소 거리 5m가 같은 경계를 가리킨다");
        }

        [Test]
        public void Asset_P07_IsEasyOnlyAndHardUsesItsOwnDefinition()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            int count = 0;
            for (int i = 0; i < catalog.Patterns.Count; ++i)
            {
                if (catalog.Patterns[i].DefinitionId == P07_ID)
                {
                    ++count;
                }
            }

            Assert.AreEqual(1, count);

            List<PatternDefinition> hard = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Hard, hard);
            for (int i = 0; i < hard.Count; ++i)
            {
                Assert.AreNotEqual(P07_ID, hard[i].DefinitionId);

                // 이지 P07의 워프 값은 하드 정의가 갖지 않는다
                Assert.AreEqual(0, hard[i].WarpCandidateCount, hard[i].DefinitionId);
            }
        }

        [Test]
        public void Asset_EasyP01ToP06_SelectionValuesAreUnchanged()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            float[][] expected =
            {
                new[] { 10f, 12f }, new[] { 10f, 12f }, new[] { 12f, 14f }, new[] { 16f, 18f }, new[] { 22f, 18f }, new[] { 8f, 9f }
            };
            string[] ids =
            {
                BossPatternCatalogSO.WOLBAEK_P2_P01_ID, BossPatternCatalogSO.WOLBAEK_P2_P02_ID, BossPatternCatalogSO.WOLBAEK_P2_P03_ID,
                BossPatternCatalogSO.WOLBAEK_P2_P04_ID, BossPatternCatalogSO.WOLBAEK_P2_P05_ID, BossPatternCatalogSO.WOLBAEK_P2_P06_ID
            };

            for (int i = 0; i < ids.Length; ++i)
            {
                Assert.IsTrue(catalog.TryGetPattern(ids[i], out PatternDefinition p), ids[i]);
                Assert.AreEqual(expected[i][0], p.BaseWeight, TOLERANCE, ids[i]);
                Assert.AreEqual(expected[i][1], p.EnhancedBaseWeight, TOLERANCE, ids[i]);
                CollectionAssert.AreEqual(new[] { 0f, 0.3f, 0.7f, 1f }, new List<float>(p.RecentUseMultipliers), ids[i]);
                Assert.AreEqual(0, p.WarpCandidateCount, ids[i]);
            }
        }

        [Test]
        public void Asset_P07_FactoryCreatesOnlyWithWarpAndArena_AndReadinessAgrees()
        {
            PatternDefinition p = LoadDefinition();
            FakeWarp warp = new FakeWarp();
            FakeArena arena = new FakeArena();

            WolbaekPatternFactory none = new WolbaekPatternFactory(HREF);
            WolbaekPatternFactory warpOnly = new WolbaekPatternFactory(HREF, warp: warp);
            WolbaekPatternFactory arenaOnly = new WolbaekPatternFactory(HREF, arena: arena);
            WolbaekPatternFactory both = new WolbaekPatternFactory(HREF, arena: arena, warp: warp);

            Assert.IsFalse(none.CanCreate(p));
            Assert.IsNull(none.Create(p));
            Assert.IsFalse(warpOnly.CanCreate(p));
            Assert.IsNull(warpOnly.Create(p));
            Assert.IsFalse(arenaOnly.CanCreate(p));
            Assert.IsNull(arenaOnly.Create(p));
            Assert.IsTrue(both.CanCreate(p));
            Assert.IsInstanceOf<BossWarpBlastPattern>(both.Create(p));
            Assert.IsTrue(LoadCatalog().IsPatternReady(WOLBAEK_ID, p));
            Assert.IsTrue(LoadCatalog().IsPhaseTuned(WOLBAEK_ID, 2));
        }

        [Test]
        public void Factory_WithoutWarp_ExistingEasyPhaseTwoCreatabilityIsUnchanged()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            BossProjectileManager projectiles = new BossProjectileManager(new FakeTargetQuery());
            BossAreaManager areas = new BossAreaManager(new FakeTargetQuery());
            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, projectiles: projectiles, areas: areas);
            string[] ids =
            {
                BossPatternCatalogSO.WOLBAEK_P2_P01_ID, BossPatternCatalogSO.WOLBAEK_P2_P02_ID, BossPatternCatalogSO.WOLBAEK_P2_P03_ID,
                BossPatternCatalogSO.WOLBAEK_P2_P04_ID, BossPatternCatalogSO.WOLBAEK_P2_P05_ID, BossPatternCatalogSO.WOLBAEK_P2_P06_ID
            };
            bool[] expected = { false, false, true, true, true, true };

            for (int i = 0; i < ids.Length; ++i)
            {
                Assert.IsTrue(catalog.TryGetPattern(ids[i], out PatternDefinition p), ids[i]);
                Assert.AreEqual(expected[i], factory.CanCreate(p), ids[i]);
            }

            Assert.IsFalse(factory.CanCreate(LoadDefinition()), "워프 창구가 없으면 P07만 빠지고 나머지 선택은 그대로다");
        }

        /****************************************
        *            Catalog Validation
        ****************************************/

        private BossPatternCatalogSO CopyWithP07(PatternDefinition replacement)
        {
            BossPatternCatalogSO copy = UnityEngine.Object.Instantiate(LoadCatalog());
            _created.Add(copy);

            List<PatternDefinition> patterns = new List<PatternDefinition>();
            for (int i = 0; i < copy.Patterns.Count; ++i)
            {
                if (copy.Patterns[i].DefinitionId != P07_ID)
                {
                    patterns.Add(copy.Patterns[i]);
                }
            }

            patterns.Add(replacement);

            const BindingFlags FLAGS = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(BossPatternCatalogSO).GetField("_patterns", FLAGS).SetValue(copy, patterns.ToArray());
            typeof(BossPatternCatalogSO).GetMethod("InvalidateLookups", FLAGS).Invoke(copy, null);
            return copy;
        }

        [Test]
        public void Validate_ValidWarpDefinition_Passes()
        {
            List<string> errors = new List<string>();

            CopyWithP07(MakeDefinition()).ValidateTuning(WOLBAEK_ID, 2, errors);

            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        }

        [TestCase(9, 3f, 5f, EEnemyAttackShape.Circle, "WarpCandidateCount")]
        [TestCase(-1, 3f, 5f, EEnemyAttackShape.Circle, "WarpCandidateCount")]
        [TestCase(8, 0f, 5f, EEnemyAttackShape.Circle, "WarpLandingDistance")]
        [TestCase(8, 18f, 5f, EEnemyAttackShape.Circle, "WarpLandingDistance")]
        [TestCase(8, 3f, -1f, EEnemyAttackShape.Circle, "WarpMinTargetDistance")]
        [TestCase(8, 3f, 5f, EEnemyAttackShape.Fan, "Shape")]
        [TestCase(0, 3f, 5f, EEnemyAttackShape.Circle, "WarpCandidateCount")]
        public void Validate_BadWarpData_ReportsField(int count, float landing, float minDistance, EEnemyAttackShape shape, string field)
        {
            List<string> errors = new List<string>();

            CopyWithP07(MakeDefinition(count, landing, minDistance, shape)).ValidateTuning(WOLBAEK_ID, 2, errors);

            Assert.IsTrue(errors.Exists(e => e.Contains(field)), string.Join("\n", errors));
        }

        /****************************************
        *       Candidate Order And Geometry
        ****************************************/

        [Test]
        public void GetCandidate_FacingForward_OrdersBehindDiagonalSideThenFrontRightFirst()
        {
            float d = 3f / Mathf.Sqrt(2f);
            Vector3[] expected =
            {
                new Vector3(0f, 0f, -3f),     // 후방
                new Vector3(d, 0f, -d),       // 후방 대각 오른쪽
                new Vector3(-d, 0f, -d),      // 후방 대각 왼쪽
                new Vector3(3f, 0f, 0f),      // 측면 오른쪽
                new Vector3(-3f, 0f, 0f),     // 측면 왼쪽
                new Vector3(d, 0f, d),        // 전방 대각 오른쪽
                new Vector3(-d, 0f, d),       // 전방 대각 왼쪽
                new Vector3(0f, 0f, 3f)       // 전방
            };

            for (int i = 0; i < expected.Length; ++i)
            {
                AssertNear(expected[i], BossWarpBlastPattern.GetCandidate(Origin, Vector3.forward, i, 3f), $"index {i}");
            }
        }

        [Test]
        public void GetCandidate_FollowsTargetFacingAndKeepsHeightAndDistance()
        {
            Vector3 target = new Vector3(4f, 2f, -6f);

            Vector3 behindEast = BossWarpBlastPattern.GetCandidate(target, Vector3.right, 0, 3f);
            AssertNear(new Vector3(1f, 2f, -6f), behindEast);

            for (int i = 0; i < MAX_CANDIDATES; ++i)
            {
                Vector3 point = BossWarpBlastPattern.GetCandidate(target, new Vector3(1f, 0f, 1f), i, 3f);
                Vector3 offset = point - target;
                Assert.AreEqual(0f, offset.y, TOLERANCE);
                Assert.AreEqual(3f, offset.magnitude, TOLERANCE, $"index {i}");
            }
        }

        /****************************************
        *      Search: Order, Limits, Failures
        ****************************************/

        [Test]
        public void Start_BehindIsFirstCandidate_WarpsThereAndChecksOnlyThatOne()
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
        public void Start_ReadsTargetFacingAtCastStart(float fx, float fy, float fz, float ex, float ey, float ez)
        {
            // 대상은 보스에서 8m 떨어진 (0,0,8)에 서서 정면을 바꾼다
            Run r = new Run(targetForward: new Vector3(fx, fy, fz));
            r.Start();

            AssertNear(new Vector3(ex, ey, ez), r.Warp.WarpCalls[0]);
        }

        [Test]
        public void Start_TargetFacingChangesAfterStart_DoesNotMoveLandingOrCenter()
        {
            Run r = new Run();
            r.Start();
            Vector3 landed = r.Self.Position;

            r.Target.Forward = Vector3.back;
            r.Fsm.Tick(0.75);
            r.Fsm.Tick(TELEGRAPH);

            AssertNear(landed, r.Listener.Center);
            AssertNear(landed, r.Pattern.Center);
            Assert.AreEqual(1, r.Warp.WarpCalls.Count);
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
            float d = 3f / Mathf.Sqrt(2f);
            AssertNear(new Vector3(d, 0f, 8f - d), r.Warp.WarpCalls[0]);
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
        public void Start_OutsideArena_SkipsWithoutCollisionOrPathCalls()
        {
            // 대상이 전장 가장자리에서 중심을 보고 서 있어 후방과 후방 대각이 전장 밖이다
            Run r = new Run(targetPosition: new Vector3(0f, 0f, 17f), targetForward: Vector3.back);
            r.Self.Position = Origin;
            r.Start();

            Assert.IsFalse(r.Pattern.IsStartRejected);
            Assert.AreEqual(3, r.Pattern.LandedIndex, "후방과 두 후방 대각은 전장 밖이라 측면 오른쪽이 처음 유효하다");
            Assert.AreEqual(4, r.Pattern.CheckedCandidates);
            Assert.AreEqual(1, r.Warp.StandCalls.Count, "경계에서 막힌 후보는 충돌 검사를 받지 않는다");
            Assert.AreEqual(1, r.Warp.PathCalls.Count);
        }

        [Test]
        public void Start_ArenaBoundaryIsInclusiveAndShrinksByBossBodyRadius()
        {
            // 후방 후보가 전장 중심에서 정확히 18m
            Run inside = new Run(targetPosition: new Vector3(0f, 0f, 15f), targetForward: Vector3.back);
            inside.Start();
            Assert.AreEqual(0, inside.Pattern.LandedIndex, "경계 위는 전장 안이다");

            Run bodyOut = new Run(targetPosition: new Vector3(0f, 0f, 15f), targetForward: Vector3.back);
            bodyOut.Self.BodyRadius = 0.5f;
            bodyOut.Start();
            Assert.AreNotEqual(0, bodyOut.Pattern.LandedIndex, "몸체 반경만큼 안쪽이 한계라 18m 후보는 밖이다");
        }

        [Test]
        public void Start_AllEightFail_RejectsWithoutWarpTelegraphOrCancelNotice()
        {
            Run r = new Run();
            r.Warp.CanStand = point => false;
            r.Start();

            Assert.IsTrue(r.Pattern.IsStartRejected);
            Assert.AreEqual(MAX_CANDIDATES, r.Pattern.CheckedCandidates);
            Assert.AreEqual(MAX_CANDIDATES, r.Warp.StandCalls.Count);
            Assert.AreEqual(0, r.Warp.PathCalls.Count);
            Assert.AreEqual(0, r.Warp.WarpCalls.Count);
            AssertNear(Origin, r.Self.Position, "보스는 움직이지 않는다");
            Assert.AreEqual(0, r.Listener.StartedCount);

            r.Fsm.Cancel(EBossInterruptReason.None);

            Assert.AreEqual(0, r.Listener.CancelCalls, "알린 예고가 없으므로 취소도 알리지 않는다");
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        [Test]
        public void Start_PathAlwaysFails_UsesAtMostOnePathCheckPerCandidate()
        {
            Run r = new Run();
            r.Warp.HasWalk = point => false;
            r.Start();

            Assert.IsTrue(r.Pattern.IsStartRejected);
            Assert.AreEqual(MAX_CANDIDATES, r.Pattern.CheckedCandidates);
            Assert.AreEqual(MAX_CANDIDATES, r.Pattern.PathChecks);
            Assert.AreEqual(0, r.Warp.WarpCalls.Count);
        }

        [TestCase(12, 8)]
        [TestCase(8, 8)]
        [TestCase(3, 3)]
        [TestCase(1, 1)]
        public void Start_NeverChecksMoreThanDefinitionOrEightCandidates(int dataCount, int expectedChecked)
        {
            Run r = new Run(MakeDefinition(dataCount));
            r.Warp.CanStand = point => false;
            r.Start();

            Assert.AreEqual(expectedChecked, r.Pattern.CheckedCandidates);
            Assert.AreEqual(expectedChecked, r.Warp.StandCalls.Count);
        }

        [Test]
        public void Start_LandingDistanceComesFromDefinition_SoEasyAndHardCanDiffer()
        {
            Run easy = new Run(MakeDefinition(landing: 3f));
            easy.Start();
            Run hard = new Run(MakeDefinition(landing: 4f, difficulty: EBossDifficulty.Hard));
            hard.Start();

            AssertNear(new Vector3(0f, 0f, 5f), easy.Warp.WarpCalls[0]);
            AssertNear(new Vector3(0f, 0f, 4f), hard.Warp.WarpCalls[0]);
        }

        [Test]
        public void Start_CandidateOverlappingTargetBody_IsRejectedByDistanceBeforeCollision()
        {
            Run r = new Run(MakeDefinition(landing: 0.5f));
            r.Self.BodyRadius   = 0.6f;
            r.Target.BodyRadius = 0.6f;
            r.Start();

            Assert.IsTrue(r.Pattern.IsStartRejected);
            Assert.AreEqual(MAX_CANDIDATES, r.Pattern.CheckedCandidates);
            Assert.AreEqual(0, r.Warp.StandCalls.Count, "거리 검사가 충돌 검사보다 앞선다");
        }

        [Test]
        public void Start_DegenerateTargetForward_TreatsTargetAsFacingBossSoBehindIsFarSide()
        {
            Run r = new Run(targetForward: Vector3.zero);
            r.Start();

            AssertNear(new Vector3(0f, 0f, 11f), r.Warp.WarpCalls[0]);
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
            Assert.AreEqual(0, below.Warp.StandCalls.Count);
            Assert.AreEqual(0, below.Warp.WarpCalls.Count);
        }

        [Test]
        public void Start_MissingArenaOrInvalidTarget_Rejects()
        {
            Run noArena = new Run();
            noArena.Arena.IsAvailable = false;
            noArena.Start();
            Assert.IsTrue(noArena.Pattern.IsStartRejected);

            Run zeroRadius = new Run();
            zeroRadius.Arena.Radius = 0f;
            zeroRadius.Start();
            Assert.IsTrue(zeroRadius.Pattern.IsStartRejected);

            Run dead = new Run();
            dead.Target.LifeState = EParticipantLifeState.Dead;
            dead.Start();
            Assert.IsTrue(dead.Pattern.IsStartRejected);

            Run summon = new Run();
            summon.Target.IsSummon = true;
            summon.Start();
            Assert.IsTrue(summon.Pattern.IsStartRejected);

            Run unknown = new Run();
            unknown.Start(selectedTargetId: 999);
            Assert.IsTrue(unknown.Pattern.IsStartRejected);

            Assert.AreEqual(0, noArena.Warp.WarpCalls.Count + zeroRadius.Warp.WarpCalls.Count + dead.Warp.WarpCalls.Count
                + summon.Warp.WarpCalls.Count + unknown.Warp.WarpCalls.Count);
        }

        /****************************************
        *      Telegraph: Before And After Warp
        ****************************************/

        [Test]
        public void Start_NoTelegraphBeforeWarp_ThenTelegraphAtActualLandedPosition()
        {
            Run r = new Run();
            r.Warp.SnapOffset = new Vector3(0.25f, 0f, 0f);
            AssertNear(Origin, r.Self.Position);

            r.Start();

            CollectionAssert.AreEqual(new[] { "stand", "path", "warp", "telegraph" }, r.Log, "착지 뒤에야 예고를 알린다");
            Vector3 landed = new Vector3(0.25f, 0f, 5f);
            AssertNear(landed, r.Self.Position, "예고 뒤 보스 위치");
            AssertNear(landed, r.Listener.Center, "서버가 돌려준 실제 위치가 중심이다");
            Assert.AreEqual(RADIUS, r.Listener.Radius, TOLERANCE);
            Assert.AreEqual(1.5f, r.Listener.Seconds, TOLERANCE);
            Assert.AreEqual(1, r.Listener.StartedCount);
        }

        [Test]
        public void Tick_BossMovesDuringTelegraph_StillStrikesAtLandedCenter()
        {
            Run r = new Run();
            FakeCombatTarget nearLanding = r.AddPlayer(11, new Vector3(0f, 0f, 3f));
            FakeCombatTarget nearOrigin = r.AddPlayer(12, new Vector3(1f, 0f, 0f));
            r.Start();

            r.Self.Position = new Vector3(30f, 0f, 0f);
            r.Fsm.Tick(0.75);
            r.Fsm.Tick(TELEGRAPH);

            Assert.IsTrue(HasTarget(r.Resolver, 11));
            Assert.IsFalse(HasTarget(r.Resolver, 12), "워프 전 위치 주변은 맞지 않는다");
            AssertNear(new Vector3(0f, 0f, 5f), r.Resolver.Requests[0].SourcePosition);
            Assert.IsNotNull(nearLanding);
            Assert.IsNotNull(nearOrigin);
        }

        /****************************************
        *     Strike: Targets, Damage, Timing
        ****************************************/

        [Test]
        public void Tick_BeforeTelegraphEnds_DoesNotStrike()
        {
            Run r = new Run();
            r.Start();

            r.Fsm.Tick(1.0);
            r.Fsm.Tick(1.4999);

            Assert.AreEqual(EBossAttackState.Telegraph, r.Fsm.State);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.HitCalls);
        }

        [Test]
        public void Tick_AtTelegraphEnd_StrikesAllPlayersInRangeOnceWithGuardableDamage()
        {
            Run r = new Run();
            r.AddPlayer(11, new Vector3(2f, 0f, 5f));
            r.AddPlayer(12, new Vector3(-3f, 0f, 3f));
            r.AddPlayer(13, new Vector3(0f, 0f, 30f));
            r.Start();

            r.Fsm.Tick(TELEGRAPH);

            Assert.AreEqual(3, r.Resolver.Requests.Count, "대상 포함 범위 안 세 명");
            Assert.IsFalse(HasTarget(r.Resolver, 13));
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                Assert.AreEqual(DAMAGE_REF * HREF, request.RawDamage, TOLERANCE);
                Assert.IsFalse(request.IsUnblockable, "반격 가능");
                Assert.AreEqual(r.Self.CombatId, request.AttackerId);
                Assert.AreEqual(P07_ID, request.SourceDefinitionId);
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
        public void Tick_RecoveryIsOneSecondThenNextSelectionCanStart()
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
        public void Tick_HugeStep_StrikesExactlyOnceAndFinishes()
        {
            Run r = new Run();
            r.Start();

            r.Fsm.Tick(100.0);
            r.Fsm.Tick(101.0);

            Assert.AreEqual(1, r.Resolver.Requests.Count);
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
        *     Real DamageResolver: Guard Rules
        ****************************************/

        [Test]
        public void Strike_WithRealResolver_FollowsGuardRulesAndRearHitFromBehindWarp()
        {
            CombatTargetRegistry registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            HitLedger ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            DamageResolver resolver = new DamageResolver(registry, ledger, 1000f, 0.6f);

            // 대상은 보스에서 멀어지는 쪽을 보며 방어 중이다, 보스는 그 뒤에 착지하므로 후방 피격이라 막지 못한다
            GuardPlayer target = new GuardPlayer { CombatId = registry.IssueCombatId(), Position = TargetSpot,
                Forward = Vector3.forward, IsGuardingFront = true };
            // 착지 지점을 보며 방어 중인 플레이어는 정면 피격이라 막는다
            GuardPlayer guarded = new GuardPlayer { CombatId = registry.IssueCombatId(), Position = new Vector3(2f, 0f, 5f),
                Forward = new Vector3(-1f, 0f, 0f), IsGuardingFront = true };
            GuardPlayer open = new GuardPlayer { CombatId = registry.IssueCombatId(), Position = new Vector3(-2f, 0f, 5f),
                Forward = new Vector3(1f, 0f, 0f), IsGuardingFront = false };
            GuardPlayer outside = new GuardPlayer { CombatId = registry.IssueCombatId(), Position = new Vector3(0f, 0f, 12f),
                Forward = Vector3.back, IsGuardingFront = false };
            Assert.IsTrue(registry.Register(target));
            Assert.IsTrue(registry.Register(guarded));
            Assert.IsTrue(registry.Register(open));
            Assert.IsTrue(registry.Register(outside));

            FakeCombatTarget self = new FakeCombatTarget { CombatId = registry.IssueCombatId(), Faction = ECombatFaction.Enemy, Position = Origin };
            FakeWarp warp = new FakeWarp { Body = self };
            BossWarpBlastPattern pattern = new BossWarpBlastPattern(HREF, warp, new FakeArena());
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();
            PatternDefinition definition = LoadDefinition();
            BossAttackFsm fsm = new BossAttackFsm();

            resolver.BeginTick(0.0);
            BossPatternContext context = new BossPatternContext(definition, 100, target.CombatId, self, registry, gate);
            fsm.Start(definition, pattern, in context, 0.0);
            resolver.BeginTick(TELEGRAPH);
            fsm.Tick(TELEGRAPH);

            float expectedDamage = DamageRules.ApplyDefense(DAMAGE_REF * HREF, 0f, 1000f, 0.6f);
            Assert.AreEqual(10000f - expectedDamage, target.Health, 0.01f, "후방 피격은 방어해도 맞는다");
            Assert.AreEqual(10000f, guarded.Health, "정면 방어는 막는다");
            Assert.AreEqual(10000f - expectedDamage, open.Health, 0.01f, "방어하지 않으면 맞는다");
            Assert.AreEqual(10000f, outside.Health, "범위 밖은 맞지 않는다");
        }

        /****************************************
        *      Weights, Group And 25 Percent
        ****************************************/

        private static float Weight(float healthRate, EPatternGroup previous, int distanceBand, int recentSlot)
        {
            Assert.IsTrue(LoadCatalog().TryGetBoss(WOLBAEK_ID, out BossDefinition boss));
            return BossPatternSelector.ComputeSecondPhaseWeight(LoadDefinition(), boss, healthRate, previous, distanceBand, 1, 1, recentSlot);
        }

        [Test]
        public void Weight_NearIsZeroAndMidFarUseBaseWeight()
        {
            Assert.AreEqual(0f, Weight(0.6f, EPatternGroup.None, 0, 3), TOLERANCE);
            Assert.AreEqual(6.5f, Weight(0.6f, EPatternGroup.None, 1, 3), TOLERANCE);
            Assert.AreEqual(6.5f, Weight(0.6f, EPatternGroup.None, 2, 3), TOLERANCE);
        }

        [Test]
        public void Weight_AtTwentyFivePercent_UsesEnhancedBaseAndFlowFromPressureGroup()
        {
            Assert.AreEqual(7f * 1.35f, Weight(0.25f, EPatternGroup.Large, 1, 3), TOLERANCE);
            Assert.AreEqual(6.5f * 1.25f, Weight(0.2501f, EPatternGroup.Large, 1, 3), TOLERANCE);
            Assert.AreEqual(6.5f * 0.6f, Weight(0.6f, EPatternGroup.Pressure, 1, 3), TOLERANCE);
        }

        [Test]
        public void Weight_RecentUse_FollowsEasyPhaseTwoRule()
        {
            Assert.AreEqual(0f, Weight(0.6f, EPatternGroup.None, 1, 0), TOLERANCE);
            Assert.AreEqual(6.5f * 0.3f, Weight(0.6f, EPatternGroup.None, 1, 1), TOLERANCE);
            Assert.AreEqual(6.5f * 0.7f, Weight(0.6f, EPatternGroup.None, 1, 2), TOLERANCE);
            Assert.AreEqual(6.5f, Weight(0.6f, EPatternGroup.None, 1, 3), TOLERANCE);
        }

        /****************************************
        *     Brain: Selection, Reject, Retry
        ****************************************/

        // 2페이즈 이지 후보를 P07 하나만 남기고(P01~P06 제외) 실제 카탈로그와 실제 팩토리로 두뇌를 돌린다
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

            // keepCircle이 참이면 보스 중심 원형 폭발(P06)을 남겨 P07이 거절됐을 때 고를 대안으로 쓴다
            public BrainRig(bool keepCircle = false)
            {
                Query.Targets.Add(Player);
                Warp.Body = Self;

                WolbaekPatternFactory inner = new WolbaekPatternFactory(HREF, circleListener: Listener, arena: Arena, warp: Warp);
                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog()),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = new ExcludingPatternFactory(inner, keepCircle
                        ? new[]
                        {
                            BossPatternCatalogSO.WOLBAEK_P2_P01_ID, BossPatternCatalogSO.WOLBAEK_P2_P02_ID,
                            BossPatternCatalogSO.WOLBAEK_P2_P03_ID, BossPatternCatalogSO.WOLBAEK_P2_P04_ID,
                            BossPatternCatalogSO.WOLBAEK_P2_P05_ID
                        }
                        : new[]
                        {
                            BossPatternCatalogSO.WOLBAEK_P2_P01_ID, BossPatternCatalogSO.WOLBAEK_P2_P02_ID,
                            BossPatternCatalogSO.WOLBAEK_P2_P03_ID, BossPatternCatalogSO.WOLBAEK_P2_P04_ID,
                            BossPatternCatalogSO.WOLBAEK_P2_P05_ID, BossPatternCatalogSO.WOLBAEK_P2_P06_ID
                        }),
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
        public void Brain_FarTarget_SelectsP07AndWarpsBehindTarget()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            Assert.AreEqual(EBossPhaseState.Active, r.Brain.PhaseState);

            Assert.IsTrue(r.TickUntilAttack(0.5, 10.0));

            Assert.AreEqual(P07_ID, r.Brain.ActivePatternId);
            AssertNear(new Vector3(0f, 0f, 5f), r.Self.Position);
            AssertNear(new Vector3(0f, 0f, 5f), r.Listener.Center);
        }

        [Test]
        public void Brain_TargetNearerThanFiveMeters_NeverSelectsP07_FiveMetersDoes()
        {
            BrainRig near = new BrainRig();
            near.Player.Position = new Vector3(0f, 0f, 4.99f);
            near.TransitionAndHandoff();
            Assert.IsFalse(near.TickUntilAttack(0.5, 20.0));
            Assert.AreEqual(0, near.Warp.WarpCalls.Count);

            BrainRig exact = new BrainRig();
            exact.Player.Position = new Vector3(0f, 0f, 5f);
            exact.TransitionAndHandoff();
            Assert.IsTrue(exact.TickUntilAttack(0.5, 10.0));
            Assert.AreEqual(P07_ID, exact.Brain.ActivePatternId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Brain_NoLandingAnywhere_RejectsWithoutRecordAndRetriesOnlyAtRetryInterval()
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

            // 직전 사용 배율이 0이므로 거절이 사용 기록을 남겼다면 이후 유효 착지가 생겨도 다시 뽑히지 않는다
            r.Warp.CanStand = point => true;
            Assert.IsTrue(r.TickUntilAttack(2.0, 4.0), "기록 없이 거절됐으므로 유효해지면 바로 선택된다");
            Assert.AreEqual(P07_ID, r.Brain.ActivePatternId);
        }

        // 착지 후보가 모두 실패하면 같은 틱에 P07을 뺀 다른 패턴(P06)이 시작한다. 거절된 P07은 보스를 옮기지도 예고하지도 않는다
        [Test]
        public void Brain_NoLandingAnywhere_ReselectsAnotherPatternInTheSameTick()
        {
            BrainRig r = new BrainRig(keepCircle: true);
            r.Random.Roll = 0.999f; // 마지막 양수 가중치(P07)를 먼저 뽑는다
            r.Warp.CanStand = point => false;
            r.TransitionAndHandoff();

            r.Brain.Tick(0.5);

            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_P2_P06_ID, r.Brain.ActivePatternId, "같은 틱에 대안이 시작한다");
            Assert.AreEqual(MAX_CANDIDATES, r.Warp.StandCalls.Count, "P07은 후보 8곳을 한 번만 검사했다");
            Assert.AreEqual(0, r.Warp.WarpCalls.Count);
            AssertNear(Origin, r.Self.Position, "보스는 워프하지 않았다");
            Assert.AreEqual(1, r.Listener.StartedCount, "예고는 대안의 것 하나뿐이다");
            AssertNear(Origin, r.Listener.Center, "대안 예고는 옮기지 않은 보스 위치다");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Brain_AfterUse_RecentZeroKeepsRequeryingWithoutNewWarps()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            Assert.IsTrue(r.TickUntilAttack(0.5, 10.0));

            for (double t = 3.0; t < 30.0; t += 0.25)
            {
                r.Self.Position = Origin;
                r.Brain.Tick(t);
            }

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, r.Warp.WarpCalls.Count, "직전 사용 배율 0이라 같은 패턴이 바로 다시 뽑히지 않는다");
        }
    }
}
