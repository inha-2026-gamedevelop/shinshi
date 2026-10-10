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

using EPatternGroup     = Shinshi.Data.BossPatternCatalogSO.EPatternGroup;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 월백 하드 2페이즈 P06 13개 동시 원형 폭발을 에셋의 실제 정의와 실제 팩토리, 실제 공격 FSM으로 확인한다
    // 실제 서버 틱, 복제, 화면, 실제 몸체와 장애물은 거치지 않는다. 전장 중심과 반지름은 고정 전장 입력으로 넣는다
    // 피해, 시간, 형상 수치는 Play Mode와 다인 접속 검증 전의 초기값이라 값 자체의 정답을 보장하지 않는다
    //
    // 기본 좌표계: 전장 중심은 원점이 아닌 ArenaCenter이고, 보스는 일부러 전장 중심에서 벗어난 BossPosition에 선다
    // 그래서 보스 중심 원과 사분면 원을 혼동하면 테스트가 깨진다. Rel(x, z)는 전장 중심 기준 월드 축 좌표다
    // 시각(시전 시작 기준): 13개 원이 모두 t=2.0에 판정하고, 회복 1초 뒤 t=3.0에 다음 선택이 열린다
    public class BossWolbaekHardPhaseTwoCircleBurstTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string HARD_ID      = BossPatternCatalogSO.WOLBAEK_HARD_P2_P06_ID;
        private const string EASY_ID      = BossPatternCatalogSO.WOLBAEK_P2_P06_ID;
        private const float  HREF         = 1000f;
        private const float  RAW_DAMAGE   = 0.28f * HREF;
        private const float  TOLERANCE    = 0.001f;
        private const double TIME_TOL     = 0.0001;
        private const float  ARENA_RADIUS = 18f;
        private const int    CIRCLES      = 13;
        private const float  BOSS_RADIUS  = 5.5f;
        private const float  SPOT_RADIUS  = 3f;
        private const float  NEAR         = 5f;
        private const float  FAR          = 11f;

        // 설계 시각(전부 정확한 이진 분수라 FSM 경계와 오차가 없다)
        private const double STRIKE_TIME    = 2.0;
        private const double NEXT_SELECTION = 3.0;

        private static readonly Vector3 ArenaCenter  = new Vector3(50f, 0f, -30f);
        private static readonly Vector3 BossPosition = new Vector3(41f, 0f, -27f); // 전장 중심에서 (-9, +3)

        /****************************************
        *               Fakes
        ****************************************/

        private struct QueryCall
        {
            public Vector3 Center;
            public float   Radius;
        }

        private sealed class BurstQuery : ICombatTargetQuery
        {
            public List<ICombatTarget> Targets { get; } = new List<ICombatTarget>();
            public List<QueryCall>     Calls   { get; } = new List<QueryCall>();

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
                Calls.Add(new QueryCall { Center = center, Radius = radius });

                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    ICombatTarget target = Targets[i];
                    Vector3 offset = target.Position - center;
                    offset.y = 0f;
                    if ((target.LifeState == EParticipantLifeState.Alive) && (target.Faction == faction)
                        && (includeSummons || (!target.IsSummon)) && (offset.magnitude <= radius))
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

        // 대상별로 정해 둔 결과를 돌려준다. 정하지 않은 대상은 적용이다
        private sealed class ScriptedResolver : IDamageResolver
        {
            private int _nextId = 1;

            public List<DamageRequest>             Requests { get; } = new List<DamageRequest>();
            public Dictionary<int, EDamageOutcome> Outcomes { get; } = new Dictionary<int, EDamageOutcome>();

            public DamageResult Apply(in DamageRequest request)
            {
                Requests.Add(request);
                if (!Outcomes.TryGetValue(request.HitTargetId, out EDamageOutcome outcome))
                {
                    return DamageResult.Applied(1f, false, false);
                }

                switch (outcome)
                {
                    case EDamageOutcome.Applied:
                        return DamageResult.Applied(1f, false, false);
                    case EDamageOutcome.Guarded:
                        return DamageResult.Guarded();
                    default:
                        return DamageResult.Ignored(outcome);
                }
            }

            public int IssueInstanceId()
            {
                int issued = _nextId;
                ++_nextId;
                return issued;
            }
        }

        private struct Strike
        {
            public double Scheduled;
            public double Processed;
            public int    HitCount;
        }

        private struct Cancel
        {
            public bool                  Struck;
            public EBossInterruptReason Reason;
        }

        private sealed class RecordingListener : IBossCircleBurstListener
        {
            public List<int>                 StartedCasts   = new List<int>();
            public List<BossCircleBurstLayout> StartedLayouts = new List<BossCircleBurstLayout>();
            public List<double>              StartedAt      = new List<double>();
            public List<float>               StrikeSeconds  = new List<float>();
            public List<Strike>              Strikes        = new List<Strike>();
            public List<Cancel>              Cancels        = new List<Cancel>();

            public void OnBurstStarted(int castInstanceId, in BossCircleBurstLayout layout, double castStartServerTime,
                float strikeSeconds)
            {
                StartedCasts.Add(castInstanceId);
                StartedLayouts.Add(layout);
                StartedAt.Add(castStartServerTime);
                StrikeSeconds.Add(strikeSeconds);
            }

            public void OnBurstStruck(int castInstanceId, double scheduledServerTime, double processedServerTime, int hitCount)
            {
                Strikes.Add(new Strike { Scheduled = scheduledServerTime, Processed = processedServerTime, HitCount = hitCount });
            }

            public void OnBurstCancelled(int castInstanceId, bool struck, EBossInterruptReason reason)
            {
                Cancels.Add(new Cancel { Struck = struck, Reason = reason });
            }
        }

        // 전장 정보를 얻지 못하는 입력
        private sealed class MissingArena : IBossArenaSource
        {
            public bool TryGetArena(out Vector3 center, out float radius)
            {
                center = Vector3.zero;
                radius = 0f;
                return false;
            }
        }

        // 공격 FSM에 실제 팩토리가 만든 패턴을 올린다. 기본 대상이 없어 요청 수는 이 테스트가 놓은 대상만 센다
        private sealed class Run
        {
            public FakeCombatTarget    Self      = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossPosition };
            public BurstQuery          Query     = new BurstQuery();
            public ScriptedResolver    Resolver  = new ScriptedResolver();
            public RecordingListener   Listener  = new RecordingListener();
            public BossFixedArena      Arena     = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossDamageGate      Gate;
            public BossAttackFsm       Fsm       = new BossAttackFsm();
            public PatternDefinition   Definition;
            public BossCircleBurstPattern Pattern;
            public int                 CastId;

            public Run()
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = LoadDefinition(HARD_ID);
                CastId     = Gate.IssueInstanceId();
            }

            public FakeCombatTarget Add(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget target = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player,
                    Position = position, IsSummon = isSummon };
                Query.Targets.Add(target);
                return target;
            }

            // 전장 중심 기준 월드 축 좌표에 놓는다
            public FakeCombatTarget AddRel(int id, float x, float z)
            {
                return Add(id, Rel(x, z));
            }

            public void Start(double time = 0.0)
            {
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, arena: Arena, burstListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                Pattern = (BossCircleBurstPattern)pattern;
                BossPatternContext context = new BossPatternContext(Definition, CastId, CombatIds.NONE, Self, Query, Gate,
                    new FakeBossRandom());
                Assert.IsTrue(Fsm.Start(Definition, pattern, in context, time));
            }

            public void TickAt(double time)
            {
                Fsm.Tick(time);
            }

            public void TickRange(double from, double to, double step = 0.1)
            {
                for (double t = from; t <= to + 0.000001; t += step)
                {
                    TickAt(t);
                }
            }

            public int Hits(int targetId)
            {
                int count = 0;
                for (int i = 0; i < Resolver.Requests.Count; ++i)
                {
                    count += (Resolver.Requests[i].HitTargetId == targetId) ? 1 : 0;
                }

                return count;
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

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

        // 전장 중심 기준 월드 축 좌표 (x, z)의 월드 위치
        private static Vector3 Rel(float x, float z)
        {
            return ArenaCenter + new Vector3(x, 0f, z);
        }

        private static BossCircleBurstLayout MakeLayout()
        {
            return new BossCircleBurstLayout(BossPosition, ArenaCenter, BOSS_RADIUS, SPOT_RADIUS, NEAR, FAR);
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, string label)
        {
            Assert.AreEqual(expected.x, actual.x, TOLERANCE, label + ".x");
            Assert.AreEqual(expected.y, actual.y, TOLERANCE, label + ".y");
            Assert.AreEqual(expected.z, actual.z, TOLERANCE, label + ".z");
        }

        // 에셋 정의를 바탕으로 동시 폭발 값만 바꾼 정의, 선택 데이터는 에셋과 같은 값이다
        private static PatternDefinition MakeVariant(string id = HARD_ID, float spot = SPOT_RADIUS, float near = NEAR,
            float far = FAR, float range = BOSS_RADIUS, float telegraph = 2f, float active = 0f, bool guardable = true,
            bool isCircle = true)
        {
            EnemyCatalogSO.AttackShapeSpec shape = isCircle
                ? EnemyCatalogSO.AttackShapeSpec.Circle(range)
                : EnemyCatalogSO.AttackShapeSpec.Fan(range, 90f);

            return new PatternDefinition(id, WOLBAEK_ID, 2, 8f,
                new[] { 1.7f, 0.4f, 0f }, new[] { 1f, 1f, 1f }, new[] { 0.5f, 1.6f, 2.4f }, new[] { 0.25f, 0.3f, 0.7f, 1f },
                telegraph, 0f, 0f, active, 1f, shape, 0.28f, guardable,
                group: EPatternGroup.Pressure, enhancedBaseWeight: 9f, difficulty: EBossDifficulty.Hard,
                burstSpotRadius: spot, burstNearOffset: near, burstFarOffset: far);
        }

        /****************************************
        *      Asset, Catalog, Factory
        ****************************************/

        [Test]
        public void Asset_HardP06_HasStartingValuesAndAHardOnlyId()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);

            Assert.AreEqual("wolbaek_hard_ph2_p06", p.DefinitionId);
            Assert.AreEqual(HARD_ID + ".name", p.DisplayKey);
            Assert.AreEqual(EBossDifficulty.Hard, p.Difficulty);
            Assert.AreEqual(WOLBAEK_ID, p.BossId);
            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EPatternGroup.Pressure, p.Group);
            Assert.AreEqual(EEnemyAttackShape.Circle, p.Shape.Shape);
            Assert.AreEqual(BOSS_RADIUS, p.Shape.Range, TOLERANCE, "보스 중심 원 반경");
            Assert.AreEqual(2f, p.TelegraphSeconds, TOLERANCE, "예고 t=0~2.0, 판정 t=2.0");
            Assert.AreEqual(0f, p.ActiveSeconds, TOLERANCE, "즉발");
            Assert.AreEqual(1f, p.RecoverySeconds, TOLERANCE, "다음 선택 t=3.0");
            Assert.AreEqual(0f, p.TrackingSeconds, "예고 중 조준을 따라가지 않는다");
            Assert.AreEqual(0.28f, p.DamageRef, TOLERANCE);
            Assert.IsTrue(p.Guardable, "방어와 반격 가능");
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(SPOT_RADIUS, p.BurstSpotRadius, TOLERANCE, "사분면 원 반경");
            Assert.AreEqual(NEAR, p.BurstNearOffset, TOLERANCE);
            Assert.AreEqual(FAR, p.BurstFarOffset, TOLERANCE);
            Assert.AreEqual(3f, p.TelegraphSeconds + p.ActiveSeconds + p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(0, p.MaxConcurrentAreas, "장판 상한으로 후보에서 빠지지 않는다");
            Assert.AreEqual(0, p.RawProjectileCount);
        }

        [Test]
        public void Asset_HardP06_SelectionDataFollowsEasyP06WithTheHardRecentTable()
        {
            PatternDefinition hard = LoadDefinition(HARD_ID);
            PatternDefinition easy = LoadDefinition(EASY_ID);

            Assert.AreEqual(easy.BaseWeight, hard.BaseWeight);
            Assert.AreEqual(easy.EnhancedBaseWeight, hard.EnhancedBaseWeight);
            Assert.AreEqual(easy.Group, hard.Group);
            BossWolbaekHardDataKit.AssertSameTable(easy.DistanceMultipliers, hard.DistanceMultipliers, "distance");
            BossWolbaekHardDataKit.AssertSameTable(easy.DirectionMultipliers, hard.DirectionMultipliers, "direction");
            BossWolbaekHardDataKit.AssertSameTable(easy.NearbyMultipliers, hard.NearbyMultipliers, "nearby");
            BossWolbaekHardDataKit.AssertTable(BossWolbaekHardDataKit.G_HardRecent, hard.RecentUseMultipliers, "recent");
            Assert.AreEqual(0.25f, hard.RecentUseMultipliers[0], TOLERANCE, "직전 사용 0.25");
            Assert.AreEqual(0.3f, hard.RecentUseMultipliers[1], TOLERANCE);
            Assert.AreEqual(0.7f, hard.RecentUseMultipliers[2], TOLERANCE);
            Assert.AreEqual(1f, hard.RecentUseMultipliers[3], TOLERANCE);
            Assert.AreEqual(easy.DamageRef, hard.DamageRef, "피해 0.28Href는 이지와 같다");
            Assert.AreEqual(easy.Guardable, hard.Guardable);
        }

        [Test]
        public void Asset_EasyP06_IsUnchanged_AndKeepsTheSingleBossCircle()
        {
            PatternDefinition easy = LoadDefinition(EASY_ID);

            Assert.AreEqual(EBossDifficulty.Easy, easy.Difficulty);
            Assert.AreEqual(EEnemyAttackShape.Circle, easy.Shape.Shape);
            Assert.AreEqual(5f, easy.Shape.Range, TOLERANCE);
            Assert.AreEqual(1.2f, easy.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0f, easy.ActiveSeconds, TOLERANCE);
            Assert.AreEqual(0.8f, easy.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(0.28f, easy.DamageRef, TOLERANCE);
            Assert.AreEqual(0f, easy.BurstSpotRadius, "이지는 사분면 원이 없다");
            Assert.AreEqual(0f, easy.BurstNearOffset);
            Assert.AreEqual(0f, easy.BurstFarOffset);
            Assert.AreEqual(0f, easy.RecentUseMultipliers[0], "이지 최근 사용 배율은 그대로다");
        }

        [Test]
        public void Asset_OnlyHardP06_CarriesBurstValues()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            int count = 0;
            for (int i = 0; i < catalog.Patterns.Count; ++i)
            {
                PatternDefinition p = catalog.Patterns[i];
                if ((p.BurstSpotRadius > 0f) || (p.BurstNearOffset > 0f) || (p.BurstFarOffset > 0f))
                {
                    Assert.AreEqual(HARD_ID, p.DefinitionId);
                    ++count;
                }
            }

            Assert.AreEqual(1, count);
        }

        [Test]
        public void Asset_Catalog_SeparatesDifficultiesStaysValidAndHardPhaseTwoOpensWithSeven()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            var easy = new List<PatternDefinition>();
            var hard = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Easy, easy);
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Hard, hard);

            Assert.AreEqual(7, easy.Count);
            Assert.AreEqual(7, hard.Count);
            Assert.AreEqual(HARD_ID, hard[5].DefinitionId);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_HARD_P2_P07_ID, hard[6].DefinitionId);
            for (int i = 0; i < easy.Count; ++i)
            {
                Assert.AreNotEqual(HARD_ID, easy[i].DefinitionId);
            }

            var errors = new List<string>();
            catalog.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, 2), "이지 2페이즈 준비 결과는 그대로다");
            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID));

            var hardErrors = new List<string>();
            catalog.ValidateTuning(WOLBAEK_ID, 2, EBossDifficulty.Hard, hardErrors);
            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, 2, EBossDifficulty.Hard), "P01~P07 일곱 개가 갖춰져 하드 2페이즈가 열린다");
            Assert.IsEmpty(hardErrors, string.Join("\n", hardErrors));
            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, LoadDefinition(HARD_ID)), "P06 한 개의 데이터 자체는 준비됐다");
        }

        [Test]
        public void Asset_Catalog_HardPhaseOneStaysClosedForPhaseTwoAndEasyAdaptersDoNotSeeP06()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            var easySource = new BossPatternCatalogSource(catalog, EBossDifficulty.Easy);
            var hardSource = new BossPatternCatalogSource(catalog, EBossDifficulty.Hard);

            CollectionAssert.DoesNotContain(BossWolbaekHardDataKit.Collect(easySource, 2), HARD_ID);
            CollectionAssert.Contains(BossWolbaekHardDataKit.Collect(hardSource, 2), HARD_ID);
            CollectionAssert.DoesNotContain(BossWolbaekHardDataKit.Collect(hardSource, 1), HARD_ID);
            Assert.AreEqual(6, BossWolbaekHardDataKit.Collect(hardSource, 1).Count, "하드 1페이즈 6개는 그대로다");
        }

        [Test]
        public void Asset_AllThirteenCirclesFitInsideTheArena()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            Assert.IsTrue(catalog.TryGetBoss(WOLBAEK_ID, out BossPatternCatalogSO.BossDefinition boss));
            PatternDefinition p = LoadDefinition(HARD_ID);

            BossCircleBurstLayout layout = new BossCircleBurstLayout(ArenaCenter, ArenaCenter, p.Shape.Range,
                p.BurstSpotRadius, p.BurstNearOffset, p.BurstFarOffset);
            for (int i = 1; i < layout.Count; ++i)
            {
                Vector3 offset = layout.GetCenter(i) - ArenaCenter;
                Assert.LessOrEqual(offset.magnitude + layout.GetRadius(i), boss.ArenaDiameter * 0.5f + TOLERANCE, "원 " + i);
            }
        }

        [Test]
        public void Factory_NeedsTheArenaAndTheBurstListener_AndReadinessAgrees()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            RecordingListener listener = new RecordingListener();

            Assert.IsFalse(new WolbaekPatternFactory(HREF).CanCreate(p));
            Assert.IsNull(new WolbaekPatternFactory(HREF).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, arena: arena).CanCreate(p), "알림을 받을 쪽이 없으면 보이지 않는 피해가 나가므로 만들지 않는다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, arena: arena).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, burstListener: listener).CanCreate(p), "전장 입력이 없으면 만들 수 없다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, burstListener: listener).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, arena: arena, hardAreaListener: null, chainFanListener: new NoChainListener()).CanCreate(p),
                "다른 하드 알림을 연결해도 P06 알림이 아니면 만들지 않는다");
            Assert.IsTrue(new WolbaekPatternFactory(HREF, arena: arena, burstListener: listener).CanCreate(p));
            Assert.IsInstanceOf<BossCircleBurstPattern>(new WolbaekPatternFactory(HREF, arena: arena, burstListener: listener).Create(p));
        }

        // 연쇄 알림만 연결한 구성을 흉내 내는 빈 구현이다
        private sealed class NoChainListener : IBossChainFanListener
        {
            public void OnChainFanStarted(int castInstanceId, in BossChainFanLayout layout, double castStartServerTime,
                float safeRevealSeconds, float firstBlastSeconds, float blastIntervalSeconds, float lingerSeconds)
            {
            }

            public void OnChainFanBlasted(int castInstanceId, int zoneIndex, double scheduledServerTime,
                double processedServerTime, int hitCount)
            {
            }

            public void OnChainFanDangerEnded(int castInstanceId, double scheduledServerTime)
            {
            }

            public void OnChainFanCancelled(int castInstanceId, int blastedCount, bool dangerEnded, EBossInterruptReason reason)
            {
            }
        }

        [Test]
        public void Factory_EasyP06_StillMakesTheSingleCircleWithoutAnyInput_AndOtherHardPoliciesAreUnchanged()
        {
            PatternDefinition easy = LoadDefinition(EASY_ID);
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);

            Assert.IsTrue(new WolbaekPatternFactory(HREF).CanCreate(easy));
            Assert.IsInstanceOf<BossCircleBlastPattern>(new WolbaekPatternFactory(HREF).Create(easy));
            Assert.IsInstanceOf<BossCircleBlastPattern>(
                new WolbaekPatternFactory(HREF, arena: arena, burstListener: new RecordingListener()).Create(easy),
                "동시 폭발 입력을 연결해도 이지 ID는 13개 원 실행에 연결되지 않는다");

            PatternDefinition p01 = LoadDefinition(BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID);
            PatternDefinition p02 = LoadDefinition(BossPatternCatalogSO.WOLBAEK_HARD_P2_P02_ID);
            Assert.IsTrue(new WolbaekPatternFactory(HREF, arena: arena).CanCreate(p01), "하드 P01은 전장 입력만 있으면 된다");
            Assert.IsFalse(new WolbaekPatternFactory(HREF, arena: arena, burstListener: new RecordingListener()).CanCreate(p02),
                "하드 P02는 격자 알림이 필요하다");
        }

        [Test]
        public void Validate_BurstFields_RejectBrokenVariantsAndAcceptTheBaseline()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant()), "기준 변형은 에셋과 같아 준비된다");
            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(spot: 2.5f, near: 6f, far: 12f)), "반경과 좌표 크기는 밸런스 값이다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(spot: 0f)), "사분면 원 반경이 없다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(near: 0f)), "안쪽 좌표가 없다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(far: 0f)), "바깥쪽 좌표가 없다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(far: NEAR)), "바깥쪽이 안쪽과 같으면 한 사분면의 원이 겹쳐 놓인다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(far: 4f)), "바깥쪽이 안쪽보다 작다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(far: 15f)), "가장 먼 원이 전장 반지름 18m를 넘는다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(active: 0.5f)), "즉발이 아니다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(guardable: false)), "방어 가능이 아니다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(isCircle: false)), "원형 형태가 아니다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(range: 0f)), "보스 중심 원 반경이 없다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(telegraph: 0f)), "예고 시간이 없다");
            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, MakeVariant(id: "x_other_burst")),
                "하드 P06 ID가 아닌 정의는 동시 폭발 값만 검사한다");
        }

        /****************************************
        *      Layout: 13 Circles
        ****************************************/

        [Test]
        public void Layout_HasThirteenCircles_TheFirstOneIsCenteredOnTheBossNotTheArena()
        {
            BossCircleBurstLayout layout = MakeLayout();

            Assert.AreEqual(CIRCLES, layout.Count);
            Assert.AreEqual(CIRCLES, BossCircleBurstLayout.CIRCLE_COUNT);
            AssertVector(BossPosition, layout.GetCenter(0), "boss");
            Assert.AreEqual(BOSS_RADIUS, layout.GetRadius(0), TOLERANCE);
            Assert.AreNotEqual(0f, (layout.GetCenter(0) - ArenaCenter).magnitude, "시험 배치는 보스가 전장 중심에서 벗어나 있다");
        }

        [Test]
        public void Layout_QuadrantCircles_UseAllFourSignCombinationsOfTheThreeFixedPositions()
        {
            BossCircleBurstLayout layout = MakeLayout();
            var expected = new List<Vector3>();
            float[] signs = { 1f, -1f };
            for (int sx = 0; sx < signs.Length; ++sx)
            {
                for (int sz = 0; sz < signs.Length; ++sz)
                {
                    expected.Add(Rel(signs[sx] * NEAR, signs[sz] * NEAR));
                    expected.Add(Rel(signs[sx] * FAR, signs[sz] * NEAR));
                    expected.Add(Rel(signs[sx] * NEAR, signs[sz] * FAR));
                }
            }

            Assert.AreEqual(12, expected.Count);
            for (int i = 1; i < layout.Count; ++i)
            {
                Assert.AreEqual(SPOT_RADIUS, layout.GetRadius(i), TOLERANCE, "원 " + i);

                int found = -1;
                for (int k = 0; k < expected.Count; ++k)
                {
                    if ((layout.GetCenter(i) - expected[k]).magnitude < TOLERANCE)
                    {
                        found = k;
                        break;
                    }
                }

                Assert.GreaterOrEqual(found, 0, "원 " + i + " 위치가 설계 좌표에 없다 " + layout.GetCenter(i));
                expected.RemoveAt(found);
            }

            Assert.AreEqual(0, expected.Count, "12개 좌표가 한 번씩 모두 쓰였다");
        }

        [Test]
        public void Layout_EachQuadrantHoldsTheThreeCirclesInOrderNearNearFarNearNearFar()
        {
            BossCircleBurstLayout layout = MakeLayout();

            for (int quadrant = 0; quadrant < BossCircleBurstLayout.QUADRANT_COUNT; ++quadrant)
            {
                Vector3 a = layout.GetCenter(1 + (quadrant * 3)) - ArenaCenter;
                Vector3 b = layout.GetCenter(2 + (quadrant * 3)) - ArenaCenter;
                Vector3 c = layout.GetCenter(3 + (quadrant * 3)) - ArenaCenter;

                Assert.AreEqual(NEAR, Mathf.Abs(a.x), TOLERANCE);
                Assert.AreEqual(NEAR, Mathf.Abs(a.z), TOLERANCE);
                Assert.AreEqual(FAR, Mathf.Abs(b.x), TOLERANCE);
                Assert.AreEqual(NEAR, Mathf.Abs(b.z), TOLERANCE);
                Assert.AreEqual(NEAR, Mathf.Abs(c.x), TOLERANCE);
                Assert.AreEqual(FAR, Mathf.Abs(c.z), TOLERANCE);
                Assert.AreEqual(Mathf.Sign(a.x), Mathf.Sign(b.x), "한 사분면은 x 부호가 같다");
                Assert.AreEqual(Mathf.Sign(a.x), Mathf.Sign(c.x));
                Assert.AreEqual(Mathf.Sign(a.z), Mathf.Sign(b.z), "한 사분면은 z 부호가 같다");
                Assert.AreEqual(Mathf.Sign(a.z), Mathf.Sign(c.z));
            }
        }

        [Test]
        public void Layout_QuadrantCirclesFollowTheArenaCenterAndIgnoreTheBoss()
        {
            Vector3 elsewhere = new Vector3(-7f, 3f, 99f);
            BossCircleBurstLayout atBoss  = new BossCircleBurstLayout(BossPosition, ArenaCenter, BOSS_RADIUS, SPOT_RADIUS, NEAR, FAR);
            BossCircleBurstLayout moved   = new BossCircleBurstLayout(elsewhere, ArenaCenter, BOSS_RADIUS, SPOT_RADIUS, NEAR, FAR);
            BossCircleBurstLayout shifted = new BossCircleBurstLayout(BossPosition, ArenaCenter + new Vector3(4f, 0f, 2f),
                BOSS_RADIUS, SPOT_RADIUS, NEAR, FAR);

            for (int i = 1; i < CIRCLES; ++i)
            {
                AssertVector(atBoss.GetCenter(i), moved.GetCenter(i), "보스 위치가 달라도 원 " + i);
                AssertVector(atBoss.GetCenter(i) + new Vector3(4f, 0f, 2f), shifted.GetCenter(i), "전장 중심을 따라 원 " + i);
            }

            AssertVector(elsewhere, moved.GetCenter(0), "보스 중심 원만 보스를 따른다");
        }

        [Test]
        public void Layout_Contains_BoundaryIsInsideAndTheBlindSpotsAreSafe()
        {
            BossCircleBurstLayout layout = MakeLayout();

            Assert.IsTrue(layout.Contains(0, BossPosition + new Vector3(BOSS_RADIUS, 0f, 0f)), "경계 위는 안이다");
            Assert.IsFalse(layout.Contains(0, BossPosition + new Vector3(BOSS_RADIUS + 0.01f, 0f, 0f)));
            Assert.IsTrue(layout.Contains(0, BossPosition + new Vector3(0f, 80f, 0f)), "높이는 거리에 넣지 않는다");
            Assert.IsTrue(layout.Contains(1, Rel(NEAR - SPOT_RADIUS, NEAR)));
            Assert.IsFalse(layout.Contains(1, Rel(NEAR - SPOT_RADIUS - 0.01f, NEAR)));
            Assert.IsFalse(layout.ContainsAny(ArenaCenter), "전장 중심은 어느 원에도 없다");
            Assert.IsFalse(layout.ContainsAny(Rel(15f, 0f)));
            Assert.IsTrue(layout.ContainsAny(Rel(8f, 5f)), "이웃한 두 원이 맞닿는 점은 위험이다");
        }

        [Test]
        public void Layout_IndexOutsideThirteen_Throws()
        {
            BossCircleBurstLayout layout = MakeLayout();

            Assert.Throws<System.ArgumentOutOfRangeException>(() => layout.GetCenter(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => layout.GetCenter(CIRCLES));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => layout.GetRadius(CIRCLES));
        }

        /****************************************
        *      Start, Strike Time
        ****************************************/

        [Test]
        public void Start_AnnouncesAllThirteenCirclesOnceWithTheCastStartAndStrikeSeconds()
        {
            Run r = new Run();
            r.Start(3.0);

            Assert.AreEqual(1, r.Listener.StartedCasts.Count);
            Assert.AreEqual(r.CastId, r.Listener.StartedCasts[0]);
            Assert.AreEqual(3.0, r.Listener.StartedAt[0], TIME_TOL);
            Assert.AreEqual(2f, r.Listener.StrikeSeconds[0], TOLERANCE);

            BossCircleBurstLayout layout = r.Listener.StartedLayouts[0];
            Assert.AreEqual(CIRCLES, layout.Count);
            AssertVector(BossPosition, layout.BossCenter, "보스 중심 원은 시전 시작의 보스 위치");
            AssertVector(ArenaCenter, layout.ArenaCenter, "사분면 원의 원점은 전장 중심");
            Assert.AreEqual(BOSS_RADIUS, layout.BossRadius, TOLERANCE);
            Assert.AreEqual(SPOT_RADIUS, layout.SpotRadius, TOLERANCE);
            Assert.AreEqual(5f, layout.NearOffset, TOLERANCE);
            Assert.AreEqual(11f, layout.FarOffset, TOLERANCE);
            Assert.AreEqual(5.0, r.Pattern.StrikeTime, TIME_TOL, "시작 3.0 + 예고 2.0");
            Assert.IsFalse(r.Pattern.IsStartRejected);
            Assert.AreEqual(0, r.Resolver.Requests.Count, "예고 중에는 피해가 없다");
            Assert.AreEqual(0, r.Listener.Strikes.Count);
        }

        [Test]
        public void Strike_NothingBeforeTheBoundary_AllCirclesExactlyAtTwoSeconds()
        {
            Run r = new Run();
            BossCircleBurstLayout layout = MakeLayout();
            for (int i = 0; i < CIRCLES; ++i)
            {
                r.Add(100 + i, layout.GetCenter(i));
            }

            r.Start();
            r.TickRange(0.0, 1.9, 0.1);
            r.TickAt(1.99);
            r.TickAt(1.9999);

            Assert.AreEqual(0, r.Resolver.Requests.Count, "t=2.0 전에는 어느 원도 판정하지 않는다");
            Assert.AreEqual(0, r.Listener.Strikes.Count);

            r.TickAt(STRIKE_TIME);

            Assert.AreEqual(CIRCLES, r.Resolver.Requests.Count, "13개 원이 같은 틱에 한꺼번에 판정한다");
            Assert.AreEqual(1, r.Listener.Strikes.Count);
            Assert.AreEqual(STRIKE_TIME, r.Listener.Strikes[0].Scheduled, TIME_TOL);
            Assert.AreEqual(STRIKE_TIME, r.Listener.Strikes[0].Processed, TIME_TOL);
            Assert.AreEqual(CIRCLES, r.Listener.Strikes[0].HitCount);
            for (int i = 0; i < CIRCLES; ++i)
            {
                Assert.AreEqual(1, r.Hits(100 + i), "원 " + i + " 중심의 대상");
            }
        }

        [Test]
        public void Strike_StartingAtALaterServerTime_StrikesTwoSecondsAfterTheStart()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start(100.0);

            r.TickRange(100.0, 101.95, 0.05);
            Assert.AreEqual(0, r.Resolver.Requests.Count);

            r.TickAt(102.0);
            Assert.AreEqual(1, r.Resolver.Requests.Count);
            Assert.AreEqual(102.0, r.Listener.Strikes[0].Scheduled, TIME_TOL);
        }

        [Test]
        public void Strike_EachCircleAloneHitsATargetAtItsCenter_AndNothingElse()
        {
            BossCircleBurstLayout layout = MakeLayout();
            for (int i = 0; i < CIRCLES; ++i)
            {
                Run r = new Run();
                r.Add(10, layout.GetCenter(i));
                r.Start();
                r.TickRange(0.0, 2.0, 0.5);

                Assert.AreEqual(1, r.Hits(10), "원 " + i);
                Assert.AreEqual(1, r.Resolver.Requests.Count, "원 " + i);
            }
        }

        [Test]
        public void Strike_RangeBoundaries_BossCircleAndQuadrantCircle()
        {
            Run r = new Run();
            // 보스 중심에서 전장 바깥쪽(-x)으로 5.5m, 사분면 원과 겹치지 않는 곳
            r.Add(10, BossPosition + new Vector3(-BOSS_RADIUS, 0f, 0f));
            r.Add(11, BossPosition + new Vector3(-(BOSS_RADIUS + 0.01f), 0f, 0f));
            // 사분면 (+x,-z) 첫 원의 안쪽 경계(3m)와 그 바깥
            r.AddRel(12, NEAR - SPOT_RADIUS, -NEAR);
            r.AddRel(13, NEAR - SPOT_RADIUS - 0.01f, -NEAR);
            r.Start();
            r.TickRange(0.0, 2.0, 0.5);

            Assert.AreEqual(1, r.Hits(10), "보스 중심 원 반경 5.5m 경계는 안");
            Assert.AreEqual(0, r.Hits(11), "5.51m는 밖");
            Assert.AreEqual(1, r.Hits(12), "사분면 원 반경 3m 경계는 안");
            Assert.AreEqual(0, r.Hits(13), "3.01m는 밖");
        }

        [Test]
        public void Strike_TheArenaCenterAndTheGapsAreSafe()
        {
            Run r = new Run();
            r.Add(10, ArenaCenter);
            r.AddRel(11, 15f, 0f);
            r.AddRel(12, 0f, -15f);
            r.AddRel(13, 8f, 0f);
            r.Start();
            r.TickRange(0.0, 3.0, 0.5);

            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.Strikes[0].HitCount);
        }

        [Test]
        public void Strike_QueriesEveryCircleWithItsOwnCenterAndRadius()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();
            r.TickRange(0.0, 2.0, 0.5);

            BossCircleBurstLayout layout = r.Listener.StartedLayouts[0];
            Assert.AreEqual(CIRCLES, r.Query.Calls.Count, "원마다 조회를 한 번씩 한다");
            for (int i = 0; i < CIRCLES; ++i)
            {
                AssertVector(layout.GetCenter(i), r.Query.Calls[i].Center, "조회 중심 " + i);
                Assert.AreEqual(layout.GetRadius(i), r.Query.Calls[i].Radius, TOLERANCE, "조회 반경 " + i);
            }
        }

        [Test]
        public void Strike_SummonsInsideACircleAreHitAndDeadTargetsAreNot()
        {
            Run r = new Run();
            r.Add(10, Rel(-5f, -5f), isSummon: true);
            FakeCombatTarget dead = r.AddRel(11, 5f, 5f);
            dead.LifeState = EParticipantLifeState.Dead;
            r.Start();
            r.TickRange(0.0, 2.0, 0.5);

            Assert.AreEqual(1, r.Hits(10), "소환물도 범위 피해를 받는다");
            Assert.AreEqual(0, r.Hits(11), "사망한 대상은 조회되지 않는다");
        }

        [Test]
        public void Strike_DamageUsesTheDefinitionAndTheReferenceHealthAndIsGuardable()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();
            r.TickRange(0.0, 2.0, 0.5);

            DamageRequest request = r.Resolver.Requests[0];
            Assert.AreEqual(RAW_DAMAGE, request.RawDamage, TOLERANCE);
            Assert.AreEqual(EDamageFlags.None, request.Flags, "방어와 반격이 가능하다");
            Assert.AreEqual(r.Self.CombatId, request.AttackerId);
            Assert.AreEqual(r.CastId, request.CastInstanceId);
            Assert.AreEqual(HARD_ID, request.SourceDefinitionId);
        }

        /****************************************
        *      Intersections, Once Per Cast
        ****************************************/

        [Test]
        public void Intersection_TargetInsideSeveralCircles_IsHitOnlyOnceAndTheTotalCountsItOnce()
        {
            Run r = new Run();
            // 보스 중심 원과 (-x,+z) 사분면의 첫 두 원이 모두 겹치는 점 (-8,+5)
            r.AddRel(10, -8f, 5f);
            r.Start();
            r.TickRange(0.0, 2.0, 0.5);

            BossCircleBurstLayout layout = r.Listener.StartedLayouts[0];
            Assert.IsTrue(layout.Contains(0, Rel(-8f, 5f)));
            int containing = 0;
            for (int i = 0; i < CIRCLES; ++i)
            {
                containing += layout.Contains(i, Rel(-8f, 5f)) ? 1 : 0;
            }

            Assert.GreaterOrEqual(containing, 3, "시험 지점은 세 원 이상에 든다");
            Assert.AreEqual(1, r.Hits(10), "교차 원에서도 피해 요청은 한 번");
            Assert.AreEqual(1, r.Listener.Strikes[0].HitCount);
            AssertVector(layout.GetCenter(0), r.Resolver.Requests[0].SourcePosition, "처음 속한 원은 보스 중심 원");
        }

        [Test]
        public void Intersection_TangentPointOfTwoQuadrantCircles_IsHitOnce()
        {
            Run r = new Run();
            // (+x,+z) 사분면의 (5,5)와 (11,5)가 맞닿는 점. 경계는 안이라 두 원에 모두 든다
            r.AddRel(10, 8f, 5f);
            r.Start();
            r.TickRange(0.0, 2.0, 0.5);

            BossCircleBurstLayout layout = r.Listener.StartedLayouts[0];
            Assert.IsTrue(layout.Contains(1, Rel(8f, 5f)));
            Assert.IsTrue(layout.Contains(2, Rel(8f, 5f)));
            Assert.IsFalse(layout.Contains(0, Rel(8f, 5f)));
            Assert.AreEqual(1, r.Hits(10));
            AssertVector(layout.GetCenter(1), r.Resolver.Requests[0].SourcePosition, "번호가 낮은 원이 먼저");
        }

        [Test]
        public void Intersection_ManyTargetsOverlapping_EachGetsOneRequestWithOneActionId()
        {
            Run r = new Run();
            r.AddRel(10, -8f, 5f);
            r.AddRel(11, 8f, 5f);
            r.AddRel(12, -5f, 5f);
            r.AddRel(13, -5f, -5f);
            r.Start();
            r.TickRange(0.0, 2.0, 0.5);

            Assert.AreEqual(4, r.Resolver.Requests.Count);
            for (int id = 10; id <= 13; ++id)
            {
                Assert.AreEqual(1, r.Hits(id), "대상 " + id);
            }

            int action = r.Resolver.Requests[0].ActionInstanceId;
            for (int i = 1; i < r.Resolver.Requests.Count; ++i)
            {
                Assert.AreEqual(action, r.Resolver.Requests[i].ActionInstanceId, "한 폭발은 한 동작이다");
            }

            Assert.AreEqual(4, r.Listener.Strikes[0].HitCount);
        }

        [Test]
        public void Intersection_LaterTicksNeverHitTheSameTargetAgain()
        {
            Run r = new Run();
            r.AddRel(10, -8f, 5f);
            r.Start();
            r.TickRange(0.0, 8.0, 0.05);

            Assert.AreEqual(1, r.Hits(10), "이 시전에서 대상당 최대 1회");
            Assert.AreEqual(1, r.Listener.Strikes.Count);
        }

        /****************************************
        *      Guard, Counter, Invulnerable
        ****************************************/

        [Test]
        public void Guarded_BlockedValidHitCountsAsOneHit_AndIsNotRepeatedByOverlap()
        {
            Run r = new Run();
            r.AddRel(10, -8f, 5f); // 세 원에 겹친다
            r.AddRel(11, 8f, 5f);  // 두 원에 겹친다
            r.Resolver.Outcomes[10] = EDamageOutcome.Guarded;
            r.Resolver.Outcomes[11] = EDamageOutcome.Applied;
            r.Start();
            r.TickRange(0.0, 2.0, 0.5);

            Assert.AreEqual(1, r.Hits(10), "방어로 막혀도 요청은 한 번이고 다른 원이 다시 때리지 않는다");
            Assert.AreEqual(1, r.Hits(11));
            Assert.AreEqual(2, r.Listener.Strikes[0].HitCount, "막힌 유효 피격도 한 번으로 센다");
        }

        [Test]
        public void Ignored_InvulnerableOrAlreadyHitRequestsAreNotCounted_ButStillTriedOnlyOnce()
        {
            Run r = new Run();
            r.AddRel(10, -8f, 5f);
            r.AddRel(11, 5f, -5f);
            r.AddRel(12, -5f, -5f);
            r.Resolver.Outcomes[10] = EDamageOutcome.Invulnerable;
            r.Resolver.Outcomes[11] = EDamageOutcome.DuplicateHit;
            r.Resolver.Outcomes[12] = EDamageOutcome.Guarded;
            r.Start();
            r.TickRange(0.0, 2.0, 0.5);

            Assert.AreEqual(1, r.Hits(10), "무적 대상도 교차한 다른 원이 다시 시도하지 않는다");
            Assert.AreEqual(1, r.Hits(11));
            Assert.AreEqual(1, r.Listener.Strikes[0].HitCount, "피해가 들어가지 않은 요청은 세지 않는다");
        }

        /****************************************
        *      Large Tick Jumps
        ****************************************/

        [Test]
        public void BigTick_PastTheBoundary_StrikesOnceAndSeparatesScheduledFromProcessed()
        {
            Run r = new Run();
            r.AddRel(10, -8f, 5f);
            r.AddRel(11, 5f, -5f);
            r.Start();
            r.TickAt(0.5);
            r.TickAt(9.0);

            Assert.AreEqual(1, r.Listener.Strikes.Count, "큰 틱 점프에서도 폭발은 한 번");
            Assert.AreEqual(STRIKE_TIME, r.Listener.Strikes[0].Scheduled, TIME_TOL, "예정 시각은 t=2.0 그대로");
            Assert.AreEqual(9.0, r.Listener.Strikes[0].Processed, TIME_TOL, "실제 처리 시각은 틱 시각");
            Assert.AreEqual(1, r.Hits(10));
            Assert.AreEqual(1, r.Hits(11));
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State, "점프한 틱 안에서 예고, 실행, 회복을 모두 지나 끝난다");
            Assert.IsTrue(r.Pattern.IsFinished);
            Assert.IsTrue(r.Pattern.IsStruck);
            Assert.AreEqual(9.0, r.Pattern.ProcessedTime, TIME_TOL);

            r.TickRange(9.0, 12.0, 0.5);
            Assert.AreEqual(2, r.Resolver.Requests.Count, "끝난 뒤에는 더 나가지 않는다");
            Assert.AreEqual(1, r.Listener.Strikes.Count);
        }

        [Test]
        public void BigTick_StraightFromTheStartToTheNextSelection_StrikesWithoutBeingSkipped()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();
            r.TickAt(0.0);
            r.TickAt(NEXT_SELECTION);

            Assert.AreEqual(1, r.Hits(10), "t=3.0까지 한 번에 건너뛰어도 t=2.0 폭발이 누락되지 않는다");
            Assert.AreEqual(STRIKE_TIME, r.Listener.Strikes[0].Scheduled, TIME_TOL);
            Assert.AreEqual(NEXT_SELECTION, r.Listener.Strikes[0].Processed, TIME_TOL);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [Test]
        public void BigTick_InsideTheRecovery_StrikesOnceAndKeepsRecovering()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();
            r.TickAt(1.0);
            r.TickAt(2.4);

            Assert.AreEqual(1, r.Hits(10));
            Assert.AreEqual(2.4, r.Listener.Strikes[0].Processed, TIME_TOL);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);

            r.TickAt(2.9);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State, "회복은 t=3.0까지");
            r.TickAt(3.0);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.AreEqual(1, r.Hits(10));
        }

        [Test]
        public void BigTick_UsesTheTargetPositionAtProcessingTime()
        {
            Run r = new Run();
            FakeCombatTarget runner = r.AddRel(10, 5f, 5f);
            FakeCombatTarget arriver = r.AddRel(11, 0f, 0f);
            r.Start();
            r.TickAt(1.0);

            // 정각 처리 전에 위치가 바뀌면 처리 시점의 현재 위치로 본다. 과거 위치를 복원하지 않는다
            runner.Position  = ArenaCenter;
            arriver.Position = Rel(-5f, -5f);
            r.TickAt(6.0);

            Assert.AreEqual(0, r.Hits(10), "예고 때 있던 곳을 떠났다");
            Assert.AreEqual(1, r.Hits(11), "처리 시점에 원 안에 있다");
        }

        [Test]
        public void SteadyTicks_NeverDuplicateTheStrike()
        {
            Run r = new Run();
            r.AddRel(10, -8f, 5f);
            r.AddRel(11, 8f, 5f);
            r.Start();
            r.TickRange(0.0, 5.0, 0.05);

            Assert.AreEqual(1, r.Hits(10));
            Assert.AreEqual(1, r.Hits(11));
            Assert.AreEqual(1, r.Listener.Strikes.Count);
            Assert.AreEqual(0, r.Listener.Cancels.Count);
        }

        [Test]
        public void EnteringExecuteWithoutATick_StillStrikesOnceAtTheBoundary()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();

            r.Pattern.EnterState(EBossAttackState.Execute, 2.0);
            r.Pattern.Tick(EBossAttackState.Execute, 2.0);
            r.Pattern.Tick(EBossAttackState.Execute, 2.5);

            Assert.AreEqual(1, r.Hits(10));
            Assert.AreEqual(1, r.Listener.Strikes.Count);
            Assert.AreEqual(STRIKE_TIME, r.Listener.Strikes[0].Processed, TIME_TOL);
        }

        [Test]
        public void EarlyExecuteNotice_BeforeTheStrikeTime_DoesNotStrike()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();

            r.Pattern.EnterState(EBossAttackState.Execute, 1.0);

            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.IsFalse(r.Pattern.IsStruck);
        }

        /****************************************
        *      Fixed At Cast Start
        ****************************************/

        [Test]
        public void Fixed_TheBossCircleStaysWhereTheBossStoodWhenTheCastStarted()
        {
            Run r = new Run();
            Vector3 oldSpot = BossPosition + new Vector3(-BOSS_RADIUS + 0.5f, 0f, 0f); // 옛 보스 위치 근처, 다른 원 밖
            Vector3 newSpot = Rel(15f, 0f);                                            // 보스가 옮겨 간 곳, 13개 원 어디에도 들지 않는 지점
            r.Add(10, oldSpot);
            r.Add(11, newSpot);
            r.Start();

            r.TickAt(1.0);
            r.Self.Position = newSpot;
            r.TickAt(2.0);

            Assert.AreEqual(1, r.Hits(10), "처음 타격에는 시전 당시 고정한 중심을 쓴다");
            Assert.AreEqual(0, r.Hits(11), "예고하지 않은 곳이 맞지 않는다");
            AssertVector(BossPosition, r.Pattern.Layout.BossCenter, "layout");
            AssertVector(BossPosition, r.Listener.StartedLayouts[0].BossCenter, "알림");
        }

        [Test]
        public void Fixed_TheQuadrantCirclesStayWhereTheArenaWasWhenTheCastStarted()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();

            r.TickAt(1.0);
            r.Arena.Set(ArenaCenter + new Vector3(30f, 0f, 0f), ARENA_RADIUS);
            r.TickAt(2.0);

            Assert.AreEqual(1, r.Hits(10), "전장 입력이 나중에 바뀌어도 시작 때 고정한 원을 쓴다");
            AssertVector(ArenaCenter, r.Pattern.Layout.ArenaCenter, "layout");
        }

        /****************************************
        *      Start Rejection
        ****************************************/

        [Test]
        public void StartRejected_WhenTheArenaIsMissing_NothingIsAnnouncedAndNothingHits()
        {
            Run r = new Run();
            r.Arena.Set(ArenaCenter, 0f);
            r.AddRel(10, 5f, 5f);
            r.Start();

            Assert.IsTrue(r.Pattern.IsStartRejected);
            Assert.AreEqual(0, r.Listener.StartedCasts.Count);

            // 두뇌는 거절된 시전을 이유 없이 바로 취소한다
            r.Fsm.Cancel(EBossInterruptReason.None);
            r.TickRange(0.0, 5.0, 0.1);

            Assert.AreEqual(0, r.Resolver.Requests.Count, "보이지 않는 피해는 나가지 않는다");
            Assert.AreEqual(0, r.Listener.Strikes.Count);
            Assert.AreEqual(0, r.Listener.Cancels.Count, "시작하지 못한 시전은 취소도 알리지 않는다");
        }

        [Test]
        public void StartRejected_WhenTheArenaSourceCannotAnswer()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);
            RecordingListener listener = new RecordingListener();
            BossBurstDirectPattern direct = new BossBurstDirectPattern(p, new MissingArena(), listener);

            Assert.IsTrue(direct.Pattern.IsStartRejected);
            Assert.AreEqual(0, listener.StartedCasts.Count);
        }

        // 팩토리를 거치지 않고 정의와 전장 입력만 바꿔 패턴을 시작한다
        private sealed class BossBurstDirectPattern
        {
            public BossCircleBurstPattern Pattern;

            public BossBurstDirectPattern(PatternDefinition definition, IBossArenaSource arena, IBossCircleBurstListener listener)
            {
                ScriptedResolver resolver = new ScriptedResolver();
                BossDamageGate gate = new BossDamageGate(resolver);
                gate.Open();
                FakeCombatTarget self = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossPosition };
                Pattern = new BossCircleBurstPattern(HREF, arena, listener);
                BossPatternContext context = new BossPatternContext(definition, gate.IssueInstanceId(), CombatIds.NONE, self,
                    new BurstQuery(), gate, new FakeBossRandom());
                Pattern.Begin(in context, 0.0);
            }
        }

        [Test]
        public void StartRejected_WhenTheDefinitionLacksBurstValues()
        {
            RecordingListener listener = new RecordingListener();
            BossBurstDirectPattern missing = new BossBurstDirectPattern(MakeVariant(spot: 0f), new BossFixedArena(ArenaCenter, ARENA_RADIUS), listener);
            BossBurstDirectPattern flat = new BossBurstDirectPattern(MakeVariant(far: NEAR), new BossFixedArena(ArenaCenter, ARENA_RADIUS), listener);

            Assert.IsTrue(missing.Pattern.IsStartRejected);
            Assert.IsTrue(flat.Pattern.IsStartRejected);
            Assert.AreEqual(0, listener.StartedCasts.Count);
        }

        [Test]
        public void Constructor_RequiresTheArenaAndTheListener()
        {
            Assert.Throws<System.ArgumentNullException>(() => new BossCircleBurstPattern(HREF, null, new RecordingListener()));
            Assert.Throws<System.ArgumentNullException>(() =>
                new BossCircleBurstPattern(HREF, new BossFixedArena(ArenaCenter, ARENA_RADIUS), null));
        }

        /****************************************
        *      Cancel And Cleanup
        ****************************************/

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.DirectControl)]
        public void Cancel_DuringTheTelegraph_ClearsTheTelegraphAndNeverStrikes(EBossInterruptReason reason)
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();
            r.TickRange(0.0, 1.5, 0.5);

            r.Fsm.Cancel(reason);

            Assert.AreEqual(1, r.Listener.Cancels.Count);
            Assert.AreEqual(reason, r.Listener.Cancels[0].Reason);
            Assert.IsFalse(r.Listener.Cancels[0].Struck, "폭발 전에 끊겼다");
            Assert.IsTrue(r.Pattern.IsCancelled);
            Assert.AreEqual(reason, r.Pattern.CancelReason);

            r.TickRange(1.5, 8.0, 0.1);
            r.Pattern.Tick(EBossAttackState.Telegraph, 2.0);
            r.Pattern.EnterState(EBossAttackState.Execute, 2.0);
            Assert.AreEqual(0, r.Resolver.Requests.Count, "예고가 끝나는 시각에도 피해가 나가지 않는다");
            Assert.AreEqual(0, r.Listener.Strikes.Count);
            Assert.AreEqual(1, r.Listener.Cancels.Count, "취소는 한 번만 알린다");
        }

        [Test]
        public void Cancel_JustBeforeTheStrikeTick_NeverStrikesEvenAtTwoSeconds()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();
            r.TickAt(1.95);

            r.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            r.TickAt(2.0);
            r.TickAt(3.0);

            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.Strikes.Count);
        }

        [Test]
        public void Cancel_AfterTheStrike_ReportsStruckAndSendsNothingMore()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();
            r.TickRange(0.0, 2.5, 0.25);
            Assert.AreEqual(1, r.Resolver.Requests.Count);

            r.Fsm.Cancel(EBossInterruptReason.CombatEnd);

            Assert.AreEqual(1, r.Listener.Cancels.Count);
            Assert.IsTrue(r.Listener.Cancels[0].Struck, "이미 폭발했다");
            r.TickRange(2.5, 6.0, 0.1);
            Assert.AreEqual(1, r.Resolver.Requests.Count);
            Assert.AreEqual(1, r.Listener.Strikes.Count);
        }

        [Test]
        public void ClosedGate_StopsDamageEvenIfThePatternIsNotCancelled()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();
            r.TickAt(1.0);

            r.Gate.CloseForGood();
            r.TickRange(1.0, 4.0, 0.1);

            Assert.AreEqual(0, r.Resolver.Requests.Count, "사망, 전투 종료로 게이트가 닫히면 resolver까지 가지 않는다");
            Assert.AreEqual(0, r.Pattern.HitCount);
            Assert.AreEqual(0, r.Listener.Cancels.Count);
        }

        [Test]
        public void Finish_AfterTheRecovery_LeavesNothingBehind()
        {
            Run r = new Run();
            r.AddRel(10, 5f, 5f);
            r.Start();
            r.TickRange(0.0, 3.0, 0.1);
            r.TickAt(NEXT_SELECTION);
            int requests = r.Resolver.Requests.Count;

            r.Pattern.Tick(EBossAttackState.Recover, 3.5);
            r.Pattern.Tick(EBossAttackState.Recover, 7.0);

            Assert.IsTrue(r.Pattern.IsFinished);
            Assert.AreEqual(requests, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.Cancels.Count);
        }

        /****************************************
        *      Easy P06 And Hard Phase One Regression
        ****************************************/

        [Test]
        public void EasyP06_StillBlastsOnlyTheBossCircleOnceAtItsOwnTime()
        {
            PatternDefinition easy = LoadDefinition(EASY_ID);
            FakeCombatTarget self = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossPosition };
            BurstQuery query = new BurstQuery();
            ScriptedResolver resolver = new ScriptedResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();
            query.Targets.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = BossPosition + new Vector3(3f, 0f, 0f) });
            query.Targets.Add(new FakeCombatTarget { CombatId = 11, Faction = ECombatFaction.Player, Position = Rel(NEAR, -NEAR) });

            IBossPattern pattern = new WolbaekPatternFactory(HREF).Create(easy);
            Assert.IsInstanceOf<BossCircleBlastPattern>(pattern);
            BossAttackFsm fsm = new BossAttackFsm();
            BossPatternContext context = new BossPatternContext(easy, gate.IssueInstanceId(), CombatIds.NONE, self, query, gate,
                new FakeBossRandom());
            Assert.IsTrue(fsm.Start(easy, pattern, in context, 0.0));

            double boundary = (double)easy.TelegraphSeconds;
            fsm.Tick(boundary - 0.01);
            Assert.AreEqual(0, resolver.Requests.Count, "이지 예고 경계 직전에는 폭발이 없다");
            fsm.Tick(boundary);
            Assert.AreEqual(1, resolver.Requests.Count, "이지 예고 경계 정각에 보스 중심 원 하나");
            Assert.AreEqual(10, resolver.Requests[0].HitTargetId, "사분면 위치의 대상은 맞지 않는다");
            Assert.AreEqual(1, query.Calls.Count, "이지는 원 하나만 조회한다");

            for (double t = boundary; t <= 4.0; t += 0.1)
            {
                fsm.Tick(t);
            }

            Assert.AreEqual(1, resolver.Requests.Count);
            Assert.AreEqual(EBossAttackState.Ready, fsm.State);
        }

        [Test]
        public void HardPhaseOneP03_StillBlastsTheBossCircleOnceWithItsOwnRadiusAndTime()
        {
            PatternDefinition hard = LoadDefinition(WolbaekPatternFactory.HARD_P03_ID);
            FakeCombatTarget self = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossPosition };
            BurstQuery query = new BurstQuery();
            ScriptedResolver resolver = new ScriptedResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();
            query.Targets.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player,
                Position = BossPosition + new Vector3(hard.Shape.Range, 0f, 0f) });
            query.Targets.Add(new FakeCombatTarget { CombatId = 11, Faction = ECombatFaction.Player,
                Position = BossPosition + new Vector3(hard.Shape.Range + 0.5f, 0f, 0f) });

            BossAttackFsm fsm = new BossAttackFsm();
            IBossPattern pattern = new WolbaekPatternFactory(HREF, arena: new BossFixedArena(ArenaCenter, ARENA_RADIUS),
                burstListener: new RecordingListener()).Create(hard);
            Assert.IsInstanceOf<BossCircleBlastPattern>(pattern, "동시 폭발 입력을 연결해도 1페이즈 하드 P03은 그대로다");
            BossPatternContext context = new BossPatternContext(hard, gate.IssueInstanceId(), CombatIds.NONE, self, query, gate,
                new FakeBossRandom());
            Assert.IsTrue(fsm.Start(hard, pattern, in context, 0.0));

            fsm.Tick((double)hard.TelegraphSeconds - 0.01);
            Assert.AreEqual(0, resolver.Requests.Count);
            fsm.Tick((double)hard.TelegraphSeconds);
            Assert.AreEqual(1, resolver.Requests.Count);
            Assert.AreEqual(10, resolver.Requests[0].HitTargetId, "반경 경계는 안, 0.5m 밖은 밖");
        }

        /****************************************
        *      Brain: Hard Catalog Selection
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = ArenaCenter };
            public BurstQuery         Query    = new BurstQuery();
            public ScriptedResolver   Damage   = new ScriptedResolver();
            public FakeBossVitals     Vitals   = new FakeBossVitals();
            public FakeBossRandom     Random   = new FakeBossRandom();
            public RecordingListener  Listener = new RecordingListener();
            public BossFixedArena     Arena    = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossBrain          Brain;

            // withBurst가 거짓이면 동시 폭발 알림을 연결하지 않은 서버 구성을 흉내 낸다
            public BrainRig(bool withBurst = true)
            {
                Query.Targets.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player,
                    Position = ArenaCenter + new Vector3(0f, 0f, 3f) });

                WolbaekPatternFactory factory = withBurst
                    ? new WolbaekPatternFactory(HREF, arena: Arena, burstListener: Listener)
                    : new WolbaekPatternFactory(HREF, arena: Arena);
                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog(), EBossDifficulty.Hard),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = factory,
                    Effects  = new BossEffectComposite(),
                    Control  = new FakeControlSource(),
                    Random   = Random
                });
            }

            public void TransitionAndHandoff()
            {
                Assert.IsTrue(Brain.Begin(0.0));
                Vitals.HealthRate = 0.5f;
                Brain.Tick(0.25);
                Assert.IsTrue(Brain.CompleteHandoff());
                Random.Roll = 0.999f; // 가중치 누적에서 마지막 후보인 P06을 고른다
            }

            // 첫 시전이 시작된 틱의 서버 시각, 시작하지 않았으면 음수다
            public double RunUntilStarted(double from = 0.5, double to = 5.0)
            {
                for (double t = from; t < to; t += 0.05)
                {
                    Brain.Tick(t);
                    if (Brain.AttackState != EBossAttackState.Ready)
                    {
                        return t;
                    }
                }

                return -1.0;
            }
        }

        [Test]
        public void Brain_HardPhaseTwo_RunsP06ForThreeSecondsAndThenOpensTheNextSelection()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();

            double started = r.RunUntilStarted();
            Assert.GreaterOrEqual(started, 0.0);
            Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId);

            for (double t = started + 0.05; t < (started + 2.95); t += 0.05)
            {
                r.Brain.Tick(t);
                Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId, "t=" + t);
            }

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, r.Listener.StartedLayouts.Count);
            Assert.AreEqual(1, r.Listener.Strikes.Count, "t=2.0에 한 번 폭발했다");
            Assert.AreEqual(started + 2.0, r.Listener.Strikes[0].Scheduled, TIME_TOL);
            Assert.AreEqual(1, r.Damage.Requests.Count);
            Assert.AreEqual(0, r.Listener.Cancels.Count);

            r.Brain.Tick(started + NEXT_SELECTION + 0.001);
            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState, "후딜 1초 뒤 t=3.0에 시전이 끝난다");
            r.Brain.Tick(started + NEXT_SELECTION + 0.05);
            Assert.AreNotEqual(EBossAttackState.Ready, r.Brain.AttackState, "다음 선택이 바로 열린다");
        }

        [Test]
        public void Brain_WithoutTheBurstListener_NeverSelectsP06_AndNoInvisibleDamageIsSent()
        {
            float[] rolls = { 0f, 0.3f, 0.6f, 0.999f };
            for (int k = 0; k < rolls.Length; ++k)
            {
                BrainRig r = new BrainRig(withBurst: false);
                r.TransitionAndHandoff();
                r.Random.Roll = rolls[k];

                for (double t = 0.5; t < 25.0; t += 0.05)
                {
                    r.Brain.Tick(t);
                    Assert.AreNotEqual(HARD_ID, r.Brain.ActivePatternId, "roll=" + rolls[k] + " t=" + t);
                }

                for (int i = 0; i < r.Damage.Requests.Count; ++i)
                {
                    Assert.AreNotEqual(HARD_ID, r.Damage.Requests[i].SourceDefinitionId, "roll=" + rolls[k]);
                }
            }
        }

        [Test]
        public void Brain_CombatEndDuringTheTelegraph_CancelsAndNothingStrikes()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            double started = r.RunUntilStarted();
            Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId);

            r.Brain.Tick(started + 1.0);
            r.Brain.RequestCombatEnd();
            for (double t = started + 1.05; t < (started + 6.0); t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(0, r.Damage.Requests.Count, "전투 종료 뒤 t=2.0 폭발이 나가지 않는다");
            Assert.AreEqual(0, r.Listener.Strikes.Count);
            Assert.AreEqual(1, r.Listener.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, r.Listener.Cancels[0].Reason);
            Assert.IsFalse(r.Listener.Cancels[0].Struck);
        }

        [Test]
        public void Brain_DeathDuringTheTelegraph_CancelsAndNothingStrikes()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            double started = r.RunUntilStarted();
            Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId);

            r.Brain.Tick(started + 1.5);
            r.Vitals.IsHealthZero = true;
            for (double t = started + 1.55; t < (started + 6.0); t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(0, r.Damage.Requests.Count);
            Assert.AreEqual(1, r.Listener.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.Death, r.Listener.Cancels[0].Reason);
            Assert.IsFalse(r.Listener.Cancels[0].Struck);
        }

        [Test]
        public void Brain_CombatEndAfterTheStrike_StopsFurtherDamageAndReportsStruck()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            double started = r.RunUntilStarted();

            for (double t = started + 0.05; t <= (started + 2.3); t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(1, r.Listener.Strikes.Count);
            int requests = r.Damage.Requests.Count;
            r.Brain.RequestCombatEnd();
            for (double t = started + 2.35; t < (started + 6.0); t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(requests, r.Damage.Requests.Count);
            Assert.AreEqual(1, r.Listener.Cancels.Count);
            Assert.IsTrue(r.Listener.Cancels[0].Struck);
        }

        [Test]
        public void Brain_ABigTickAcrossTheBoundary_StillStrikesOnce()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            double started = r.RunUntilStarted();
            Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId);

            r.Brain.Tick(started + 1.0);
            r.Brain.Tick(started + 2.8);

            Assert.AreEqual(1, r.Listener.Strikes.Count);
            Assert.AreEqual(started + 2.0, r.Listener.Strikes[0].Scheduled, TIME_TOL);
            Assert.AreEqual(started + 2.8, r.Listener.Strikes[0].Processed, TIME_TOL);
            Assert.AreEqual(1, r.Damage.Requests.Count);
        }
    }
}
