// System
using System;
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
    // 월백 2페이즈 P04 강화형 지정 장판을 에셋의 실제 정의와 실제 팩토리, 실제 장판 관리자로 확인한다. 실행은 1페이즈 P05의 장판 패턴을 확장해 쓴다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다
    public class BossPhaseTwoAreaTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string P04_ID       = BossPatternCatalogSO.WOLBAEK_P2_P04_ID;
        private const string LEGACY_ID    = "wolbaek_p05";
        private const float  HREF         = 1000f;
        private const float  TOLERANCE    = 0.001f;
        private const double TIME_TOL     = 0.0001;
        private const double TELEGRAPH_END = 2.0;
        private const double AREA_END      = 8.0;
        private const float  RAW_DAMAGE    = 0.08f * HREF;

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class RadiusQuery : ICombatTargetQuery
        {
            public List<ICombatTarget> Targets { get; } = new List<ICombatTarget>();

            // 대상 번호 조회를 막는다
            public bool FailTryGet;

            // 0 이상이면 이 횟수만큼만 생존 플레이어를 돌려주고 그 뒤로는 비워서, 선택 뒤 대상이 사라진 상황을 만든다
            public int LivingPlayersBudget = -1;

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                if (LivingPlayersBudget == 0)
                {
                    return 0;
                }

                if (LivingPlayersBudget > 0)
                {
                    --LivingPlayersBudget;
                }

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

        // 지정한 순서대로 인덱스를 돌려주는 난수, 호출 인자(후보 수)를 기록한다
        private sealed class SequenceRandom : IBossRandom
        {
            private readonly Queue<int> _values = new Queue<int>();

            public List<int> Arguments { get; } = new List<int>();

            public void Enqueue(params int[] values)
            {
                for (int i = 0; i < values.Length; ++i)
                {
                    _values.Enqueue(values[i]);
                }
            }

            public float NextFloat01()
            {
                return 0f;
            }

            public int NextInt(int maxExclusive)
            {
                Arguments.Add(maxExclusive);
                return (_values.Count > 0) ? (_values.Dequeue() % maxExclusive) : 0;
            }
        }

        private struct Tick
        {
            public int    AreaId;
            public int    Index;
            public double Time;
        }

        private struct AreaEnd
        {
            public int         AreaId;
            public EBossAreaEnd Kind;
            public double      Time;
        }

        private sealed class RecordingListener : IBossAreaTelegraphListener, IBossAreaListener
        {
            public List<int>      TelegraphCasts   = new List<int>();
            public List<int>      TelegraphAreas   = new List<int>();
            public List<Vector3>  TelegraphCenters = new List<Vector3>();
            public List<int>      CancelledAreas   = new List<int>();
            public List<int>      ActivatedCasts   = new List<int>();
            public List<int>      ActivatedAreas   = new List<int>();
            public List<Vector3>  ActivatedCenters = new List<Vector3>();
            public List<double>   ActivatedTimes   = new List<double>();
            public List<Tick>     Ticks            = new List<Tick>();
            public List<AreaEnd>  Ends             = new List<AreaEnd>();

            public void OnTelegraphStarted(int castInstanceId, int areaId, Vector3 center, float radius, float telegraphSeconds)
            {
                TelegraphCasts.Add(castInstanceId);
                TelegraphAreas.Add(areaId);
                TelegraphCenters.Add(center);
            }

            public void OnCancelled(int castInstanceId, int areaId, EBossInterruptReason reason)
            {
                CancelledAreas.Add(areaId);
            }

            public void OnAreaActivated(int castInstanceId, int areaId, Vector3 center, float radius, float durationSeconds,
                float tickInterval, double activateServerTime)
            {
                ActivatedCasts.Add(castInstanceId);
                ActivatedAreas.Add(areaId);
                ActivatedCenters.Add(center);
                ActivatedTimes.Add(activateServerTime);
            }

            public void OnAreaTick(int castInstanceId, int areaId, int hitIndex, double roundServerTime, int hitCount)
            {
                Ticks.Add(new Tick { AreaId = areaId, Index = hitIndex, Time = roundServerTime });
            }

            public void OnAreaEnded(int castInstanceId, int areaId, EBossAreaEnd end, double endServerTime)
            {
                Ends.Add(new AreaEnd { AreaId = areaId, Kind = end, Time = endServerTime });
            }

            public int TicksOf(int areaId)
            {
                int count = 0;
                for (int i = 0; i < Ticks.Count; ++i)
                {
                    count += (Ticks[i].AreaId == areaId) ? 1 : 0;
                }

                return count;
            }
        }

        // 공격 FSM에 실제 팩토리가 만든 패턴을 올리고 장판 관리자를 BossCombat처럼 공격 진행 뒤에 부른다
        private sealed class Run
        {
            public FakeCombatTarget    Self      = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = new Vector3(100f, 0f, 100f) };
            public RadiusQuery         Query     = new RadiusQuery();
            public FakeDamageResolver  Resolver  = new FakeDamageResolver();
            public RecordingListener   Listener  = new RecordingListener();
            public SequenceRandom      Random    = new SequenceRandom();
            public BossDamageGate      Gate;
            public BossAreaManager     Manager;
            public BossAttackFsm       Fsm       = new BossAttackFsm();
            public PatternDefinition   Definition;
            public BossAreaPattern     Pattern;
            public int                 CastId;

            public Run()
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = LoadDefinition();
                Manager    = new BossAreaManager(Query, Listener);
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
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, areas: Manager, areaListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                Pattern = (BossAreaPattern)pattern;
                BossPatternContext context = new BossPatternContext(Definition, CastId, selectedTargetId, Self, Query, Gate, Random);
                Assert.IsTrue(Fsm.Start(Definition, pattern, in context, time));
            }

            public void TickAt(double time)
            {
                Fsm.Tick(time);
                Manager.Tick(time);
            }

            // 1초 간격으로 진행한다
            public void TickSeconds(double from, double to)
            {
                for (double t = from; t <= to + 0.0001; t += 1.0)
                {
                    TickAt(t);
                }
            }

            // 1페이즈 P05 같은 기존 장판 하나를 설치한다
            public int AddLegacyArea(Vector3 center, double activateTime)
            {
                int cast = Gate.IssueInstanceId();
                Assert.IsTrue(Manager.TryReserve(cast, LEGACY_ID, 3));
                BossAreaSpec spec = new BossAreaSpec(cast, 1, LEGACY_ID, center, 3f, 6f, 1f, RAW_DAMAGE, EDamageFlags.Unblockable,
                    activateTime, Gate);
                Assert.IsTrue(Manager.TryActivate(in spec));
                return cast;
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

        private static PatternDefinition LoadDefinition()
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern(P04_ID, out PatternDefinition definition));
            return definition;
        }

        private static readonly Vector3 PosA = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 PosB = new Vector3(30f, 0f, 0f);
        private static readonly Vector3 PosC = new Vector3(-30f, 0f, 0f);
        private static readonly Vector3 PosD = new Vector3(0f, 0f, 40f);

        /****************************************
        *      Asset, Factory, Readiness Agree
        ****************************************/

        [Test]
        public void Asset_P04_HasAgreedValuesAndKeepsSelectionData()
        {
            PatternDefinition p = LoadDefinition();

            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EPatternGroup.Pressure, p.Group);
            Assert.AreEqual(2f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0f, p.ActiveSeconds);
            Assert.AreEqual(1f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.TargetArea, p.Shape.Shape);
            Assert.AreEqual(3f, p.Shape.Range, TOLERANCE);
            Assert.AreEqual(6f, p.AreaDurationSeconds, TOLERANCE);
            Assert.AreEqual(1f, p.AreaTickInterval, TOLERANCE);
            Assert.AreEqual(0.08f, p.DamageRef, TOLERANCE);
            Assert.IsFalse(p.Guardable, "방어 불가");
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(2, p.AreaTargetCount);
            Assert.AreEqual(3, p.MaxConcurrentAreas, "기존 월백 장판을 합쳐 최대 3개");
            Assert.IsTrue(p.AreaSkipsMissedRounds);
            Assert.AreEqual(16f, p.BaseWeight, TOLERANCE);
            Assert.AreEqual(18f, p.EnhancedBaseWeight, TOLERANCE);
            Assert.AreEqual(6, BossAreaManager.ComputeHitCount(p.AreaDurationSeconds, p.AreaTickInterval));
        }

        [Test]
        public void Asset_PhaseOneP05_StaysSingleAreaAndSkipsMissedRounds()
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern(LEGACY_ID, out PatternDefinition p5));

            Assert.AreEqual(1, p5.Phase);
            Assert.AreEqual(1, p5.AreaTargetCount);
            Assert.AreEqual(0, p5.RawAreaTargetCount);
            Assert.IsTrue(p5.AreaSkipsMissedRounds, "1페이즈 P05도 지연 시 장판당 한 틱에 한 회, 이전 회차 생략");
            Assert.AreEqual(2f, p5.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(3, p5.MaxConcurrentAreas);
        }

        [Test]
        public void Asset_P04_FactoryCanCreateAndPatternIsReadyAgree()
        {
            PatternDefinition p = LoadDefinition();
            BossAreaManager manager = new BossAreaManager(new RadiusQuery());

            Assert.IsFalse(new WolbaekPatternFactory(HREF).CanCreate(p), "장판 창구가 없으면 만들 수 없다");
            Assert.IsNull(new WolbaekPatternFactory(HREF).Create(p));

            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, areas: manager);
            Assert.IsTrue(factory.CanCreate(p));
            Assert.IsInstanceOf<BossAreaPattern>(factory.Create(p));
            Assert.IsTrue(LoadCatalog().IsPatternReady(WOLBAEK_ID, p));
        }

        /****************************************
        *      Target Selection And Slot Counts
        ****************************************/

        [Test]
        public void Start_Solo_InstallsOneAreaAtTheSelectedTargetPosition()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Start(10);

            Assert.AreEqual(1, r.Pattern.AreaCount);
            Assert.AreEqual(1, r.Listener.TelegraphAreas.Count);
            Assert.AreEqual(PosA, r.Listener.TelegraphCenters[0]);
            Assert.AreEqual(1, r.Manager.TotalAreaCount);
            Assert.AreEqual(0, r.Random.Arguments.Count, "남은 후보가 없으면 난수를 쓰지 않는다");
        }

        [Test]
        public void Start_Party_FirstIsSelectedTargetAndSecondIsUniformFromTheOthers()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosB);
            r.Add(12, PosC);
            r.Add(13, PosD);
            r.Random.Enqueue(2); // 남은 후보 [11, 12, 13] 중 세 번째

            r.Start(10);

            Assert.AreEqual(2, r.Pattern.AreaCount);
            CollectionAssert.AreEqual(new[] { 3 }, r.Random.Arguments, "첫 대상을 뺀 3명 중에서 뽑는다");
            Assert.AreEqual(PosA, r.Listener.TelegraphCenters[0]);
            Assert.AreEqual(PosD, r.Listener.TelegraphCenters[1]);
        }

        [Test]
        public void Start_TwoPlayers_NeverPicksTheSamePlayerTwice()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosB);
            r.Random.Enqueue(0);

            r.Start(10);

            Assert.AreEqual(2, r.Pattern.AreaCount);
            Assert.AreNotEqual(r.Listener.TelegraphCenters[0], r.Listener.TelegraphCenters[1]);
        }

        [Test]
        public void Start_BothAreasShareTheCastIdAndHaveDistinctAreaIds()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosB);
            r.Random.Enqueue(0);

            r.Start(10);
            r.TickAt(TELEGRAPH_END);

            Assert.AreEqual(2, r.Listener.TelegraphAreas.Count);
            Assert.AreEqual(r.CastId, r.Listener.TelegraphCasts[0]);
            Assert.AreEqual(r.CastId, r.Listener.TelegraphCasts[1]);
            Assert.AreNotEqual(r.Listener.TelegraphAreas[0], r.Listener.TelegraphAreas[1]);
            Assert.AreNotEqual(r.CastId, r.Listener.TelegraphAreas[0]);
            Assert.AreEqual(2, r.Listener.ActivatedAreas.Count);
            CollectionAssert.AreEquivalent(r.Listener.TelegraphAreas, r.Listener.ActivatedAreas);
        }

        [Test]
        public void Start_SummonsAreNotInstallTargetsButTakeAreaDamage()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(20, PosA + new Vector3(1f, 0f, 0f), isSummon: true);
            r.Random.Enqueue(0);

            r.Start(10);
            Assert.AreEqual(1, r.Pattern.AreaCount, "소환물은 설치 대상이 아니라 후보가 하나도 남지 않는다");

            r.TickAt(TELEGRAPH_END);
            r.TickAt(TELEGRAPH_END);
            Assert.AreEqual(1, r.Hits(20), "소환물도 범위 피해를 받는다");
            Assert.AreEqual(1, r.Hits(10));
        }

        [Test]
        public void Start_PositionsAreFixedAtStartEvenIfTargetsMoveOrDie()
        {
            Run r = new Run();
            FakeCombatTarget a = r.Add(10, PosA);
            FakeCombatTarget b = r.Add(11, PosB);
            r.Random.Enqueue(0);
            r.Start(10);

            a.Position = new Vector3(50f, 0f, 50f);
            b.LifeState = EParticipantLifeState.Dead;
            r.TickAt(0.5);
            r.TickAt(TELEGRAPH_END);

            Assert.AreEqual(2, r.Listener.ActivatedAreas.Count, "이동과 사망이 설치를 바꾸지 않는다");
            Assert.AreEqual(PosA, r.Listener.ActivatedCenters[0]);
            Assert.AreEqual(PosB, r.Listener.ActivatedCenters[1]);
        }

        [Test]
        public void Start_SelectedTargetLookupFails_FillsFromOthersAndNeverFallsBackToTheBoss()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosB);
            r.Random.Enqueue(1, 0);

            r.Start(selectedTargetId: 999);

            Assert.AreEqual(2, r.Pattern.AreaCount, "두뇌가 고른 타깃이 사라져도 남은 생존 플레이어로 채운다");
            for (int i = 0; i < r.Listener.TelegraphCenters.Count; ++i)
            {
                Assert.AreNotEqual(r.Self.Position, r.Listener.TelegraphCenters[i], "보스 위치에는 설치하지 않는다");
            }
        }

        [Test]
        public void Start_NoValidTarget_RejectsAndLeavesNoReservation()
        {
            Run r = new Run();
            FakeCombatTarget a = r.Add(10, PosA);
            a.LifeState = EParticipantLifeState.Dead;

            r.Start(10);

            Assert.IsTrue(r.Pattern.IsStartRejected);
            Assert.AreEqual(0, r.Pattern.AreaCount);
            Assert.AreEqual(0, r.Manager.TotalAreaCount, "예약이 남지 않는다");
            Assert.AreEqual(0, r.Listener.TelegraphAreas.Count);
        }

        [Test]
        public void Start_FreeSlotsDecideHowManyAreasAreInstalledAndExistingAreasStay()
        {
            for (int existing = 0; existing <= 3; ++existing)
            {
                Run r = new Run();
                r.Add(10, PosA);
                r.Add(11, PosB);
                r.Add(12, PosC);
                r.Random.Enqueue(0);
                for (int i = 0; i < existing; ++i)
                {
                    r.AddLegacyArea(new Vector3(60f + i, 0f, 60f), 0.0);
                }

                r.Start(10);

                int expected = Math.Min(2, 3 - existing);
                Assert.AreEqual(expected, r.Pattern.AreaCount, "기존 장판 " + existing + "개");
                Assert.AreEqual(existing + expected, r.Manager.TotalAreaCount);
                Assert.AreEqual(existing + expected, r.Manager.ActiveCount + r.Manager.ReservedCount);
                Assert.AreEqual(existing, r.Manager.ActiveCount, "기존 장판은 지우지 않는다");
                Assert.AreEqual(expected == 0, r.Pattern.IsStartRejected);
            }
        }

        /****************************************
        *      Slot Return, Timeline, Damage
        ****************************************/

        [Test]
        public void Cancel_BeforeActivation_ReleasesEveryReservationOnceAndNotifiesEachArea()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosB);
            r.Random.Enqueue(0);
            r.Start(10);
            r.TickAt(0.5);
            Assert.AreEqual(2, r.Manager.ReservedCount);

            r.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            r.Manager.Cancel(EBossInterruptReason.PhaseTransition, CombatIds.NONE);
            r.Fsm.Cancel(EBossInterruptReason.PhaseTransition);

            Assert.AreEqual(0, r.Manager.TotalAreaCount);
            Assert.AreEqual(2, r.Listener.CancelledAreas.Count);
            Assert.AreEqual(0, r.Listener.ActivatedAreas.Count);

            // 슬롯이 정확히 한 번 돌아와 다시 예약할 수 있다
            for (int i = 0; i < 3; ++i)
            {
                Assert.IsTrue(r.Manager.TryReserveWithinTotal(900 + i, 900 + i, P04_ID, 3));
            }
        }

        [Test]
        public void Tick_ActivatesAtTelegraphEndWithFirstHitThenSixRoundsAndEndsAtSixSeconds()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosB);
            r.Random.Enqueue(0);
            r.Start(10);

            r.TickSeconds(0.0, 0.0);
            r.TickAt(TELEGRAPH_END - 0.01);
            Assert.AreEqual(0, r.Listener.ActivatedAreas.Count, "예고 중에는 활성하지 않는다");
            Assert.AreEqual(0, r.Resolver.Requests.Count);

            r.TickSeconds(TELEGRAPH_END, AREA_END);

            Assert.AreEqual(2, r.Listener.ActivatedAreas.Count);
            Assert.AreEqual(TELEGRAPH_END, r.Listener.ActivatedTimes[0], TIME_TOL);
            for (int i = 0; i < 2; ++i)
            {
                int area = r.Listener.ActivatedAreas[i];
                Assert.AreEqual(6, r.Listener.TicksOf(area), "활성 기준 0, 1, 2, 3, 4, 5초에 총 6회");
            }

            for (int i = 0; i < r.Listener.Ticks.Count; ++i)
            {
                Assert.AreEqual(TELEGRAPH_END + r.Listener.Ticks[i].Index, r.Listener.Ticks[i].Time, TIME_TOL);
            }

            Assert.AreEqual(2, r.Listener.Ends.Count);
            Assert.AreEqual(EBossAreaEnd.Expired, r.Listener.Ends[0].Kind);
            Assert.AreEqual(AREA_END, r.Listener.Ends[0].Time, TIME_TOL);
            Assert.AreEqual(0, r.Manager.TotalAreaCount, "수명이 끝나면 슬롯이 돌아온다");
            Assert.AreEqual(1, r.Hits(10) / 6, "한 장판당 6회, 대상은 자기 장판 안에서만 맞는다");
            Assert.AreEqual(6, r.Hits(10));
            Assert.AreEqual(6, r.Hits(11));
        }

        [Test]
        public void Tick_RecoveryEndsAfterRecoverySecondsAndFinishDoesNotRemoveAreas()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Start(10);

            r.TickAt(TELEGRAPH_END);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(TELEGRAPH_END + 0.99);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State, "후딜이 끝나기 전에는 다음 선택이 없다");
            r.TickAt(TELEGRAPH_END + 1.0);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.AreEqual(1, r.Manager.ActiveCount, "정상 종료는 장판을 지우지 않는다");

            r.Fsm.Cancel(EBossInterruptReason.DirectControl);
            r.Manager.Cancel(EBossInterruptReason.DirectControl, r.CastId);
            Assert.AreEqual(1, r.Manager.ActiveCount, "직접 제어도 지우지 않는다");
        }

        [Test]
        public void Tick_OverlappingAreas_StackIndependentlyPerRound()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosA);
            r.Random.Enqueue(0);
            r.Start(10);

            r.TickAt(TELEGRAPH_END);
            r.TickAt(TELEGRAPH_END);

            Assert.AreEqual(2, r.Hits(10), "같은 대상이 장판마다 한 번씩 맞는다");
            Assert.AreEqual(2, r.Hits(11));
            float total = 0f;
            HashSet<int> actions = new HashSet<int>();
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                if (request.HitTargetId == 10)
                {
                    total += request.RawDamage;
                    actions.Add(request.ActionInstanceId);
                    Assert.IsTrue(request.IsUnblockable, "방어 불가");
                    Assert.AreEqual(r.CastId, request.CastInstanceId);
                    Assert.AreEqual(P04_ID, request.SourceDefinitionId);
                }
            }

            Assert.AreEqual(0.16f * HREF, total, TOLERANCE, "두 장판 합계 0.16Href");
            Assert.AreEqual(2, actions.Count, "장판마다 별도 동작 번호");
        }

        [Test]
        public void Tick_ThreeOverlappingAreasIncludingAnExistingOne_SumToPointTwoFourHref()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosA);
            r.AddLegacyArea(PosA, TELEGRAPH_END);
            r.Random.Enqueue(0);
            r.Start(10);

            r.TickAt(TELEGRAPH_END);
            r.TickAt(TELEGRAPH_END);

            float total = 0f;
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                total += (r.Resolver.Requests[i].HitTargetId == 10) ? r.Resolver.Requests[i].RawDamage : 0f;
            }

            Assert.AreEqual(3, r.Hits(10), "기존 장판 하나와 새 장판 둘이 각각 한 번");
            Assert.AreEqual(0.24f * HREF, total, TOLERANCE);
        }

        [Test]
        public void Tick_EachRound_RequeriesTheCurrentAreaAndHitsEachTargetOnce()
        {
            Run r = new Run();
            FakeCombatTarget a = r.Add(10, PosA);
            r.Start(10);
            r.TickAt(TELEGRAPH_END);
            r.TickAt(TELEGRAPH_END);
            Assert.AreEqual(1, r.Hits(10));

            a.Position = new Vector3(20f, 0f, 0f);
            r.TickAt(2.0);
            Assert.AreEqual(1, r.Hits(10), "범위 밖으로 나가면 다음 회차에 맞지 않는다");

            a.Position = PosA;
            r.TickAt(3.0);
            r.TickAt(3.0);
            Assert.AreEqual(2, r.Hits(10), "한 틱에 같은 회차를 두 번 판정하지 않는다");
        }

        [Test]
        public void Tick_RadiusBoundaryIsInclusive()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(20, new Vector3(3f, 0f, 0f), isSummon: true);
            r.Add(21, new Vector3(3.01f, 0f, 0f), isSummon: true);
            r.Start(10);

            r.TickAt(TELEGRAPH_END);
            r.TickAt(TELEGRAPH_END);

            Assert.AreEqual(1, r.Hits(20), "경계 포함");
            Assert.AreEqual(0, r.Hits(21));
        }

        /****************************************
        *        Delayed Ticks And Cancellation
        ****************************************/

        [Test]
        public void Tick_Delayed_HitsOnlyTheLatestDueRoundOncePerAreaAndKeepsTheSchedule()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosB);
            r.Random.Enqueue(0);
            r.Start(10);
            r.TickAt(TELEGRAPH_END);
            int afterActivation = r.Resolver.Requests.Count;

            r.TickAt(TELEGRAPH_END + 3.5); // 활성 기준 3.5초, 회차 1, 2, 3이 밀렸다

            Assert.AreEqual(afterActivation + 2, r.Resolver.Requests.Count, "장판 둘이 각각 한 번");
            Tick latest = r.Listener.Ticks[r.Listener.Ticks.Count - 1];
            Assert.AreEqual(3, latest.Index, "가장 최근 회차만 판정하고 이전 회차는 건너뛴다");
            Assert.AreEqual(TELEGRAPH_END + 3.0, latest.Time, TIME_TOL);

            r.TickAt(TELEGRAPH_END + 3.9);
            Assert.AreEqual(afterActivation + 2, r.Resolver.Requests.Count);
            r.TickAt(TELEGRAPH_END + 4.0);
            Assert.AreEqual(afterActivation + 4, r.Resolver.Requests.Count, "다음 회차는 원래 활성 시각 기준이다");
            Assert.AreEqual(4, r.Listener.Ticks[r.Listener.Ticks.Count - 1].Index);
        }

        [Test]
        public void Tick_PastTheEndTime_EndsWithoutPendingDamage()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Start(10);
            r.TickAt(TELEGRAPH_END);
            int hits = r.Resolver.Requests.Count;

            r.TickAt(100.0);

            Assert.AreEqual(hits, r.Resolver.Requests.Count, "종료 시각을 지났으면 미처리 피해 없이 끝난다");
            Assert.AreEqual(1, r.Listener.Ends.Count);
            Assert.AreEqual(0, r.Manager.TotalAreaCount);
        }

        [Test]
        public void Tick_OneAreaExpiringDoesNotRemoveTheOthers()
        {
            Run r = new Run();
            r.Add(10, PosA);
            int legacyCast = r.AddLegacyArea(PosB, -2.0);
            r.Start(10);
            r.TickAt(0.0);

            r.TickAt(0.5);
            r.TickAt(TELEGRAPH_END);

            // 기존 장판은 -2초에 활성해 4초에 끝나고 새 장판은 7초에 끝난다
            r.TickAt(4.5);
            Assert.AreEqual(1, r.Listener.Ends.Count);
            Assert.AreEqual(legacyCast, r.Listener.Ends[0].AreaId);
            Assert.AreEqual(1, r.Manager.ActiveCount, "한 장판의 종료가 다른 장판을 지우지 않는다");
            Assert.AreEqual(1, r.Hits(10) > 0 ? 1 : 0);
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        public void Cancel_WholeCast_ClearsReservationsActiveAreasAndFurtherDamage(EBossInterruptReason reason)
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosB);
            r.Random.Enqueue(0);
            r.Start(10);
            r.TickAt(TELEGRAPH_END);
            r.TickAt(TELEGRAPH_END);
            int hits = r.Resolver.Requests.Count;

            r.Gate.CloseForGood();
            r.Fsm.Cancel(reason);
            r.Manager.Cancel(reason, CombatIds.NONE);
            r.TickAt(2.0);
            r.TickAt(5.0);

            Assert.AreEqual(0, r.Manager.TotalAreaCount);
            Assert.AreEqual(hits, r.Resolver.Requests.Count, "이미 적용한 피해는 그대로이고 추가 피해는 없다");
            Assert.AreEqual(2, r.Listener.Ends.Count);
            Assert.AreEqual(EBossAreaEnd.Cancelled, r.Listener.Ends[0].Kind);
        }

        [Test]
        public void Cancel_ByCastId_RemovesBothAreasOfThatCastButNotOtherCasts()
        {
            Run r = new Run();
            r.Add(10, PosA);
            r.Add(11, PosB);
            r.Random.Enqueue(0);
            int legacy = r.AddLegacyArea(PosC, 0.0);
            r.Start(10);
            r.TickAt(TELEGRAPH_END);
            Assert.AreEqual(3, r.Manager.ActiveCount);

            r.Manager.Cancel(EBossInterruptReason.PhaseTransition, r.CastId);

            Assert.AreEqual(1, r.Manager.ActiveCount);
            Assert.AreEqual(2, r.Listener.Ends.Count);
            for (int i = 0; i < r.Listener.Ends.Count; ++i)
            {
                Assert.AreNotEqual(legacy, r.Listener.Ends[i].AreaId);
            }
        }

        /****************************************
        *       Manager: Boss-Wide Cap And Ids
        ****************************************/

        [Test]
        public void Manager_ReserveWithinTotal_CountsEveryPatternAndRejectsDuplicateAreaId()
        {
            RadiusQuery query = new RadiusQuery();
            BossAreaManager manager = new BossAreaManager(query);

            Assert.IsTrue(manager.TryReserve(1, 1, LEGACY_ID, 3));
            Assert.IsTrue(manager.TryReserveWithinTotal(2, 21, P04_ID, 3));
            Assert.IsTrue(manager.TryReserveWithinTotal(2, 22, P04_ID, 3));
            Assert.IsFalse(manager.TryReserveWithinTotal(3, 31, P04_ID, 3), "패턴이 달라도 합쳐 3개");
            Assert.IsFalse(manager.TryReserveWithinTotal(2, 21, P04_ID, 9), "같은 장판 번호는 두 번 예약하지 않는다");
            Assert.AreEqual(3, manager.TotalAreaCount);

            manager.ReleaseReservation(21);
            manager.ReleaseReservation(21);
            Assert.AreEqual(2, manager.TotalAreaCount, "정확히 한 번만 돌아온다");
            Assert.IsTrue(manager.TryReserveWithinTotal(3, 31, P04_ID, 3));
        }

        [Test]
        public void Manager_PerPatternReserve_StaysIndependentPerPatternForPhaseOne()
        {
            BossAreaManager manager = new BossAreaManager(new RadiusQuery());

            Assert.IsTrue(manager.TryReserve(1, "pattern_a", 1));
            Assert.IsFalse(manager.TryReserve(2, "pattern_a", 1));
            Assert.IsTrue(manager.TryReserve(3, "pattern_b", 1), "1페이즈 단일 장판은 패턴별로 센다");
        }

        /****************************************
        *       Brain: Selection, Cap, Rejection
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget    Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Vector3.zero };
            public RadiusQuery         Query    = new RadiusQuery();
            public FakeDamageResolver  Damage   = new FakeDamageResolver();
            public FakeBossVitals      Vitals   = new FakeBossVitals();
            public FakeBossRandom      Random   = new FakeBossRandom();
            public RecordingListener   Listener = new RecordingListener();
            public BossAreaManager     Areas;
            public BossProjectileManager Projectiles;
            public BossBrain           Brain;
            public List<string>        Used     = new List<string>();

            public BrainRig(Vector3 playerPosition, int players = 2)
            {
                for (int i = 0; i < players; ++i)
                {
                    Query.Targets.Add(new FakeCombatTarget { CombatId = 10 + i, Faction = ECombatFaction.Player,
                        Position = playerPosition + new Vector3(i * 1f, 0f, 0f) });
                }

                Areas       = new BossAreaManager(Query, Listener);
                Projectiles = new BossProjectileManager(Query);
                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog()),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = new ExcludingPatternFactory(new WolbaekPatternFactory(HREF, projectiles: Projectiles, areas: Areas, areaListener: Listener),
                        BossPatternCatalogSO.WOLBAEK_P2_P03_ID),
                    Effects  = new BossEffectComposite(Projectiles, Areas),
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

            public void Run(double from, double to)
            {
                string last = string.Empty;
                for (double t = from; t <= to; t += 0.25)
                {
                    Brain.Tick(t);
                    string id = Brain.ActivePatternId;
                    if ((!string.IsNullOrEmpty(id)) && (id != last))
                    {
                        Used.Add(id);
                    }

                    last = id;
                }
            }
        }

        [Test]
        public void Brain_WithThreeAreasAlreadyPresent_P04IsNotACandidateUntilASlotFrees()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 3f), 3);
            r.TransitionAndHandoff();

            // 페이즈 전환은 기존 장판을 모두 정리하므로 인계 뒤에 기존 월백 장판 세 개를 둔다
            for (int i = 0; i < 3; ++i)
            {
                int cast = 700 + i;
                Assert.IsTrue(r.Areas.TryReserve(cast, LEGACY_ID, 3));
                BossAreaSpec spec = new BossAreaSpec(cast, 1, LEGACY_ID, new Vector3(80f, 0f, 80f), 3f, 600f, 1f, 1f,
                    EDamageFlags.Unblockable, 0.0, new BossDamageGate(r.Damage));
                Assert.IsTrue(r.Areas.TryActivate(in spec));
            }

            r.Random.Roll = 0f;
            r.Run(0.5, 12.0);

            LogAssert.NoUnexpectedReceived();
            Assert.IsFalse(r.Used.Contains(P04_ID), "빈 슬롯이 없으면 후보에서 제외한다");
            Assert.AreEqual(3, r.Areas.ActiveCount, "기존 장판을 지우고 설치하지 않는다");
            Assert.IsTrue(r.Used.Count > 0, "다른 패턴으로 전투는 이어진다");
        }

        [Test]
        public void Brain_WithRoomForAreas_P04IsSelectedThenAlternatesWithTheOtherPhaseTwoPatterns()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 3f), 3);
            r.TransitionAndHandoff();
            r.Random.Roll = 0f; // 가중치가 있는 첫 후보부터, 후보 순서는 카탈로그 순서(P04, P05, P06)

            r.Run(0.5, 14.0);

            LogAssert.NoUnexpectedReceived();
            Assert.GreaterOrEqual(r.Used.Count, 2);
            Assert.AreEqual(P04_ID, r.Used[0]);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_P2_P05_ID, r.Used[1], "직전 P04는 0이라 다음 후보가 선택된다");
            Assert.IsTrue(r.Listener.ActivatedAreas.Count >= 2, "P04가 장판을 활성했다");
        }

        [Test]
        public void Brain_RejectedStart_LeavesNoRecordAndRetriesWithoutErrors()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 3f), 1);
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            // 선택 때는 조회되지만 패턴이 시작할 때는 대상을 얻지 못하는 상황, 생존 플레이어 조회는 선택 한 번만 돌려준다
            r.Query.FailTryGet = true;
            r.Query.LivingPlayersBudget = 1;
            r.Run(0.5, 2.0);
            Assert.AreEqual(0, r.Listener.TelegraphAreas.Count, "시작을 거절해 예고도 없다");
            Assert.AreEqual(0, r.Areas.TotalAreaCount, "예약이 남지 않는다");
            LogAssert.NoUnexpectedReceived();

            // 다시 조회가 되면 같은 패턴이 직전 기록 없이 선택된다
            r.Query.FailTryGet = false;
            r.Query.LivingPlayersBudget = -1;
            r.Run(2.25, 6.0);
            Assert.IsTrue(r.Used.Contains(P04_ID), "시작 실패는 기록하지 않아 직전 사용 x0이 걸리지 않는다");
        }

        [Test]
        public void Brain_HealthCrossing25DuringTheCast_KeepsTargetCountAndTiming()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 3f), 3);
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            for (double t = 0.5; t < 10.0; t += 0.25)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                    r.Vitals.HealthRate = 0.2f;
                }

                // 첫 시전의 예고와 활성까지만 본다
                if ((started >= 0.0) && (t >= (started + TELEGRAPH_END + 0.25)))
                {
                    break;
                }
            }

            Assert.GreaterOrEqual(started, 0.0);
            Assert.AreEqual(2, r.Listener.ActivatedAreas.Count, "대상 수는 체력으로 바뀌지 않는다");
            Assert.AreEqual(started + TELEGRAPH_END, r.Listener.ActivatedTimes[0], TIME_TOL, "예고 시간도 그대로다");
        }
    }
}
