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
    // 월백 하드 2페이즈 P01 단계형 별 모양 빔을 에셋의 실제 정의와 실제 팩토리, 실제 공격 FSM으로 확인한다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 전장 중심과 반지름은 고정 전장 입력으로 넣는다
    // 피해, 시간, 형상 수치는 Play Mode와 다인 접속 검증 전의 초기값이라 값 자체의 정답을 보장하지 않는다
    public class BossWolbaekHardPhaseTwoStarTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH  = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID    = "boss_wolbaek";
        private const string HARD_ID       = BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID;
        private const string EASY_ID       = BossPatternCatalogSO.WOLBAEK_P2_P01_ID;
        private const float  HREF          = 1000f;
        private const float  RAW_DAMAGE    = 0.14f * HREF;
        private const float  TOLERANCE     = 0.001f;
        private const double TIME_TOL      = 0.0001;
        private const double STAGE_0       = 4.0;
        private const double STAGE_1       = 4.8;

        // FSM 경계는 float 정의 값을 double로 더해 얻으므로 리터럴 3.6, 4.6과 오차가 있다. 경계 틱 테스트는 FSM과 같은 식을 쓴다
        private const double STAGE_2       = 4.0 + 1.6f;
        private const double RECOVERY_END  = STAGE_2 + 2.0f;
        private const float  ARENA_RADIUS  = 18f;
        private const float  STEP_DEGREES  = 30f;
        private const int    STAGE_COUNT   = 3;
        private const float  MIN_RADIUS    = 6f;
        private const float  MAX_RADIUS    = 13.5f;
        private const float  RADIUS_STEP   = 0.25f;

        private static readonly Vector3 ArenaCenter = new Vector3(50f, 0f, -30f);
        private static readonly Vector3 BossStart   = new Vector3(100f, 0f, 100f);

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class StageQuery : ICombatTargetQuery
        {
            public List<ICombatTarget> Targets { get; } = new List<ICombatTarget>();
            public bool FailTryGet;

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
                target = null;
                if (FailTryGet)
                {
                    return false;
                }

                for (int i = 0; i < Targets.Count; ++i)
                {
                    if (Targets[i].CombatId == combatId)
                    {
                        target = Targets[i];
                        return true;
                    }
                }

                return false;
            }
        }

        private struct Strike
        {
            public int    StageIndex;
            public float  Rotation;
            public double Scheduled;
            public double Processed;
            public int    HitCount;
        }

        private sealed class RecordingListener : IBossStarStageListener
        {
            public List<int>           StartedCasts   = new List<int>();
            public List<int>           StartedStars   = new List<int>();
            public List<BossStarShape> StartedShapes  = new List<BossStarShape>();
            public List<float>         StartedSteps   = new List<float>();
            public List<int>           StartedStages  = new List<int>();
            public List<double>        StartedAt      = new List<double>();
            public List<float>         FirstStrikes   = new List<float>();
            public List<float>         Intervals      = new List<float>();
            public List<Strike>        Strikes        = new List<Strike>();
            public List<int>           CancelledNext  = new List<int>();
            public List<EBossInterruptReason> CancelReasons = new List<EBossInterruptReason>();

            public void OnStageSequenceStarted(int castInstanceId, int starId, in BossStarShape shape, float stepDegrees,
                int stageCount, double castStartServerTime, float firstStrikeSeconds, float strikeIntervalSeconds)
            {
                StartedCasts.Add(castInstanceId);
                StartedStars.Add(starId);
                StartedShapes.Add(shape);
                StartedSteps.Add(stepDegrees);
                StartedStages.Add(stageCount);
                StartedAt.Add(castStartServerTime);
                FirstStrikes.Add(firstStrikeSeconds);
                Intervals.Add(strikeIntervalSeconds);
            }

            public void OnStageStruck(int castInstanceId, int starId, int stageIndex, float rotationDegrees, double scheduledServerTime,
                double processedServerTime, int hitCount)
            {
                Strikes.Add(new Strike { StageIndex = stageIndex, Rotation = rotationDegrees, Scheduled = scheduledServerTime,
                    Processed = processedServerTime, HitCount = hitCount });
            }

            public void OnStageSequenceCancelled(int castInstanceId, int starId, int nextStageIndex, EBossInterruptReason reason)
            {
                CancelledNext.Add(nextStageIndex);
                CancelReasons.Add(reason);
            }
        }

        // 공격 FSM에 실제 팩토리가 만든 패턴을 올린다. 별형 관리자는 쓰지 않는다
        private sealed class Run
        {
            public FakeCombatTarget    Self      = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossStart };
            public StageQuery          Query     = new StageQuery();
            public FakeDamageResolver  Resolver  = new FakeDamageResolver();
            public RecordingListener   Listener  = new RecordingListener();
            public BossFixedArena      Arena     = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossDamageGate      Gate;
            public BossAttackFsm       Fsm       = new BossAttackFsm();
            public PatternDefinition   Definition;
            public BossStarStagePattern Pattern;
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

            // 보스에서 +z 방향으로 10m 떨어진 곳에 선택 대상을 둔다
            public FakeCombatTarget AddAimer(int id)
            {
                return Add(id, Self.Position + (Vector3.forward * 10f));
            }

            public void Start(int selectedTargetId, double time = 0.0)
            {
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, arena: Arena, starStageListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                Pattern = (BossStarStagePattern)pattern;
                BossPatternContext context = new BossPatternContext(Definition, CastId, selectedTargetId, Self, Query, Gate, null);
                Assert.IsTrue(Fsm.Start(Definition, pattern, in context, time));
            }

            public void TickAt(double time)
            {
                Fsm.Tick(time);
            }

            public void TickRange(double from, double to, double step = 0.25)
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

            // 고정한 별 위의 한 점, 기준 방향에서 오른쪽으로 degrees도 반지름 radius
            public Vector3 Polar(float degrees, float radius)
            {
                BossStarShape shape = Pattern.Shape;
                return shape.ArenaCenter + ((Quaternion.AngleAxis(degrees, Vector3.up) * shape.BaseDirection) * radius);
            }

            public bool InStage(Vector3 point, int stageIndex)
            {
                BossStarShape shape = Pattern.Shape;
                return BossStarGeometry.Contains(in shape, point, Pattern.StepDegrees * stageIndex);
            }

            // 단계별로 맞는지 여부가 mask(비트 k = 단계 k)와 같은 점을 찾는다
            public bool TryFindPoint(int mask, out Vector3 point)
            {
                for (int degrees = 0; degrees < 360; ++degrees)
                {
                    for (float radius = MIN_RADIUS; radius <= MAX_RADIUS; radius += RADIUS_STEP)
                    {
                        Vector3 candidate = Polar(degrees, radius);
                        int bits = 0;
                        for (int k = 0; k < STAGE_COUNT; ++k)
                        {
                            bits |= InStage(candidate, k) ? (1 << k) : 0;
                        }

                        if (bits == mask)
                        {
                            point = candidate;
                            return true;
                        }
                    }
                }

                point = Vector3.zero;
                return false;
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

        // 난수를 주지 않는다. 하드 P01은 좌우 추첨을 하지 않으므로 난수 없이 시작해야 한다
        private static Run StandardRun()
        {
            Run r = new Run();
            r.AddAimer(10);
            return r;
        }

        // 단계 0 직전 한 점도 맞지 않는 곳에서, 지정한 단계들에서만 맞는 점에 대상을 둔다
        private static FakeCombatTarget AddAt(Run r, int id, int mask)
        {
            Assert.IsTrue(r.TryFindPoint(mask, out Vector3 point), "mask=" + mask);
            return r.Add(id, point);
        }

        /****************************************
        *      Asset, Factory, Readiness Agree
        ****************************************/

        [Test]
        public void Asset_HardP01_HasStartingValuesAndAHardOnlyId()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);

            Assert.AreNotEqual(EASY_ID, p.DefinitionId, "이지 P01 ID를 재사용하지 않는다");
            Assert.AreEqual(EBossDifficulty.Hard, p.Difficulty);
            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(WOLBAEK_ID, p.BossId);
            Assert.AreEqual(HARD_ID + ".name", p.DisplayKey);
            Assert.AreEqual(EPatternGroup.Large, p.Group);
            Assert.AreEqual(4f, p.TelegraphSeconds, TOLERANCE, "첫 예고와 첫 타격 t=4.0");
            Assert.AreEqual(1.6f, p.ActiveSeconds, TOLERANCE, "첫 타격부터 마지막 타격 t=5.6");
            Assert.AreEqual(2f, p.RecoverySeconds, TOLERANCE, "다음 선택 t=7.6");
            Assert.AreEqual(5, p.StarVertexCount);
            Assert.AreEqual(14f, p.StarRingRadius, TOLERANCE);
            Assert.AreEqual(2, p.StarConnectStep);
            Assert.AreEqual(1.5f, p.Shape.Width, TOLERANCE);
            Assert.AreEqual(STAGE_COUNT, p.RawStarStageCount);
            Assert.AreEqual(STEP_DEGREES, p.StarStageDegrees, TOLERANCE);
            Assert.AreEqual(0.8f, p.StarStageIntervalSeconds, TOLERANCE);
            Assert.AreEqual(0f, p.StarRotationDegrees, "연속 회전을 쓰지 않는다");
            Assert.AreEqual(0f, p.BeamTickInterval, "지속 피해 간격을 쓰지 않는다");
            Assert.AreEqual(0.14f, p.DamageRef, TOLERANCE);
            Assert.IsFalse(p.Guardable, "방어 불가");
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(0f, p.TrackingSeconds);
            Assert.AreEqual(0.25f, p.RecentUseMultipliers[0], TOLERANCE, "하드 직전 배율");
            Assert.AreEqual(7.6f, p.TelegraphSeconds + p.ActiveSeconds + p.RecoverySeconds, TOLERANCE);
        }

        [Test]
        public void Asset_EasyP01_IsUnchanged_AndStillRotates()
        {
            PatternDefinition p = LoadDefinition(EASY_ID);

            Assert.AreEqual(EBossDifficulty.Easy, p.Difficulty);
            Assert.AreEqual(4f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(6f, p.ActiveSeconds, TOLERANCE);
            Assert.AreEqual(20f, p.StarRotationDegrees, TOLERANCE);
            Assert.AreEqual(0.25f, p.BeamTickInterval, TOLERANCE);
            Assert.AreEqual(0.03f, p.DamageRef, TOLERANCE);
            Assert.AreEqual(0, p.RawStarStageCount, "이지는 단계형이 아니다");

            BossStarManager manager = new BossStarManager(new StageQuery());
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, stars: manager, arena: arena, starStageListener: new RecordingListener());
            Assert.IsInstanceOf<BossStarPattern>(factory.Create(p), "이지 ID는 기존 회전형 실행에 연결된 채다");
            Assert.IsInstanceOf<BossStarStagePattern>(factory.Create(LoadDefinition(HARD_ID)), "하드 ID는 단계형 실행에 연결된다");
        }

        [Test]
        public void Asset_Catalog_SeparatesDifficultiesAndStaysValid()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            var easy = new List<PatternDefinition>();
            var hard = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Easy, easy);
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Hard, hard);

            Assert.AreEqual(7, easy.Count);
            Assert.AreEqual(7, hard.Count);
            Assert.AreEqual(HARD_ID, hard[0].DefinitionId);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_HARD_P2_P02_ID, hard[1].DefinitionId);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_HARD_P2_P03_ID, hard[2].DefinitionId);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_HARD_P2_P04_ID, hard[3].DefinitionId);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_HARD_P2_P05_ID, hard[4].DefinitionId);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_HARD_P2_P06_ID, hard[5].DefinitionId);
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
        }

        [Test]
        public void Asset_HardPhaseTwo_OpensOnlyWithAllSevenAndNoCountError()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            var errors = new List<string>();
            catalog.ValidateTuning(WOLBAEK_ID, 2, EBossDifficulty.Hard, errors);

            Assert.IsTrue(catalog.IsPhaseTuned(WOLBAEK_ID, 2, EBossDifficulty.Hard), "P01~P07 일곱 개가 갖춰져 하드 2페이즈가 열린다");
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, LoadDefinition(HARD_ID)), "P01 한 개의 데이터 자체는 준비됐다");
        }

        [Test]
        public void Factory_NeedsOnlyTheArena_AndReadinessAgrees()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);

            Assert.IsFalse(new WolbaekPatternFactory(HREF).CanCreate(p));
            Assert.IsNull(new WolbaekPatternFactory(HREF).Create(p));
            Assert.IsTrue(new WolbaekPatternFactory(HREF, arena: arena).CanCreate(p));
            Assert.IsInstanceOf<BossStarStagePattern>(new WolbaekPatternFactory(HREF, arena: arena).Create(p));
        }

        [Test]
        public void Validate_StageFields_RejectMixedAndIncompleteData()
        {
            PatternDefinition ok = LoadDefinition(HARD_ID);
            BossPatternCatalogSO catalog = LoadCatalog();

            PatternDefinition mixed = new PatternDefinition("x_mixed", WOLBAEK_ID, 2, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                2f, 0f, 0f, 1.6f, 1f, ok.Shape, 0.14f, false, group: EPatternGroup.Large, enhancedBaseWeight: 12f,
                beamTickInterval: 0.25f, starVertexCount: 5, starRingRadius: 14f, starConnectStep: 2, starRotationDegrees: 20f,
                difficulty: EBossDifficulty.Hard, starStageCount: 3, starStageDegrees: 30f, starStageIntervalSeconds: 0.8f);
            PatternDefinition tooShort = new PatternDefinition("x_short", WOLBAEK_ID, 2, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                2f, 0f, 0f, 1.0f, 1f, ok.Shape, 0.14f, false, group: EPatternGroup.Large, enhancedBaseWeight: 12f,
                starVertexCount: 5, starRingRadius: 14f, starConnectStep: 2,
                difficulty: EBossDifficulty.Hard, starStageCount: 3, starStageDegrees: 30f, starStageIntervalSeconds: 0.8f);
            PatternDefinition orphan = new PatternDefinition("x_orphan", WOLBAEK_ID, 2, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                2f, 0f, 0f, 1.6f, 1f, ok.Shape, 0.14f, false, group: EPatternGroup.Large, enhancedBaseWeight: 12f,
                starVertexCount: 5, starRingRadius: 14f, starConnectStep: 2, starRotationDegrees: 20f, beamTickInterval: 0.25f,
                starStageDegrees: 30f);

            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, mixed), "회전 속도와 단계가 섞이면 준비되지 않는다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, tooShort), "실행 시간이 마지막 단계를 덮지 못하면 준비되지 않는다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, orphan), "단계 수 없이 단계 각도만 있으면 준비되지 않는다");
        }

        /****************************************
        *      Placement At Cast Start
        ****************************************/

        [Test]
        public void Start_PlacesTheStarOnTheArenaCenterAndAnnouncesTheSchedule()
        {
            Run r = StandardRun();
            r.Start(10, 5.0);

            Assert.IsFalse(r.Pattern.IsStartRejected);
            Assert.AreEqual(1, r.Listener.StartedStars.Count);
            Assert.AreEqual(r.CastId, r.Listener.StartedCasts[0]);
            Assert.AreEqual(5.0, r.Listener.StartedAt[0], TIME_TOL, "시전 시작 서버 시각");
            Assert.AreEqual(4f, r.Listener.FirstStrikes[0], TOLERANCE);
            Assert.AreEqual(0.8f, r.Listener.Intervals[0], TOLERANCE);
            Assert.AreEqual(STAGE_COUNT, r.Listener.StartedStages[0]);
            Assert.AreEqual(STEP_DEGREES, r.Listener.StartedSteps[0], TOLERANCE, "고정된 양의 단계 각도");

            BossStarShape shape = r.Listener.StartedShapes[0];
            Assert.AreEqual(ArenaCenter, shape.ArenaCenter, "보스 위치가 아니라 전장 중심");
            Assert.AreEqual(0f, (Vector3.forward - shape.BaseDirection).magnitude, TOLERANCE, "꼭짓점 0이 대상 방향");
            Assert.AreEqual(14f, shape.RingRadius, TOLERANCE);
            Assert.AreEqual(5, shape.VertexCount);
            Assert.AreEqual(2, shape.ConnectStep);
            Assert.AreEqual(1.5f, shape.BeamWidth, TOLERANCE);
            Assert.AreEqual(0, r.Resolver.Requests.Count, "예고만으로는 피해가 없다");
        }

        [Test]
        public void Start_NeedsNoRandom_AndTheStepAngleIsAlwaysPositive()
        {
            for (int i = 0; i < 4; ++i)
            {
                Run r = StandardRun();
                r.Start(10);

                Assert.IsFalse(r.Pattern.IsStartRejected, "난수가 없어도 시작한다");
                Assert.AreEqual(STEP_DEGREES, r.Listener.StartedSteps[0], TOLERANCE);
                Assert.AreEqual(STEP_DEGREES, r.Pattern.StepDegrees, TOLERANCE);
                Assert.IsTrue(r.Pattern.Shape.RotatesRight);
            }
        }

        [Test]
        public void Start_RejectsWithNothingAnnounced_WhenTargetOrArenaIsMissing()
        {
            Run noTarget = StandardRun();
            noTarget.Query.FailTryGet = true;
            noTarget.Start(10);
            Assert.IsTrue(noTarget.Pattern.IsStartRejected);
            Assert.AreEqual(0, noTarget.Listener.StartedStars.Count);

            Run noArena = StandardRun();
            noArena.Arena.Set(ArenaCenter, 0f);
            noArena.Start(10);
            Assert.IsTrue(noArena.Pattern.IsStartRejected);

            Run smallArena = StandardRun();
            smallArena.Arena.Set(ArenaCenter, 10f);
            smallArena.Start(10);
            Assert.IsTrue(smallArena.Pattern.IsStartRejected, "배치 반경 14m가 전장 반지름보다 크면 거절");

            Run dead = new Run();
            dead.Add(10, Vector3.zero).LifeState = EParticipantLifeState.Dead;
            dead.Start(10);
            Assert.IsTrue(dead.Pattern.IsStartRejected);

            noTarget.TickRange(0.25, 6.0);
            Assert.AreEqual(0, noTarget.Resolver.Requests.Count);
            Assert.AreEqual(0, noTarget.Listener.Strikes.Count, "거절한 시전은 타격하지 않는다");
        }

        [Test]
        public void Placement_StaysFixed_WhenTargetMovesDiesOrBossMovesAndArenaChanges()
        {
            Run r = StandardRun();
            r.Start(10);
            BossStarShape first = r.Pattern.Shape;

            FakeCombatTarget aimer = (FakeCombatTarget)r.Query.Targets[0];
            aimer.Position = ArenaCenter;
            aimer.LifeState = EParticipantLifeState.Dead;
            r.Self.Position = ArenaCenter;
            r.Self.Forward = Vector3.back;
            r.Arena.Set(Vector3.zero, 5f);

            r.TickRange(0.25, 1.75);
            Assert.AreEqual(first.BaseDirection, r.Pattern.Shape.BaseDirection);
            Assert.AreEqual(first.ArenaCenter, r.Pattern.Shape.ArenaCenter);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        /****************************************
        *      Timing: 4.0 / 4.8 / 5.6 Boundaries
        ****************************************/

        [Test]
        public void Boundaries_StrikeExactlyAtTheScheduledTimes_NotBefore()
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b001);
            AddAt(r, 12, 0b010);
            AddAt(r, 13, 0b100);

            r.TickAt(0.0);
            r.TickAt(STAGE_0 - 0.01);
            Assert.AreEqual(0, r.Resolver.Requests.Count, "t=4.0 전에는 타격이 없다");

            r.TickAt(STAGE_0);
            Assert.AreEqual(1, r.Listener.Strikes.Count);
            Assert.AreEqual(1, r.Hits(11));
            Assert.AreEqual(0, r.Hits(12));
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);

            r.TickAt(STAGE_1 - 0.01);
            Assert.AreEqual(1, r.Listener.Strikes.Count, "t=4.8 전에는 둘째 타격이 없다");

            r.TickAt(STAGE_1);
            Assert.AreEqual(2, r.Listener.Strikes.Count);
            Assert.AreEqual(1, r.Hits(12));
            Assert.AreEqual(0, r.Hits(13));

            r.TickAt(STAGE_2 - 0.01);
            Assert.AreEqual(2, r.Listener.Strikes.Count, "t=5.6 전에는 셋째 타격이 없다");
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);

            r.TickAt(STAGE_2);
            Assert.AreEqual(3, r.Listener.Strikes.Count, "Execute에서 Recover로 넘어가는 틱에도 마지막 타격이 빠지지 않는다");
            Assert.AreEqual(1, r.Hits(13));
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);

            for (int i = 0; i < STAGE_COUNT; ++i)
            {
                Assert.AreEqual(i, r.Listener.Strikes[i].StageIndex);
                Assert.AreEqual(STEP_DEGREES * i, r.Listener.Strikes[i].Rotation, TOLERANCE);
            }

            Assert.AreEqual(STAGE_0, r.Listener.Strikes[0].Scheduled, TIME_TOL);
            Assert.AreEqual(STAGE_1, r.Listener.Strikes[1].Scheduled, TIME_TOL);
            Assert.AreEqual(STAGE_2, r.Listener.Strikes[2].Scheduled, TIME_TOL);
        }

        [Test]
        public void Boundaries_QuarterSecondTicks_ProcessEachStageOnceAtTheFirstTickAfterItsTime()
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b111);

            r.TickRange(0.0, 8.0);

            Assert.AreEqual(STAGE_COUNT, r.Listener.Strikes.Count);
            Assert.AreEqual(4.0, r.Listener.Strikes[0].Processed, TIME_TOL);
            Assert.AreEqual(5.0, r.Listener.Strikes[1].Processed, TIME_TOL, "예정 4.8 -> 처음 닿은 틱 5.0");
            Assert.AreEqual(5.75, r.Listener.Strikes[2].Processed, TIME_TOL, "예정 5.6 -> 처음 닿은 틱 5.75");
            Assert.AreEqual(STAGE_1, r.Listener.Strikes[1].Scheduled, TIME_TOL, "예정 시각은 틱에 끌려가지 않는다");
            Assert.AreEqual(STAGE_COUNT, r.Hits(11));
        }

        [Test]
        public void Fsm_NextSelectionOpensAt4Point6_NotBefore()
        {
            Run r = StandardRun();
            r.Start(10);

            r.TickAt(STAGE_2);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(RECOVERY_END - 0.01);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State, "후딜 1초 중에는 다음 패턴을 고르지 않는다");
            r.TickAt(RECOVERY_END + 0.001);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.IsFalse(r.Fsm.IsControlWindowOpen(4.0), "직접 제어 구간 없음");
        }

        /****************************************
        *      Big Tick Jumps
        ****************************************/

        [Test]
        public void BigTick_FromStartTo10Seconds_StrikesAllThreeStagesOnceEach()
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b111);

            r.TickAt(10.0);

            Assert.AreEqual(STAGE_COUNT, r.Listener.Strikes.Count, "예정 단계를 빠뜨리지 않는다");
            Assert.AreEqual(STAGE_COUNT, r.Hits(11));
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            for (int i = 0; i < STAGE_COUNT; ++i)
            {
                Assert.AreEqual(i, r.Listener.Strikes[i].StageIndex, "단계 순서대로");
                Assert.AreEqual(10.0, r.Listener.Strikes[i].Processed, TIME_TOL, "실제 처리 시각은 틱 시각으로 보고한다");
            }

            Assert.AreEqual(STAGE_0, r.Listener.Strikes[0].Scheduled, TIME_TOL);
            Assert.AreEqual(STAGE_2, r.Listener.Strikes[2].Scheduled, TIME_TOL);

            r.TickRange(10.25, 14.0);
            Assert.AreEqual(STAGE_COUNT, r.Listener.Strikes.Count, "두 번 실행하지 않는다");
            Assert.AreEqual(STAGE_COUNT, r.Hits(11));
        }

        [Test]
        public void BigTick_AcrossTwoStages_StrikesBothOnceAndDoesNotRepeatTheFirst()
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b111);

            r.TickAt(STAGE_0);
            Assert.AreEqual(1, r.Listener.Strikes.Count);
            r.TickAt(STAGE_2 + 0.1);

            Assert.AreEqual(STAGE_COUNT, r.Listener.Strikes.Count);
            Assert.AreEqual(STAGE_COUNT, r.Hits(11), "단계 0은 다시 치지 않는다");
            Assert.AreEqual(1, r.Listener.Strikes[1].StageIndex);
            Assert.AreEqual(2, r.Listener.Strikes[2].StageIndex);
        }

        [Test]
        public void BigTick_UsesCurrentTargetPositions_NotARestoredPastPosition()
        {
            Run r = StandardRun();
            r.Start(10);
            // 단계 0에서만 맞는 곳에 있다가 큰 틱 직전에 단계 1에서만 맞는 곳으로 옮긴다
            FakeCombatTarget mover = AddAt(r, 11, 0b001);
            Assert.IsTrue(r.TryFindPoint(0b010, out Vector3 laterSpot));
            mover.Position = laterSpot;

            r.TickAt(10.0);

            Assert.AreEqual(1, r.Hits(11), "현재 위치 기준으로 단계 1에서만 맞는다");
            Assert.AreEqual(0, r.Listener.Strikes[0].HitCount, "예정 시각의 과거 위치를 복원하지 않는다");
            Assert.AreEqual(1, r.Listener.Strikes[1].HitCount);
            Assert.AreEqual(0, r.Listener.Strikes[2].HitCount);
        }

        /****************************************
        *      One Hit Per Stage, No Residual
        ****************************************/

        [Test]
        public void Damage_IsUnblockableConfiguredAmountWithOneActionIdPerStage()
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b111);
            AddAt(r, 12, 0b111);
            r.TickRange(0.0, 7.0);

            Assert.AreEqual(STAGE_COUNT * 2, r.Resolver.Requests.Count);
            HashSet<int> actionIds = new HashSet<int>();
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                Assert.AreEqual(RAW_DAMAGE, request.RawDamage, TOLERANCE);
                Assert.IsTrue((request.Flags & EDamageFlags.Unblockable) != 0, "방어 불가");
                Assert.AreEqual(r.CastId, request.CastInstanceId, "시전 번호 공유");
                Assert.AreEqual(1, request.AttackerId);
                Assert.AreEqual(HARD_ID, request.SourceDefinitionId);
                actionIds.Add(request.ActionInstanceId);
            }

            Assert.AreEqual(STAGE_COUNT, actionIds.Count, "단계마다 동작 번호가 하나, 같은 단계의 두 대상은 같은 번호");
        }

        [Test]
        public void Damage_AtMostThreeTimesPerTarget_AndNothingLingersBetweenStages()
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b111);

            r.TickRange(0.0, 5.5, 0.05);
            Assert.AreEqual(2, r.Hits(11), "단계 0, 1만 지났다. 그 사이 틱마다 추가 피해가 없다");

            r.TickRange(5.55, 20.0, 0.05);
            Assert.AreEqual(3, r.Hits(11), "한 시전 대상당 최대 3회");
            Assert.AreEqual(3, r.Resolver.Requests.Count);
        }

        [Test]
        public void Damage_EachStageHitsOnlyTargetsInsideThatStageAngle()
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b001);
            AddAt(r, 12, 0b010);
            AddAt(r, 13, 0b100);
            AddAt(r, 14, 0b000);

            r.TickAt(10.0);

            Assert.AreEqual(1, r.Hits(11));
            Assert.AreEqual(1, r.Hits(12));
            Assert.AreEqual(1, r.Hits(13));
            Assert.AreEqual(0, r.Hits(14), "어느 단계 빔에도 닿지 않는 자리는 맞지 않는다");
        }

        [Test]
        public void Damage_BeamCrossingHitsOncePerStage()
        {
            Run r = StandardRun();
            r.Start(10);
            // 오각별 안쪽 오각형의 꼭짓점 근처 교차점, 빔 둘이 겹쳐도 참 거짓 하나다
            float apothem = 4.326f;
            Vector3 crossing = r.Polar(36f, apothem / Mathf.Cos(36f * Mathf.Deg2Rad));
            r.Add(11, crossing);

            r.TickAt(STAGE_0);
            Assert.AreEqual(1, r.Hits(11), "빔 둘이 겹쳐도 한 단계에 한 번");
        }

        [Test]
        public void Targets_CenterIsSafe_SummonsAreHitDeadAndEnemiesAreNot()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, ArenaCenter);
            Assert.IsTrue(r.TryFindPoint(0b111, out Vector3 spot));
            r.Add(12, spot, true);
            r.Add(13, spot).LifeState = EParticipantLifeState.Dead;
            r.Add(14, spot).Faction = ECombatFaction.Enemy;
            r.TickAt(10.0);

            Assert.AreEqual(0, r.Hits(11), "정중앙 안전 구역");
            Assert.AreEqual(STAGE_COUNT, r.Hits(12), "소환물도 피해를 받는다");
            Assert.AreEqual(0, r.Hits(13));
            Assert.AreEqual(0, r.Hits(14));
        }

        [Test]
        public void Angles_AreZeroThirtySixty_AndEveryStageUsesItsOwnAnglePosition()
        {
            Run r = StandardRun();
            r.Start(10);
            BossStarShape shape = r.Pattern.Shape;

            // 단계 k에서만 맞는 점은 기준 방향에서 오른쪽으로 30 x k 도 돌린 별의 빔 위에 있다
            for (int k = 0; k < STAGE_COUNT; ++k)
            {
                BossStarGeometry.GetBeam(in shape, 0, STEP_DEGREES * k, out Vector3 start, out Vector3 end);
                Vector3 middle = (start + end) * 0.5f;
                Assert.IsTrue(r.InStage(middle, k), "단계 " + k + "의 빔 중점은 단계 " + k + "에서 맞는다");
            }

            // 별은 서로 겹치므로 단계마다 그 단계에서만 맞는 점을 따로 찾아 단계별 위치가 실제로 다른지 본다
            Assert.IsTrue(r.TryFindPoint(0b001, out Vector3 onlyZero));
            Assert.IsTrue(r.TryFindPoint(0b010, out Vector3 onlyThirty));
            Assert.IsTrue(r.TryFindPoint(0b100, out Vector3 onlySixty));
            r.Add(11, onlyZero);
            r.Add(12, onlyThirty);
            r.Add(13, onlySixty);

            r.TickAt(STAGE_0);
            Assert.AreEqual(1, r.Hits(11));
            Assert.AreEqual(0, r.Hits(12));
            Assert.AreEqual(0, r.Hits(13));

            r.TickAt(STAGE_1);
            Assert.AreEqual(1, r.Hits(12));
            Assert.AreEqual(0, r.Hits(13));

            r.TickAt(STAGE_2);
            Assert.AreEqual(1, r.Hits(13));

            Assert.AreEqual(0f, r.Listener.Strikes[0].Rotation, TOLERANCE);
            Assert.AreEqual(30f, r.Listener.Strikes[1].Rotation, TOLERANCE);
            Assert.AreEqual(60f, r.Listener.Strikes[2].Rotation, TOLERANCE);
            for (int i = 0; i < r.Listener.Strikes.Count; ++i)
            {
                Assert.GreaterOrEqual(r.Listener.Strikes[i].Rotation, 0f, "음수 각도를 쓰지 않는다");
            }
        }

        /****************************************
        *      Cancel And Cleanup
        ****************************************/

        [Test]
        public void Cancel_DuringTheFirstTelegraph_AnnouncesOnceAndNeverStrikes()
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b001);
            r.TickAt(STAGE_0 / 2.0);

            r.Fsm.Cancel(EBossInterruptReason.Death);
            r.Fsm.Cancel(EBossInterruptReason.Death);
            r.TickRange(STAGE_0, 10.0);

            Assert.AreEqual(1, r.Listener.CancelledNext.Count);
            Assert.AreEqual(0, r.Listener.CancelledNext[0], "미실행 단계 0부터 정리");
            Assert.AreEqual(0, r.Listener.Strikes.Count);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        public void Cancel_AfterTheFirstStage_ClearsPendingStagesButKeepsAppliedDamage(EBossInterruptReason reason)
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b111);
            r.TickRange(0.0, STAGE_0 + 0.25);
            Assert.AreEqual(1, r.Hits(11));

            r.Fsm.Cancel(reason);
            r.TickRange(STAGE_0 + 0.5, 12.0);

            Assert.AreEqual(1, r.Hits(11), "이미 적용된 피해는 그대로, 남은 단계는 타격하지 않는다");
            Assert.AreEqual(1, r.Listener.Strikes.Count);
            Assert.AreEqual(1, r.Listener.CancelledNext.Count);
            Assert.AreEqual(1, r.Listener.CancelledNext[0], "단계 1부터 정리");
            Assert.AreEqual(reason, r.Listener.CancelReasons[0]);
        }

        [Test]
        public void Cancel_ClosedGateStopsDamageEvenIfTheFsmIsNotCancelled()
        {
            Run r = StandardRun();
            r.Start(10);
            AddAt(r, 11, 0b111);
            r.TickAt(STAGE_0);
            int before = r.Hits(11);

            r.Gate.CloseForGood();
            r.TickRange(STAGE_0 + 0.25, 8.0);

            Assert.AreEqual(before, r.Hits(11));
        }

        /****************************************
        *      Brain: Hard Catalog Selection
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = ArenaCenter };
            public StageQuery         Query    = new StageQuery();
            public FakeDamageResolver Damage   = new FakeDamageResolver();
            public FakeBossVitals     Vitals   = new FakeBossVitals();
            public FakeBossRandom     Random   = new FakeBossRandom();
            public RecordingListener  Listener = new RecordingListener();
            public BossFixedArena     Arena    = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossBrain          Brain;

            public BrainRig()
            {
                Query.Targets.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player,
                    Position = ArenaCenter + new Vector3(0f, 0f, 3f) });

                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, arena: Arena, starStageListener: Listener);
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
            }
        }

        [Test]
        public void Brain_HardPhaseTwo_RunsP01ForFourPointSixSecondsWithoutPickingAnother()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            string firstId = null;
            for (double t = 0.5; t < 12.0; t += 0.05)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                    firstId = r.Brain.ActivePatternId;
                }

                if ((started >= 0.0) && (t < (started + RECOVERY_END - 0.06)))
                {
                    Assert.AreEqual(firstId, r.Brain.ActivePatternId, "t=" + t);
                    Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId);
                }
            }

            LogAssert.NoUnexpectedReceived();
            Assert.GreaterOrEqual(started, 0.0);
            Assert.AreEqual(HARD_ID, firstId);
            Assert.GreaterOrEqual(r.Listener.StartedStars.Count, 1);
            Assert.GreaterOrEqual(r.Listener.Strikes.Count, STAGE_COUNT);
        }

        [Test]
        public void Brain_CombatEndAfterTheFirstStage_StopsTheRemainingStages()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            for (double t = 0.5; t < 20.0; t += 0.05)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                }

                if ((started >= 0.0) && (t >= (started + STAGE_0 + 0.1)))
                {
                    break;
                }
            }

            Assert.AreEqual(1, r.Listener.Strikes.Count);
            r.Brain.RequestCombatEnd();
            for (double t = started + STAGE_0 + 0.5; t < started + 12.0; t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(1, r.Listener.Strikes.Count, "전투 종료 뒤 남은 단계는 타격하지 않는다");
            Assert.AreEqual(1, r.Listener.CancelledNext.Count);
        }
    }
}
