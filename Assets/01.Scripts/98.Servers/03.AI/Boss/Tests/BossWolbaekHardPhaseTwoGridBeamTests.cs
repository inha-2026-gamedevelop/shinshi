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
    // 월백 하드 2페이즈 P02 격자 순차 빔을 에셋의 실제 정의와 실제 팩토리, 실제 공격 FSM으로 확인한다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 전장 중심과 반지름은 고정 전장 입력으로 넣는다
    // 피해, 시간, 형상 수치는 Play Mode와 다인 접속 검증 전의 초기값이라 값 자체의 정답을 보장하지 않는다
    // 줄 방향의 위아래, 좌우는 월드 축(가로줄은 월드 Z가 커지는 순서, 세로줄은 월드 X가 커지는 순서)까지만 확인하고 카메라 화면 방향은 확인하지 않는다
    public class BossWolbaekHardPhaseTwoGridBeamTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH  = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID    = "boss_wolbaek";
        private const string HARD_ID       = BossPatternCatalogSO.WOLBAEK_HARD_P2_P02_ID;
        private const string EASY_ID       = BossPatternCatalogSO.WOLBAEK_P2_P02_ID;
        private const string HARD_P01_ID   = BossPatternCatalogSO.WOLBAEK_HARD_P2_P01_ID;
        private const float  HREF          = 1000f;
        private const float  RAW_DAMAGE    = 0.14f * HREF;
        private const float  TOLERANCE     = 0.001f;
        private const double TIME_TOL      = 0.0001;
        private const float  ARENA_RADIUS  = 18f;
        private const int    LINE_COUNT    = 12;
        private const int    TOTAL_LINES   = 24;
        private const float  SPACING       = 3f;
        private const float  FIRST_OFFSET  = -16.5f; // 첫 줄 중심선, 전장 중심에서의 거리

        // 설계 시각(전부 정확한 이진 분수라 FSM 경계와 오차가 없다)
        private const double FIRST_STRIKE        = 1.0;
        private const double HORIZONTAL_LAST     = 6.5;
        private const double VERTICAL_FIRST      = 8.0;
        private const double VERTICAL_LAST       = 13.5;
        private const double NEXT_SELECTION      = 15.0;

        // 설계 시각 24개를 값 그대로 적은 표다. 공식으로 만들지 않으므로 실행 코드의 공식이 틀려도 같이 틀려서 통과하지 않는다
        // 가로 12줄은 1.0부터 0.5초 간격으로 6.5까지, 가로 마지막에서 1.5초 뒤 세로 8.0부터 0.5초 간격으로 13.5까지다
        private static readonly double[] DesignedStrikeTable =
        {
            1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0, 5.5, 6.0, 6.5,
            8.0, 8.5, 9.0, 9.5, 10.0, 10.5, 11.0, 11.5, 12.0, 12.5, 13.0, 13.5
        };

        private static readonly Vector3 ArenaCenter = new Vector3(50f, 0f, -30f);
        private static readonly Vector3 BossStart   = new Vector3(100f, 0f, 100f);

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class GridQuery : ICombatTargetQuery
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

        private struct Telegraph
        {
            public int               LineId;
            public EBossGridBeamAxis Axis;
            public int               Index;
            public double            TelegraphAt;
            public double            StrikeAt;
        }

        private struct Strike
        {
            public int               LineId;
            public EBossGridBeamAxis Axis;
            public int               Index;
            public double            Scheduled;
            public double            Processed;
            public int               HitCount;
        }

        // 예고와 타격이 들어온 순서를 예정 시각과 함께 남긴다
        private struct TimelineEntry
        {
            public bool   IsStrike;
            public int    LineId;
            public double Scheduled;
        }

        private sealed class RecordingListener : IBossGridBeamListener
        {
            public List<int>                   StartedCasts  = new List<int>();
            public List<BossGridBeamLayout>    StartedLayouts = new List<BossGridBeamLayout>();
            public List<double>                StartedAt     = new List<double>();
            public List<float>                 FirstStrikes  = new List<float>();
            public List<float>                 Intervals     = new List<float>();
            public List<float>                 AxisGaps      = new List<float>();
            public List<int>                   FirstLineIds  = new List<int>();
            public List<Telegraph>             Telegraphs    = new List<Telegraph>();
            public List<Strike>                Strikes       = new List<Strike>();
            public List<TimelineEntry>         Timeline      = new List<TimelineEntry>();
            public List<int>                   CancelledTelegraphed = new List<int>();
            public List<int>                   CancelledStruck      = new List<int>();
            public List<EBossInterruptReason>  CancelReasons = new List<EBossInterruptReason>();

            public void OnGridStarted(int castInstanceId, in BossGridBeamLayout layout, double castStartServerTime,
                float firstStrikeSeconds, float strikeIntervalSeconds, float axisGapSeconds, int firstLineId)
            {
                StartedCasts.Add(castInstanceId);
                StartedLayouts.Add(layout);
                StartedAt.Add(castStartServerTime);
                FirstStrikes.Add(firstStrikeSeconds);
                Intervals.Add(strikeIntervalSeconds);
                AxisGaps.Add(axisGapSeconds);
                FirstLineIds.Add(firstLineId);
            }

            public void OnLineTelegraphed(int castInstanceId, int lineId, EBossGridBeamAxis axis, int lineIndex,
                double telegraphServerTime, double strikeServerTime)
            {
                Telegraphs.Add(new Telegraph { LineId = lineId, Axis = axis, Index = lineIndex, TelegraphAt = telegraphServerTime,
                    StrikeAt = strikeServerTime });
                Timeline.Add(new TimelineEntry { IsStrike = false, LineId = lineId, Scheduled = telegraphServerTime });
            }

            public void OnLineStruck(int castInstanceId, int lineId, EBossGridBeamAxis axis, int lineIndex,
                double scheduledServerTime, double processedServerTime, int hitCount)
            {
                Strikes.Add(new Strike { LineId = lineId, Axis = axis, Index = lineIndex, Scheduled = scheduledServerTime,
                    Processed = processedServerTime, HitCount = hitCount });
                Timeline.Add(new TimelineEntry { IsStrike = true, LineId = lineId, Scheduled = scheduledServerTime });
            }

            public void OnGridCancelled(int castInstanceId, int telegraphedCount, int struckCount, EBossInterruptReason reason)
            {
                CancelledTelegraphed.Add(telegraphedCount);
                CancelledStruck.Add(struckCount);
                CancelReasons.Add(reason);
            }
        }

        // 공격 FSM에 실제 팩토리가 만든 패턴을 올린다. 빔 관리자와 별형 관리자는 쓰지 않는다
        private sealed class Run
        {
            public FakeCombatTarget    Self      = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossStart };
            public GridQuery           Query     = new GridQuery();
            public FakeDamageResolver  Resolver  = new FakeDamageResolver();
            public RecordingListener   Listener  = new RecordingListener();
            public BossFixedArena      Arena     = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossDamageGate      Gate;
            public BossAttackFsm       Fsm       = new BossAttackFsm();
            public PatternDefinition   Definition;
            public BossGridBeamPattern Pattern;
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

            public void Start(int selectedTargetId, double time = 0.0)
            {
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, arena: Arena, gridListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                Pattern = (BossGridBeamPattern)pattern;
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

        // 대상 한 명이 있으면 시작할 수 있다. 격자는 대상 위치와 무관하다
        private static Run StandardRun()
        {
            Run r = new Run();
            r.Add(10, ArenaCenter + new Vector3(0f, 0f, 3f));
            return r;
        }

        // 기본 대상(ID 10)이 없는 실행기. 기본 대상은 줄 경계(가로 6, 7번과 세로 5, 6번)에 서 있어 가로 한 번, 세로 한 번을 맞으므로
        // 요청 수와 맞는 순서를 세는 테스트는 자신이 놓은 대상만 있게 이 실행기로 시작한다. 격자는 대상이 필요 없어 시작은 막히지 않는다
        private static Run EmptyRun()
        {
            return new Run();
        }

        // 전장 중심에서 (x, z)만큼 떨어진 점
        private static Vector3 At(float x, float z)
        {
            return ArenaCenter + new Vector3(x, 0f, z);
        }

        // i번째 줄의 중심선 오프셋
        private static float LineOffset(int index)
        {
            return FIRST_OFFSET + (index * SPACING);
        }

        // 설계 시각 k번째 줄의 타격 시각, 공식이 아니라 표에서 읽는다
        private static double DesignedStrike(int k)
        {
            return DesignedStrikeTable[k];
        }

        // 에셋이 아닌 정의로 격자 패턴을 직접 올린다. 정의 ID와 상관없이 실행 클래스를 만들어 데이터가 달라도 공식이 맞는지 본다
        private static Run CustomRun(PatternDefinition definition, float arenaRadius, double time = 0.0)
        {
            Run r = new Run();
            r.Definition = definition;
            r.Arena.Set(ArenaCenter, arenaRadius);
            r.Add(10, ArenaCenter);
            r.Pattern = new BossGridBeamPattern(HREF, r.Arena, r.Listener);
            BossPatternContext context = new BossPatternContext(definition, r.CastId, 10, r.Self, r.Query, r.Gate, null);
            Assert.IsTrue(r.Fsm.Start(definition, r.Pattern, in context, time));
            Assert.IsFalse(r.Pattern.IsStartRejected);
            return r;
        }

        private static PatternDefinition MakeGrid(float width, int count, float spacing, float interval, float gap, float active,
            int beamCount = 0)
        {
            return new PatternDefinition("x_grid", WOLBAEK_ID, 2, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                1f, 0f, 0f, active, 1.5f, EnemyCatalogSO.AttackShapeSpec.Line(36f, width), 0.14f, false,
                group: EPatternGroup.Large, enhancedBaseWeight: 12f, beamCount: beamCount, beamLateralOffset: (beamCount > 0) ? 2f : 0f,
                beamTickInterval: (beamCount > 0) ? 0.5f : 0f, difficulty: EBossDifficulty.Hard,
                gridLineCount: count, gridLineSpacing: spacing, gridStrikeIntervalSeconds: interval, gridAxisGapSeconds: gap);
        }

        /****************************************
        *      Asset, Factory, Readiness Agree
        ****************************************/

        [Test]
        public void Asset_HardP02_HasStartingValuesAndAHardOnlyId()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);

            Assert.AreNotEqual(EASY_ID, p.DefinitionId, "이지 P02 ID를 재사용하지 않는다");
            Assert.AreEqual(EBossDifficulty.Hard, p.Difficulty);
            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(WOLBAEK_ID, p.BossId);
            Assert.AreEqual(HARD_ID + ".name", p.DisplayKey);
            Assert.AreEqual(EPatternGroup.Large, p.Group);
            Assert.AreEqual(1f, p.TelegraphSeconds, TOLERANCE, "첫 줄 예고 1초, 첫 타격 t=1.0");
            Assert.AreEqual(12.5f, p.ActiveSeconds, TOLERANCE, "첫 타격부터 마지막 타격 t=13.5");
            Assert.AreEqual(1.5f, p.RecoverySeconds, TOLERANCE, "다음 선택 t=15.0");
            Assert.AreEqual(15f, p.TelegraphSeconds + p.ActiveSeconds + p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Line, p.Shape.Shape);
            Assert.AreEqual(3f, p.Shape.Width, TOLERANCE);
            Assert.AreEqual(LINE_COUNT, p.RawGridLineCount);
            Assert.AreEqual(SPACING, p.GridLineSpacing, TOLERANCE);
            Assert.AreEqual(0.5f, p.GridStrikeIntervalSeconds, TOLERANCE);
            Assert.AreEqual(1.5f, p.GridAxisGapSeconds, TOLERANCE);
            Assert.AreEqual(0.14f, p.DamageRef, TOLERANCE);
            Assert.IsFalse(p.Guardable, "방어 불가");
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(0f, p.TrackingSeconds);
            Assert.AreEqual(0, p.RawBeamCount, "이지 P02의 평행 빔 값을 쓰지 않는다");
            Assert.AreEqual(0f, p.BeamTickInterval, "지속 피해 간격을 쓰지 않는다");
            Assert.AreEqual(0.25f, p.RecentUseMultipliers[0], TOLERANCE, "하드 직전 배율");
        }

        [Test]
        public void Asset_EasyP02_IsUnchanged_AndStillUsesTheReservedBeams()
        {
            PatternDefinition p = LoadDefinition(EASY_ID);

            Assert.AreEqual(EBossDifficulty.Easy, p.Difficulty);
            Assert.AreEqual(1.5f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(4f, p.ActiveSeconds, TOLERANCE);
            Assert.AreEqual(0.8f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(2f, p.Shape.Width, TOLERANCE);
            Assert.AreEqual(2, p.RawBeamCount);
            Assert.AreEqual(5f, p.BeamLateralOffset, TOLERANCE);
            Assert.AreEqual(0.5f, p.BeamTickInterval, TOLERANCE);
            Assert.AreEqual(0.08f, p.DamageRef, TOLERANCE);
            Assert.AreEqual(0, p.RawGridLineCount, "이지는 격자 빔이 아니다");

            BossBeamManager manager = new BossBeamManager(new GridQuery());
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            RecordingListener listener = new RecordingListener();
            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, beams: manager, arena: arena, gridListener: listener);
            Assert.IsInstanceOf<BossBeamPattern>(factory.Create(p), "이지 ID는 기존 예약 빔 실행에 연결된 채다");
            Assert.IsInstanceOf<BossGridBeamPattern>(factory.Create(LoadDefinition(HARD_ID)), "하드 ID는 격자 실행에 연결된다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, arena: arena, gridListener: listener).Create(p), "이지 P02는 빔 창구 없이 만들 수 없는 채다");
        }

        [Test]
        public void Asset_Catalog_KeepsEasyResultsUnchangedAndHardPhaseTwoOpensWithSeven()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            var easy = new List<PatternDefinition>();
            var hard = new List<PatternDefinition>();
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Easy, easy);
            catalog.CollectPatterns(WOLBAEK_ID, 2, EBossDifficulty.Hard, hard);

            Assert.AreEqual(7, easy.Count);
            Assert.AreEqual(7, hard.Count);
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
            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, LoadDefinition(HARD_ID)), "P02 한 개의 데이터 자체는 준비됐다");
        }

        [Test]
        public void Factory_NeedsTheArenaAndAListener_AndReadinessAgrees()
        {
            PatternDefinition p = LoadDefinition(HARD_ID);
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            RecordingListener listener = new RecordingListener();

            Assert.IsFalse(new WolbaekPatternFactory(HREF).CanCreate(p));
            Assert.IsNull(new WolbaekPatternFactory(HREF).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, arena: arena).CanCreate(p), "알림을 받을 쪽이 없으면 보이지 않는 피해가 나가므로 만들지 않는다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, arena: arena).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, gridListener: listener).CanCreate(p), "전장 입력이 없으면 만들 수 없다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, gridListener: listener).Create(p));
            Assert.IsTrue(new WolbaekPatternFactory(HREF, arena: arena, gridListener: listener).CanCreate(p));
            Assert.IsInstanceOf<BossGridBeamPattern>(new WolbaekPatternFactory(HREF, arena: arena, gridListener: listener).Create(p));
        }

        [Test]
        public void Validate_GridFields_RejectGapsShortCoverageMismatchedTimeAndMixedData()
        {
            BossPatternCatalogSO catalog = LoadCatalog();

            Assert.IsTrue(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(3f, 12, 3f, 0.5f, 1.5f, 12.5f)), "기준 값은 통과한다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(2.9f, 12, 3f, 0.5f, 1.5f, 12.5f)), "폭이 간격보다 좁으면 틈이 생긴다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(3f, 11, 3f, 0.5f, 1.5f, 11.5f)), "줄 전체가 전장 지름 36m를 덮지 못한다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(3f, 12, 3f, 0.5f, 1.5f, 13f)), "실행 시간이 첫 타격부터 마지막 타격보다 길다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(3f, 12, 3f, 0.5f, 1.5f, 12f)), "실행 시간이 짧아 마지막 줄이 밀린다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(3f, 12, 3f, 0f, 1.5f, 1.5f)), "여러 줄인데 타격 간격이 없다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(3f, 12, 0f, 0.5f, 1.5f, 12.5f)), "줄 간격이 없다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(3f, 17, 3f, 0.5f, 1.5f, 17.5f)), "줄 수 상한을 넘는다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(3f, 12, 3f, 0.5f, 1.5f, 12.5f, beamCount: 2)), "평행 빔 값과 섞을 수 없다");
            Assert.IsFalse(catalog.IsPatternReady(WOLBAEK_ID, MakeGrid(3f, 0, 3f, 0.5f, 1.5f, 12.5f)), "줄 수 없이 격자 값만 있다");
        }

        /****************************************
        *      Geometry: Axes, Order, No Gaps
        ****************************************/

        [Test]
        public void Geometry_RightOfSignsMatchTheWorldAxes()
        {
            Assert.AreEqual(0f, (Vector3.right - BossBeamGeometry.RightOf(Vector3.forward)).magnitude, TOLERANCE, "+Z 방향의 오른쪽은 +X");
            Assert.AreEqual(0f, (Vector3.forward - BossBeamGeometry.RightOf(Vector3.left)).magnitude, TOLERANCE, "-X 방향의 오른쪽은 +Z");
        }

        [Test]
        public void Geometry_HorizontalLinesAscendInZ_VerticalLinesAscendInX_WithThreeMeterCenters()
        {
            Run r = StandardRun();
            r.Start(10);
            BossGridBeamLayout layout = r.Pattern.Layout;

            Assert.AreEqual(ArenaCenter, layout.ArenaCenter, "보스 위치가 아니라 전장 중심");
            Assert.AreEqual(LINE_COUNT, layout.LineCount);
            for (int i = 0; i < LINE_COUNT; ++i)
            {
                BossBeamShape horizontal = r.Pattern.GetLineShape(i);
                BossBeamShape vertical   = r.Pattern.GetLineShape(LINE_COUNT + i);

                Assert.AreEqual(EBossGridBeamAxis.Horizontal, r.Pattern.GetAxis(i));
                Assert.AreEqual(EBossGridBeamAxis.Vertical, r.Pattern.GetAxis(LINE_COUNT + i));
                Assert.AreEqual(ArenaCenter.z + LineOffset(i), horizontal.LineCenter.z, TOLERANCE, "가로줄 " + i + "은 Z가 커지는 순서");
                Assert.AreEqual(ArenaCenter.x, horizontal.LineCenter.x, TOLERANCE);
                Assert.AreEqual(ArenaCenter.x + LineOffset(i), vertical.LineCenter.x, TOLERANCE, "세로줄 " + i + "은 X가 커지는 순서");
                Assert.AreEqual(ArenaCenter.z, vertical.LineCenter.z, TOLERANCE);
                Assert.AreEqual(3f, horizontal.Width, TOLERANCE);
                Assert.AreEqual(3f, vertical.Width, TOLERANCE);
                Assert.AreEqual(Vector3.left, horizontal.Direction, "가로줄은 월드 X 축과 나란하다");
                Assert.AreEqual(Vector3.forward, vertical.Direction, "세로줄은 월드 Z 축과 나란하다");
            }

            Assert.AreEqual(-16.5f, LineOffset(0), TOLERANCE);
            Assert.AreEqual(16.5f, LineOffset(LINE_COUNT - 1), TOLERANCE);
        }

        [Test]
        public void Geometry_AdjacentLinesTouchWithNoGap_AndTheOuterEdgesReachTheArenaEdge()
        {
            Run r = StandardRun();
            r.Start(10);
            BossGridBeamLayout layout = r.Pattern.Layout;

            Assert.IsTrue(layout.IsGapFree);
            Assert.IsTrue(layout.CoversArena);
            Assert.AreEqual(ARENA_RADIUS, layout.HalfExtent, TOLERANCE);

            for (int i = 0; i + 1 < LINE_COUNT; ++i)
            {
                BossBeamShape a = layout.GetLine(EBossGridBeamAxis.Horizontal, i);
                BossBeamShape b = layout.GetLine(EBossGridBeamAxis.Horizontal, i + 1);
                float centerDistance = b.LineCenter.z - a.LineCenter.z;
                Assert.AreEqual(3f, centerDistance, TOLERANCE, "중심선 간격 3m");
                Assert.GreaterOrEqual((a.Width * 0.5f) + (b.Width * 0.5f), centerDistance - TOLERANCE, "가장자리가 맞닿는다");
            }

            Assert.AreEqual(-ARENA_RADIUS, layout.GetLine(EBossGridBeamAxis.Horizontal, 0).LineCenter.z - ArenaCenter.z - 1.5f, TOLERANCE, "첫 줄의 바깥 가장자리");
            Assert.AreEqual(ARENA_RADIUS, layout.GetLine(EBossGridBeamAxis.Vertical, LINE_COUNT - 1).LineCenter.x - ArenaCenter.x + 1.5f, TOLERANCE, "끝 줄의 바깥 가장자리");
        }

        // 바깥 가장자리는 ((N - 1) x 간격 + 폭) / 2다. 폭이 간격과 다르면 N x 간격 / 2와 달라진다
        [TestCase(12, 3f, 3f, 18f)]
        [TestCase(12, 3f, 4f, 18.5f)]
        [TestCase(11, 3f, 6f, 18f)]
        [TestCase(1, 36f, 36f, 18f)]
        [TestCase(2, 4f, 8f, 6f)]
        public void Layout_HalfExtentIsTheOuterEdgeOfTheOutermostLines(int count, float spacing, float width, float expectedHalfExtent)
        {
            BossGridBeamLayout layout = new BossGridBeamLayout(ArenaCenter, 1f, count, width, spacing);

            Assert.AreEqual(expectedHalfExtent, layout.HalfExtent, TOLERANCE);

            BossBeamShape first = layout.GetLine(EBossGridBeamAxis.Horizontal, 0);
            BossBeamShape last  = layout.GetLine(EBossGridBeamAxis.Horizontal, count - 1);
            Assert.AreEqual(expectedHalfExtent, (ArenaCenter.z - first.LineCenter.z) + (width * 0.5f), TOLERANCE, "첫 줄 바깥 가장자리");
            Assert.AreEqual(expectedHalfExtent, (last.LineCenter.z - ArenaCenter.z) + (width * 0.5f), TOLERANCE, "끝 줄 바깥 가장자리");
        }

        [Test]
        public void Layout_WideLines_CoverTheArenaThatTheOldCountTimesSpacingFormulaWouldRefuse()
        {
            // 11줄 x 간격 3m = 33m라 예전 식은 반지름 18m를 덮지 못한다고 봤지만, 폭 6m면 바깥 가장자리가 +-18m까지 나간다
            BossGridBeamLayout wide = new BossGridBeamLayout(ArenaCenter, 18f, 11, 6f, 3f);
            Assert.AreEqual(18f, wide.HalfExtent, TOLERANCE);
            Assert.IsTrue(wide.IsGapFree);
            Assert.IsTrue(wide.CoversArena);

            BossBeamShape outer = wide.GetLine(EBossGridBeamAxis.Horizontal, 10);
            Assert.IsTrue(BossBeamGeometry.Contains(in outer, At(0f, 17.9f)), "끝 줄의 바깥 가장자리 안쪽 점이 든다");

            // 폭 3m = 간격이면 11줄은 33m라 반지름 18m를 덮지 못하는 채다
            BossGridBeamLayout exact = new BossGridBeamLayout(ArenaCenter, 18f, 11, 3f, 3f);
            Assert.AreEqual(16.5f, exact.HalfExtent, TOLERANCE);
            Assert.IsFalse(exact.CoversArena);
        }

        [Test]
        public void Layout_WideLines_LeaveNoGapAcrossTheWholeArenaWidth()
        {
            BossGridBeamLayout layout = new BossGridBeamLayout(ArenaCenter, 18f, 11, 6f, 3f);

            for (float z = -17.9f; z <= 17.9f; z += 0.5f)
            {
                int lines = 0;
                for (int i = 0; i < 11; ++i)
                {
                    BossBeamShape line = layout.GetLine(EBossGridBeamAxis.Horizontal, i);
                    lines += BossBeamGeometry.Contains(in line, At(0f, z)) ? 1 : 0;
                }

                Assert.GreaterOrEqual(lines, 1, "틈 z=" + z);
            }
        }

        [Test]
        public void Layout_PatternRejectsWideLinesOnlyWhenTheRealOuterEdgeFallsShortOfTheArena()
        {
            // 11줄, 폭 6m, 간격 3m는 바깥 가장자리 +-18m로 반지름 18m를 덮어 시작한다
            Run covered = CustomRun(MakeGrid(6f, 11, 3f, 0.5f, 1.5f, 11.5f), 18f);
            Assert.IsFalse(covered.Pattern.IsStartRejected);
            Assert.AreEqual(18f, covered.Pattern.Layout.HalfExtent, TOLERANCE);

            // 같은 줄에 반지름 18.5m 전장은 덮지 못해 거절한다
            Run tooBig = new Run();
            tooBig.Definition = MakeGrid(6f, 11, 3f, 0.5f, 1.5f, 11.5f);
            tooBig.Arena.Set(ArenaCenter, 18.5f);
            tooBig.Pattern = new BossGridBeamPattern(HREF, tooBig.Arena, tooBig.Listener);
            BossPatternContext context = new BossPatternContext(tooBig.Definition, tooBig.CastId, 10, tooBig.Self, tooBig.Query, tooBig.Gate, null);
            Assert.IsTrue(tooBig.Fsm.Start(tooBig.Definition, tooBig.Pattern, in context, 0.0));
            Assert.IsTrue(tooBig.Pattern.IsStartRejected);
        }

        [Test]
        public void Geometry_EveryPointInTheArenaIsInExactlyOneOrTwoLinesPerAxis_AndOutsideIsCut()
        {
            Run r = StandardRun();
            r.Start(10);
            BossGridBeamLayout layout = r.Pattern.Layout;

            for (float x = -20f; x <= 20f; x += 0.5f)
            {
                for (float z = -20f; z <= 20f; z += 0.5f)
                {
                    Vector3 point = At(x, z);
                    int horizontal = 0;
                    int vertical = 0;
                    for (int i = 0; i < LINE_COUNT; ++i)
                    {
                        BossBeamShape h = layout.GetLine(EBossGridBeamAxis.Horizontal, i);
                        BossBeamShape v = layout.GetLine(EBossGridBeamAxis.Vertical, i);
                        horizontal += BossBeamGeometry.Contains(in h, point) ? 1 : 0;
                        vertical   += BossBeamGeometry.Contains(in v, point) ? 1 : 0;
                    }

                    float distance = new Vector2(x, z).magnitude;
                    if (distance <= ARENA_RADIUS)
                    {
                        Assert.GreaterOrEqual(horizontal, 1, "가로 틈 (" + x + ", " + z + ")");
                        Assert.GreaterOrEqual(vertical, 1, "세로 틈 (" + x + ", " + z + ")");
                        Assert.LessOrEqual(horizontal, 2, "경계에서만 두 줄이 겹친다");
                        Assert.LessOrEqual(vertical, 2);
                    }
                    else if (distance > ARENA_RADIUS + 0.01f)
                    {
                        Assert.AreEqual(0, horizontal, "전장 밖은 잘린다 (" + x + ", " + z + ")");
                        Assert.AreEqual(0, vertical);
                    }
                }
            }
        }

        /****************************************
        *      Start: Fixed, Rejects, Announces
        ****************************************/

        [Test]
        public void Start_AnnouncesTheScheduleAndTheFirstLineTelegraphImmediately()
        {
            Run r = StandardRun();
            r.Start(10, 5.0);

            Assert.IsFalse(r.Pattern.IsStartRejected);
            Assert.AreEqual(1, r.Listener.StartedCasts.Count);
            Assert.AreEqual(r.CastId, r.Listener.StartedCasts[0]);
            Assert.AreEqual(5.0, r.Listener.StartedAt[0], TIME_TOL);
            Assert.AreEqual(1f, r.Listener.FirstStrikes[0], TOLERANCE);
            Assert.AreEqual(0.5f, r.Listener.Intervals[0], TOLERANCE);
            Assert.AreEqual(1.5f, r.Listener.AxisGaps[0], TOLERANCE);
            Assert.AreEqual(ArenaCenter, r.Listener.StartedLayouts[0].ArenaCenter);
            Assert.AreEqual(ARENA_RADIUS, r.Listener.StartedLayouts[0].ArenaRadius, TOLERANCE);

            Assert.AreEqual(1, r.Listener.Telegraphs.Count, "첫 줄의 예고는 시전 시작 순간이다");
            Assert.AreEqual(5.0, r.Listener.Telegraphs[0].TelegraphAt, TIME_TOL);
            Assert.AreEqual(6.0, r.Listener.Telegraphs[0].StrikeAt, TIME_TOL);
            Assert.AreEqual(0, r.Resolver.Requests.Count, "예고만으로는 피해가 없다");
        }

        [Test]
        public void Start_NeedsNoTarget_AndStaysFixedWhenTheTargetBossOrArenaChange()
        {
            Run r = new Run();
            r.Start(77);
            Assert.IsFalse(r.Pattern.IsStartRejected, "격자는 월드 축 기준이라 대상이 필요하지 않다");

            Run moving = StandardRun();
            moving.Start(10);
            BossGridBeamLayout first = moving.Pattern.Layout;
            BossBeamShape firstLine = moving.Pattern.GetLineShape(3);

            FakeCombatTarget aimer = (FakeCombatTarget)moving.Query.Targets[0];
            aimer.Position = At(-8f, 3f);
            aimer.LifeState = EParticipantLifeState.Dead;
            moving.Self.Position = ArenaCenter;
            moving.Self.Forward = Vector3.back;
            moving.Arena.Set(Vector3.zero, 5f);
            moving.TickRange(0.25, 1.75);

            Assert.AreEqual(first.ArenaCenter, moving.Pattern.Layout.ArenaCenter);
            Assert.AreEqual(first.ArenaRadius, moving.Pattern.Layout.ArenaRadius);
            Assert.AreEqual(firstLine.LineCenter, moving.Pattern.GetLineShape(3).LineCenter);
        }

        [Test]
        public void Start_RejectsWithNothingAnnounced_WhenTheArenaIsMissingOrDoesNotFit()
        {
            Run noArena = StandardRun();
            noArena.Arena.Set(ArenaCenter, 0f);
            noArena.Start(10);
            Assert.IsTrue(noArena.Pattern.IsStartRejected);
            Assert.AreEqual(0, noArena.Listener.StartedCasts.Count);
            Assert.AreEqual(0, noArena.Listener.Telegraphs.Count);

            Run bigArena = StandardRun();
            bigArena.Arena.Set(ArenaCenter, 20f);
            bigArena.Start(10);
            Assert.IsTrue(bigArena.Pattern.IsStartRejected, "줄 전체(지름 36m)가 전장 지름보다 작으면 가장자리에 틈이 생기므로 거절한다");
            Assert.AreEqual(0, bigArena.Listener.Telegraphs.Count);

            bigArena.TickRange(0.25, 16.0);
            Assert.AreEqual(0, bigArena.Resolver.Requests.Count);
            Assert.AreEqual(0, bigArena.Listener.Strikes.Count, "거절한 시전은 타격하지 않는다");
        }

        [Test]
        public void Start_ASmallerArenaStillStarts_BecauseTheLinesOnlyHaveToCoverIt()
        {
            Run r = StandardRun();
            r.Arena.Set(ArenaCenter, 10f);
            r.Start(10);

            Assert.IsFalse(r.Pattern.IsStartRejected);
        }

        /****************************************
        *      Timing: 1.0 / 6.5 / 8.0 / 13.5 / 15.0
        ****************************************/

        [Test]
        public void Schedule_StrikeAndTelegraphTimesFollowTheData()
        {
            Run r = StandardRun();
            r.Start(10, 2.0);

            for (int k = 0; k < TOTAL_LINES; ++k)
            {
                Assert.AreEqual(2.0 + DesignedStrike(k), r.Pattern.GetStrikeTime(k), TIME_TOL, "타격 " + k);
                Assert.AreEqual(1.0, r.Pattern.GetStrikeTime(k) - r.Pattern.GetTelegraphTime(k), TIME_TOL, "발사 1초 전 예고 " + k);
            }

            Assert.AreEqual(2.0 + 1.0, r.Pattern.GetStrikeTime(0), TIME_TOL);
            Assert.AreEqual(2.0 + 6.5, r.Pattern.GetStrikeTime(LINE_COUNT - 1), TIME_TOL);
            Assert.AreEqual(2.0 + 8.0, r.Pattern.GetStrikeTime(LINE_COUNT), TIME_TOL);
            Assert.AreEqual(2.0 + 13.5, r.Pattern.GetStrikeTime(TOTAL_LINES - 1), TIME_TOL);
        }

        [Test]
        public void Boundaries_EveryLineStrikesExactlyAtItsTime_NotBefore()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, At(0f, LineOffset(0)));
            r.Add(12, At(0f, LineOffset(LINE_COUNT - 1)));

            r.TickAt(0.0);
            r.TickAt(0.99);
            Assert.AreEqual(0, r.Listener.Strikes.Count, "t=1.0 전에는 타격이 없다");

            r.TickAt(FIRST_STRIKE);
            Assert.AreEqual(1, r.Listener.Strikes.Count);
            Assert.AreEqual(1, r.Hits(11), "가로 첫 줄");
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);

            r.TickAt(HORIZONTAL_LAST - 0.01);
            Assert.AreEqual(LINE_COUNT - 1, r.Listener.Strikes.Count, "t=6.5 전에는 가로 마지막 줄이 없다");
            Assert.AreEqual(0, r.Hits(12));

            r.TickAt(HORIZONTAL_LAST);
            Assert.AreEqual(LINE_COUNT, r.Listener.Strikes.Count);
            Assert.AreEqual(1, r.Hits(12), "가로 마지막 줄 t=6.5");

            r.TickAt(VERTICAL_FIRST - 0.01);
            Assert.AreEqual(LINE_COUNT, r.Listener.Strikes.Count, "t=8.0 전에는 세로 첫 줄이 없다");
            r.TickAt(VERTICAL_FIRST);
            Assert.AreEqual(LINE_COUNT + 1, r.Listener.Strikes.Count, "세로 첫 줄 t=8.0");

            r.TickAt(VERTICAL_LAST - 0.01);
            Assert.AreEqual(TOTAL_LINES - 1, r.Listener.Strikes.Count);
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);
            r.TickAt(VERTICAL_LAST);
            Assert.AreEqual(TOTAL_LINES, r.Listener.Strikes.Count, "Execute에서 Recover로 넘어가는 틱에도 마지막 줄이 빠지지 않는다");
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);

            for (int k = 0; k < TOTAL_LINES; ++k)
            {
                Assert.AreEqual(DesignedStrike(k), r.Listener.Strikes[k].Scheduled, TIME_TOL, "예정 시각 " + k);
            }
        }

        [Test]
        public void Fsm_NextSelectionOpensAt15_NotBefore()
        {
            Run r = StandardRun();
            r.Start(10);

            r.TickAt(VERTICAL_LAST);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(14.99);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State, "회복 1.5초 중에는 다음 패턴을 고르지 않는다");
            r.TickAt(NEXT_SELECTION);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State, "t=15.0에 다음 선택 가능");
            Assert.IsFalse(r.Fsm.IsControlWindowOpen(14.0), "직접 제어 구간 없음");
        }

        [TestCase(0.0)]
        [TestCase(2.0)]
        [TestCase(123.25)]
        public void Schedule_AllTwentyFourLines_MatchTheLiteralTableAtAnyCastStart(double castStart)
        {
            Run r = StandardRun();
            r.Start(10, castStart);

            Assert.AreEqual(TOTAL_LINES, DesignedStrikeTable.Length);
            for (int k = 0; k < TOTAL_LINES; ++k)
            {
                Assert.AreEqual(castStart + DesignedStrikeTable[k], r.Pattern.GetStrikeTime(k), TIME_TOL, "타격 " + k);
                Assert.AreEqual(castStart + DesignedStrikeTable[k] - 1.0, r.Pattern.GetTelegraphTime(k), TIME_TOL, "예고 " + k);
            }
        }

        [Test]
        public void Schedule_FirstVerticalIsExactlyTheAxisGapAfterTheLastHorizontal_NotOneIntervalMore()
        {
            Run r = StandardRun();
            r.Start(10);
            PatternDefinition p = r.Definition;

            double lastHorizontal = r.Pattern.GetStrikeTime(LINE_COUNT - 1);
            double firstVertical  = r.Pattern.GetStrikeTime(LINE_COUNT);
            Assert.AreEqual(6.5, lastHorizontal, TIME_TOL);
            Assert.AreEqual(8.0, firstVertical, TIME_TOL, "시전 시작 t=0일 때 첫 세로는 8.0초다. 8.5초가 아니다");
            Assert.AreEqual(p.GridAxisGapSeconds, firstVertical - lastHorizontal, TIME_TOL, "방향 전환 간격은 가로 마지막에서 세로 첫 줄까지다");
            Assert.AreEqual(p.GridStrikeIntervalSeconds, r.Pattern.GetStrikeTime(1) - r.Pattern.GetStrikeTime(0), TIME_TOL);
            Assert.AreEqual(p.GridStrikeIntervalSeconds, r.Pattern.GetStrikeTime(LINE_COUNT + 1) - firstVertical, TIME_TOL, "세로 안의 간격은 타격 간격이다");
        }

        [Test]
        public void Schedule_LastStrikeSitsOnTheExecuteEnd_AndTheNextSelectionIsRecoveryLater_ForTheRealDefinition()
        {
            Run r = StandardRun();
            r.Start(10, 4.0);
            PatternDefinition p = r.Definition;

            double executeEnd = 4.0 + p.TelegraphSeconds + p.ActiveSeconds;
            Assert.AreEqual(executeEnd, r.Pattern.GetStrikeTime(TOTAL_LINES - 1), TIME_TOL, "마지막 세로 줄은 실행 구간이 끝나는 경계다");
            Assert.AreEqual(4.0 + VERTICAL_LAST, r.Pattern.GetStrikeTime(TOTAL_LINES - 1), TIME_TOL);
            Assert.AreEqual(4.0 + NEXT_SELECTION, executeEnd + p.RecoverySeconds, TIME_TOL, "마지막 타격 1.5초 뒤가 다음 선택");

            r.TickAt(executeEnd - 0.01);
            Assert.AreEqual(TOTAL_LINES - 1, r.Listener.Strikes.Count, "마지막 줄은 경계 전에는 나가지 않는다");
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);

            r.TickAt(executeEnd);
            Assert.AreEqual(TOTAL_LINES, r.Listener.Strikes.Count);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);

            r.TickAt(executeEnd + p.RecoverySeconds - 0.01);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(executeEnd + p.RecoverySeconds);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [Test]
        public void Schedule_TickingExactlyAtEachTableTime_StrikesEachLineOnTimeAndNeverEarlier()
        {
            Run r = StandardRun();
            r.Start(10);

            for (int k = 0; k < TOTAL_LINES; ++k)
            {
                if (k > 0)
                {
                    r.TickAt(DesignedStrikeTable[k] - 0.01);
                    Assert.AreEqual(k, r.Listener.Strikes.Count, "타격 " + k + "은 " + DesignedStrikeTable[k] + "초 전에 나가지 않는다");
                }

                r.TickAt(DesignedStrikeTable[k]);
                Assert.AreEqual(k + 1, r.Listener.Strikes.Count, "타격 " + k + "은 " + DesignedStrikeTable[k] + "초에 나간다");
                Assert.AreEqual(DesignedStrikeTable[k], r.Listener.Strikes[k].Scheduled, TIME_TOL);
                Assert.AreEqual(DesignedStrikeTable[k], r.Listener.Strikes[k].Processed, TIME_TOL, "늦지 않게 예정 시각에 처리했다");
            }

            Assert.AreEqual(TOTAL_LINES, r.Listener.Telegraphs.Count);
            for (int k = 0; k < TOTAL_LINES; ++k)
            {
                Assert.AreEqual(DesignedStrikeTable[k], r.Listener.Telegraphs[k].StrikeAt, TIME_TOL, "예고가 알린 타격 시각 " + k);
                Assert.AreEqual(DesignedStrikeTable[k] - 1.0, r.Listener.Telegraphs[k].TelegraphAt, TIME_TOL, "예고 시작 " + k);
            }
        }

        // 값이 에셋과 다른 정의로도 가로 마지막 타격 + 방향 전환 간격 + 방향 안 간격이 맞는지 본다. 기대값은 손으로 계산해 적었다
        [Test]
        public void Schedule_OtherData_ThreeLines_FollowsLastHorizontalPlusAxisGap()
        {
            // 3줄, 간격 2.0m, 폭 2.0m(지름 6m라 전장 반지름 3m), 타격 0.5초, 방향 전환 2.0초, 실행 2 x 2 x 0.5 + 2.0 = 4.0초
            Run r = CustomRun(MakeGrid(2f, 3, 2f, 0.5f, 2.0f, 4.0f), 3f, 10.0);

            double[] expected = { 11.0, 11.5, 12.0, 14.0, 14.5, 15.0 };
            for (int k = 0; k < expected.Length; ++k)
            {
                Assert.AreEqual(expected[k], r.Pattern.GetStrikeTime(k), TIME_TOL, "타격 " + k);
            }

            r.TickAt(10.0 + 1.0 + 4.0);
            Assert.AreEqual(expected.Length, r.Listener.Strikes.Count);
            for (int k = 0; k < expected.Length; ++k)
            {
                Assert.AreEqual(expected[k], r.Listener.Strikes[k].Scheduled, TIME_TOL);
            }
        }

        [Test]
        public void Schedule_OtherData_SingleLine_VerticalComesOneAxisGapAfterTheHorizontal()
        {
            // 1줄이면 타격 간격이 0이어도 된다. 가로 t=1.0, 세로는 방향 전환 간격 1.5초 뒤 t=2.5, 실행 1.5초
            Run r = CustomRun(MakeGrid(36f, 1, 36f, 0f, 1.5f, 1.5f), 18f);

            Assert.AreEqual(1.0, r.Pattern.GetStrikeTime(0), TIME_TOL);
            Assert.AreEqual(2.5, r.Pattern.GetStrikeTime(1), TIME_TOL);

            r.TickAt(2.49);
            Assert.AreEqual(1, r.Listener.Strikes.Count);
            r.TickAt(2.5);
            Assert.AreEqual(2, r.Listener.Strikes.Count);
        }

        [Test]
        public void Timeline_ProcessesEachTelegraphAndStrikeOnceInTimeOrder()
        {
            Run r = StandardRun();
            r.Start(10);
            r.TickRange(0.0, 16.0, 0.25);

            Assert.AreEqual(TOTAL_LINES, r.Listener.Telegraphs.Count);
            Assert.AreEqual(TOTAL_LINES, r.Listener.Strikes.Count);
            Assert.AreEqual(TOTAL_LINES * 2, r.Listener.Timeline.Count);

            HashSet<int> telegraphed = new HashSet<int>();
            HashSet<int> struck = new HashSet<int>();
            double previous = double.MinValue;
            for (int i = 0; i < r.Listener.Timeline.Count; ++i)
            {
                TimelineEntry entry = r.Listener.Timeline[i];
                Assert.GreaterOrEqual(entry.Scheduled, previous - TIME_TOL, "예정 시각 순서 " + i);
                previous = entry.Scheduled;

                if (entry.IsStrike)
                {
                    Assert.IsTrue(telegraphed.Contains(entry.LineId), "예고 없이 타격하지 않는다");
                    Assert.IsTrue(struck.Add(entry.LineId), "타격은 줄마다 한 번");
                }
                else
                {
                    Assert.IsTrue(telegraphed.Add(entry.LineId), "예고는 줄마다 한 번");
                }
            }

            Assert.AreEqual(TOTAL_LINES, telegraphed.Count);
            for (int k = 0; k < TOTAL_LINES; ++k)
            {
                Assert.AreEqual(r.Pattern.GetLineId(k), r.Listener.Telegraphs[k].LineId, "발사 순서대로");
                Assert.AreEqual(DesignedStrike(k) - 1.0, r.Listener.Telegraphs[k].TelegraphAt, TIME_TOL, "발사 1초 전");
                Assert.AreEqual(DesignedStrike(k), r.Listener.Telegraphs[k].StrikeAt, TIME_TOL);
                Assert.AreEqual(k < LINE_COUNT ? EBossGridBeamAxis.Horizontal : EBossGridBeamAxis.Vertical, r.Listener.Telegraphs[k].Axis);
                Assert.AreEqual(k % LINE_COUNT, r.Listener.Telegraphs[k].Index);
            }
        }

        [Test]
        public void Timeline_TheTwentyFourLineIdsAreDistinct()
        {
            Run r = StandardRun();
            r.Start(10);

            HashSet<int> ids = new HashSet<int>();
            for (int k = 0; k < TOTAL_LINES; ++k)
            {
                Assert.IsTrue(ids.Add(r.Pattern.GetLineId(k)), "줄 번호 " + k);
                Assert.AreNotEqual(CombatIds.NONE, r.Pattern.GetLineId(k));
            }
        }

        // 클라이언트 복원은 시작 알림의 첫 줄 번호에 발사 순서를 더해 줄 번호를 만든다
        [Test]
        public void Start_FirstLineId_IsTheFirstLineAndTheRestFollowInFiringOrder()
        {
            Run r = StandardRun();
            r.Start(10);

            Assert.AreEqual(1, r.Listener.FirstLineIds.Count);
            int first = r.Listener.FirstLineIds[0];
            for (int k = 0; k < TOTAL_LINES; ++k)
            {
                Assert.AreEqual(first + k, r.Pattern.GetLineId(k), "발사 순서 " + k);
            }

            Assert.AreEqual(first, r.Listener.Telegraphs[0].LineId);
        }

        /****************************************
        *      Order And Hit Caps
        ****************************************/

        [Test]
        public void Order_HorizontalFromLowZToHighZ()
        {
            Run r = EmptyRun();
            for (int i = 0; i < LINE_COUNT; ++i)
            {
                r.Add(100 + i, At(0f, LineOffset(i)));
            }

            r.Start(100);

            r.TickRange(0.0, HORIZONTAL_LAST, 0.5);

            Assert.AreEqual(LINE_COUNT, r.Resolver.Requests.Count);
            for (int i = 0; i < LINE_COUNT; ++i)
            {
                Assert.AreEqual(100 + i, r.Resolver.Requests[i].HitTargetId, "월드 Z가 " + i + "번째로 작은 줄의 대상이 " + i + "번째로 맞는다");
                Assert.AreEqual(r.Pattern.GetLineId(i), r.Resolver.Requests[i].ActionInstanceId);
            }
        }

        [Test]
        public void Order_VerticalFromLowXToHighX_AfterTheHorizontalLines()
        {
            Run r = EmptyRun();
            for (int j = 0; j < LINE_COUNT; ++j)
            {
                r.Add(200 + j, At(LineOffset(j), 0f));
            }

            r.Start(200);

            r.TickRange(0.0, VERTICAL_LAST, 0.5);

            List<int> verticalOrder = new List<int>();
            HashSet<int> verticalLineIds = new HashSet<int>();
            for (int j = 0; j < LINE_COUNT; ++j)
            {
                verticalLineIds.Add(r.Pattern.GetLineId(LINE_COUNT + j));
            }

            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                if (verticalLineIds.Contains(r.Resolver.Requests[i].ActionInstanceId))
                {
                    verticalOrder.Add(r.Resolver.Requests[i].HitTargetId);
                }
            }

            Assert.AreEqual(LINE_COUNT, verticalOrder.Count);
            for (int j = 0; j < LINE_COUNT; ++j)
            {
                Assert.AreEqual(200 + j, verticalOrder[j], "월드 X가 " + j + "번째로 작은 줄의 대상이 " + j + "번째로 맞는다");
            }
        }

        [Test]
        public void Caps_AStationaryTargetOnALineBoundaryIsHitOncePerAxis_TwiceInTotal()
        {
            Run r = StandardRun();
            r.Start(10);
            FakeCombatTarget center = r.Add(11, ArenaCenter);
            Assert.IsTrue(BossBeamGeometry.Contains(r.Pattern.GetLineShape(5), center.Position), "가로 5번 줄 경계");
            Assert.IsTrue(BossBeamGeometry.Contains(r.Pattern.GetLineShape(6), center.Position), "가로 6번 줄 경계, 두 줄에 모두 든다");

            r.TickRange(0.0, 16.0, 0.25);

            Assert.AreEqual(2, r.Hits(11), "경계에 서 있어도 가로 한 번, 세로 한 번");
        }

        [Test]
        public void Caps_TheCrossingPointIsHitOncePerAxis_NotOncePerLineThatCoversIt()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, At(4.5f, 4.5f));

            r.TickRange(0.0, 16.0, 0.25);

            Assert.AreEqual(2, r.Hits(11), "교차점은 가로 줄 하나와 세로 줄 하나에 든다");
            Assert.AreEqual(4.5, r.Listener.Strikes[7].Scheduled, TIME_TOL, "가로 7번 줄은 t=4.5");
            Assert.AreEqual(1, r.Listener.Strikes[7].HitCount);
            Assert.AreEqual(1, r.Listener.Strikes[LINE_COUNT + 7].HitCount);
        }

        [Test]
        public void Caps_ATargetThatRunsThroughEveryLineIsStillHitOncePerAxis()
        {
            Run r = StandardRun();
            r.Start(10);
            FakeCombatTarget runner = r.Add(11, At(0f, LineOffset(0)));

            r.TickAt(0.0);
            for (int i = 0; i < LINE_COUNT; ++i)
            {
                runner.Position = At(0f, LineOffset(i));
                r.TickAt(FIRST_STRIKE + (i * 0.5));
            }

            Assert.AreEqual(1, r.Hits(11), "가로 12줄을 따라 달려도 가로는 한 번");

            for (int j = 0; j < LINE_COUNT; ++j)
            {
                runner.Position = At(LineOffset(j), 0f);
                r.TickAt(VERTICAL_FIRST + (j * 0.5));
            }

            r.TickRange(14.0, 16.0);
            Assert.AreEqual(2, r.Hits(11), "세로 12줄을 따라 달려도 세로는 한 번, 총 2회");
        }

        [Test]
        public void Caps_ATargetThatLeavesAfterBeingHitAndComesBackIsNotHitAgainOnTheSameAxis()
        {
            Run r = StandardRun();
            r.Start(10);
            FakeCombatTarget mover = r.Add(11, At(0f, LineOffset(2)));

            r.TickRange(0.0, 1.75, 0.25);
            Assert.AreEqual(0, r.Hits(11), "가로 2번 줄은 t=2.0");
            r.TickAt(2.0);
            Assert.AreEqual(1, r.Hits(11));

            mover.Position = At(0f, LineOffset(9));
            r.TickRange(2.25, HORIZONTAL_LAST, 0.25);
            Assert.AreEqual(0, r.Listener.Strikes[9].HitCount, "가로 9번 줄 t=5.5, 이미 가로를 맞은 대상은 다시 맞지 않는다");
            Assert.AreEqual(1, r.Hits(11));
        }

        [Test]
        public void Caps_EachLineStrikesAndThenIsSafe_NoResidualDamage()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, At(0f, LineOffset(0)));

            r.TickRange(0.0, 1.0, 0.25);
            Assert.AreEqual(1, r.Hits(11));
            r.TickRange(1.05, 6.0, 0.05);
            Assert.AreEqual(1, r.Hits(11), "맞은 줄은 곧바로 안전하다. 틱마다 추가 피해가 없다");
        }

        /****************************************
        *      Damage Contract
        ****************************************/

        [Test]
        public void Damage_IsUnblockableConfiguredAmountWithOneActionIdPerLine()
        {
            Run r = EmptyRun();
            r.Add(11, At(-7f, 7f));   // 가로 8번 줄, 세로 3번 줄 안쪽
            r.Add(12, At(-7f, 7f));
            r.Start(11);
            r.TickRange(0.0, 16.0);

            Assert.AreEqual(4, r.Resolver.Requests.Count, "대상 둘이 각각 가로 한 번, 세로 한 번");
            Assert.AreEqual(2, r.Hits(11));
            Assert.AreEqual(2, r.Hits(12));
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

            Assert.AreEqual(2, actionIds.Count, "맞은 줄마다 동작 번호가 하나, 같은 줄의 두 대상은 같은 번호");
        }

        [Test]
        public void Targets_ArenaCutSummonsDeadAndEnemies()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, At(17.9f, 0f));           // 전장 안쪽 끝
            r.Add(12, At(13f, 13f));            // 줄 격자 안이지만 전장 밖(반경 18.38m)
            r.Add(13, At(12.5f, 12.5f));        // 전장 안(반경 17.68m)
            r.Add(14, At(18.5f, 0f));           // 전장 밖
            r.Add(15, At(-4f, 4f), true);
            r.Add(16, At(-4f, 4f)).LifeState = EParticipantLifeState.Dead;
            r.Add(17, At(-4f, 4f)).Faction = ECombatFaction.Enemy;
            r.TickRange(0.0, 16.0);

            Assert.AreEqual(2, r.Hits(11));
            Assert.AreEqual(0, r.Hits(12), "전장 밖 모서리는 잘린다");
            Assert.AreEqual(2, r.Hits(13));
            Assert.AreEqual(0, r.Hits(14), "전장 밖");
            Assert.AreEqual(2, r.Hits(15), "소환물도 피해를 받는다");
            Assert.AreEqual(0, r.Hits(16), "죽은 대상");
            Assert.AreEqual(0, r.Hits(17), "적 진영");
        }

        [Test]
        public void Targets_APlayerWhoDiesMidwayIsNotHitAfterwards()
        {
            Run r = StandardRun();
            r.Start(10);
            FakeCombatTarget victim = r.Add(11, At(0f, LineOffset(4)));
            r.Add(12, At(0f, LineOffset(8)));

            r.TickRange(0.0, 2.5, 0.5);
            victim.LifeState = EParticipantLifeState.Dead;
            r.TickRange(3.0, 16.0, 0.5);

            Assert.AreEqual(0, r.Hits(11), "가로 4번 줄은 t=3.0이라 죽은 뒤");
            Assert.AreEqual(2, r.Hits(12));
        }

        /****************************************
        *      Big Tick Jumps
        ****************************************/

        [Test]
        public void BigTick_FromStartTo20Seconds_ProcessesAll24LinesOnceInOrder()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, At(5f, -5f));
            r.Add(12, At(-12f, 9f));

            r.TickAt(20.0);

            Assert.AreEqual(TOTAL_LINES, r.Listener.Strikes.Count, "예정 줄을 빠뜨리지 않는다");
            Assert.AreEqual(TOTAL_LINES, r.Listener.Telegraphs.Count);
            Assert.AreEqual(2, r.Hits(11));
            Assert.AreEqual(2, r.Hits(12));
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            for (int k = 0; k < TOTAL_LINES; ++k)
            {
                Assert.AreEqual(k, FindStrikeOrder(r, r.Pattern.GetLineId(k)), "줄 순서대로");
                Assert.AreEqual(20.0, r.Listener.Strikes[k].Processed, TIME_TOL, "실제 처리 시각은 틱 시각으로 보고한다");
                Assert.AreEqual(DesignedStrike(k), r.Listener.Strikes[k].Scheduled, TIME_TOL);
            }

            r.TickRange(20.25, 30.0);
            Assert.AreEqual(TOTAL_LINES, r.Listener.Strikes.Count, "두 번 실행하지 않는다");
            Assert.AreEqual(2, r.Hits(11));
        }

        private static int FindStrikeOrder(Run r, int lineId)
        {
            for (int i = 0; i < r.Listener.Strikes.Count; ++i)
            {
                if (r.Listener.Strikes[i].LineId == lineId)
                {
                    return i;
                }
            }

            return -1;
        }

        [Test]
        public void BigTick_AcrossPartOfTheSequence_ProcessesOnlyTheDueLinesAndKeepsTheRest()
        {
            Run r = StandardRun();
            r.Start(10);

            r.TickAt(0.0);
            r.TickAt(9.0);

            Assert.AreEqual(LINE_COUNT + 3, r.Pattern.StrikeCount, "가로 12줄과 세로 8.0, 8.5, 9.0");
            Assert.AreEqual(LINE_COUNT + 5, r.Pattern.TelegraphCount, "세로 예고는 7.0부터 9.0까지 5줄");
            r.TickAt(9.0);
            Assert.AreEqual(LINE_COUNT + 3, r.Pattern.StrikeCount, "같은 시각을 두 번 처리하지 않는다");

            r.TickAt(9.4);
            Assert.AreEqual(LINE_COUNT + 3, r.Pattern.StrikeCount, "남은 줄은 원래 일정 기준이라 9.5 전에는 나가지 않는다");
            r.TickAt(9.5);
            Assert.AreEqual(LINE_COUNT + 4, r.Pattern.StrikeCount);
        }

        [Test]
        public void BigTick_UsesCurrentTargetPositions_NotARestoredPastPosition()
        {
            Run r = StandardRun();
            r.Start(10);
            FakeCombatTarget mover = r.Add(11, At(0f, LineOffset(0)));
            mover.Position = At(0f, LineOffset(11));

            r.TickAt(20.0);

            Assert.AreEqual(0, r.Listener.Strikes[0].HitCount, "예정 시각의 과거 위치를 복원하지 않는다");
            Assert.AreEqual(1, r.Listener.Strikes[11].HitCount, "처리 시점의 현재 위치로 판정한다");
            Assert.AreEqual(2, r.Hits(11));
        }

        /****************************************
        *      Cancel And Cleanup
        ****************************************/

        [Test]
        public void Cancel_DuringTheFirstTelegraphs_AnnouncesOnceAndNeverStrikes()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, At(0f, LineOffset(0)));
            r.TickAt(0.5);

            r.Fsm.Cancel(EBossInterruptReason.Death);
            r.Fsm.Cancel(EBossInterruptReason.Death);
            r.TickRange(1.0, 16.0);

            Assert.AreEqual(1, r.Listener.CancelReasons.Count);
            Assert.AreEqual(2, r.Listener.CancelledTelegraphed[0], "t=0.0, 0.5의 두 줄을 예고했다");
            Assert.AreEqual(0, r.Listener.CancelledStruck[0]);
            Assert.AreEqual(0, r.Listener.Strikes.Count);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(2, r.Listener.Telegraphs.Count, "취소 뒤에는 새 예고도 없다");
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        public void Cancel_AfterSomeLines_ClearsPendingLinesButKeepsAppliedDamage(EBossInterruptReason reason)
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, At(0f, LineOffset(1)));
            r.Add(12, At(0f, LineOffset(10)));
            r.TickRange(0.0, 1.75, 0.25);
            Assert.AreEqual(1, r.Hits(11));

            r.Fsm.Cancel(reason);
            r.TickRange(2.25, 20.0);

            Assert.AreEqual(1, r.Hits(11), "이미 적용된 피해는 그대로, 남은 줄은 타격하지 않는다");
            Assert.AreEqual(0, r.Hits(12));
            Assert.AreEqual(2, r.Listener.Strikes.Count);
            Assert.AreEqual(1, r.Listener.CancelReasons.Count);
            Assert.AreEqual(2, r.Listener.CancelledStruck[0]);
            Assert.AreEqual(reason, r.Listener.CancelReasons[0]);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [Test]
        public void Cancel_ClosedGateStopsDamageEvenIfTheFsmIsNotCancelled_AndNothingIsRecordedAsHit()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, ArenaCenter);
            r.TickRange(0.0, 6.0);
            int before = r.Hits(11);
            Assert.AreEqual(1, before);

            r.Gate.CloseForGood();
            r.TickRange(6.25, 16.0);

            Assert.AreEqual(before, r.Hits(11));
        }

        /****************************************
        *      Brain: Hard Catalog Selection
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = ArenaCenter };
            public GridQuery          Query    = new GridQuery();
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

                WolbaekPatternFactory inner = new WolbaekPatternFactory(HREF, arena: Arena, gridListener: Listener);
                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog(), EBossDifficulty.Hard),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = new ExcludingPatternFactory(inner, HARD_P01_ID),
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
        public void Brain_HardPhaseTwo_RunsP02ForFifteenSecondsWithoutPickingAnotherThenSelectsAgain()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            for (double t = 0.5; t < 40.0; t += 0.05)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                }

                if ((started >= 0.0) && (t < (started + NEXT_SELECTION - 0.06)))
                {
                    Assert.AreEqual(HARD_ID, r.Brain.ActivePatternId, "t=" + t);
                    Assert.AreEqual(1, r.Listener.StartedCasts.Count, "15초 동안 다음 시전이 시작되지 않는다 t=" + t);
                }
            }

            LogAssert.NoUnexpectedReceived();
            Assert.GreaterOrEqual(started, 0.0);
            Assert.GreaterOrEqual(r.Listener.StartedCasts.Count, 2, "회복이 끝난 뒤에는 다시 고른다");
            Assert.GreaterOrEqual(r.Listener.StartedAt[1], r.Listener.StartedAt[0] + NEXT_SELECTION - TIME_TOL, "다음 시전은 t=15.0 이후");
            Assert.AreEqual(TOTAL_LINES, FirstCastStrikes(r));
        }

        private static int FirstCastStrikes(BrainRig r)
        {
            int count = 0;
            double secondStart = r.Listener.StartedAt[1];
            for (int i = 0; i < r.Listener.Strikes.Count; ++i)
            {
                count += (r.Listener.Strikes[i].Scheduled < secondStart) ? 1 : 0;
            }

            return count;
        }

        [Test]
        public void Brain_CombatEndMidSequence_StopsTheRemainingLines()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            for (double t = 0.5; t < 30.0; t += 0.05)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                }

                if ((started >= 0.0) && (t >= (started + 3.2)))
                {
                    break;
                }
            }

            int before = r.Listener.Strikes.Count;
            Assert.Greater(before, 0);
            Assert.Less(before, TOTAL_LINES);
            r.Brain.RequestCombatEnd();
            for (double t = started + 3.3; t < started + 20.0; t += 0.05)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(before, r.Listener.Strikes.Count, "전투 종료 뒤 남은 줄은 타격하지 않는다");
            Assert.AreEqual(1, r.Listener.CancelReasons.Count);
        }
    }
}
