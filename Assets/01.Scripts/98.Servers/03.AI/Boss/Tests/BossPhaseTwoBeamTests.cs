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
    // 월백 2페이즈 P02 필드 절단 여우불 빔을 에셋의 실제 정의와 실제 팩토리, 실제 빔 관리자로 확인한다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 전장 중심과 반지름은 고정 전장 입력으로 넣는다
    public class BossPhaseTwoBeamTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string P02_ID       = BossPatternCatalogSO.WOLBAEK_P2_P02_ID;
        private const float  HREF         = 1000f;
        private const float  RAW_DAMAGE   = 0.08f * HREF;
        private const float  TOLERANCE    = 0.001f;
        private const double TIME_TOL     = 0.0001;
        private const double TELEGRAPH_END = 1.5;
        private const double BEAM_END      = 5.5;
        private const double RECOVERY_END  = 6.3;
        private const float  ARENA_RADIUS  = 18f;

        private static readonly Vector3 ArenaCenter = new Vector3(50f, 0f, -30f);
        private static readonly Vector3 BossStart   = new Vector3(100f, 0f, 100f);

        // 방향 +z일 때 왼쪽 빔은 x=45, 오른쪽 빔은 x=55 줄이다
        private static readonly Vector3 InLeft   = new Vector3(45f, 0f, -20f);
        private static readonly Vector3 InRight  = new Vector3(55f, 0f, -20f);
        private static readonly Vector3 InGap    = new Vector3(50f, 0f, -30f);
        private static readonly Vector3 Outer    = new Vector3(62f, 0f, -30f);

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class BeamQuery : ICombatTargetQuery
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

        private struct Tick
        {
            public int    BeamId;
            public int    Index;
            public double Time;
            public int    Hits;
        }

        private struct Ended
        {
            public int          BeamId;
            public EBossAreaEnd Kind;
            public double       Time;
        }

        private sealed class RecordingListener : IBossBeamTelegraphListener, IBossBeamListener
        {
            public List<int>            TelegraphCasts    = new List<int>();
            public List<int>            TelegraphBeams    = new List<int>();
            public List<BossBeamShape>  TelegraphShapes   = new List<BossBeamShape>();
            public List<float>          TelegraphSeconds  = new List<float>();
            public List<float>          TelegraphDurations = new List<float>();
            public List<int>            CancelledBeams    = new List<int>();
            public List<int>            ActivatedBeams    = new List<int>();
            public List<double>         ActivatedTimes    = new List<double>();
            public List<Tick>           Ticks             = new List<Tick>();
            public List<Ended>          Ends              = new List<Ended>();

            public void OnTelegraphStarted(int castInstanceId, int beamId, in BossBeamShape shape, float telegraphSeconds,
                float durationSeconds, float tickInterval)
            {
                TelegraphCasts.Add(castInstanceId);
                TelegraphBeams.Add(beamId);
                TelegraphShapes.Add(shape);
                TelegraphSeconds.Add(telegraphSeconds);
                TelegraphDurations.Add(durationSeconds);
            }

            public void OnCancelled(int castInstanceId, int beamId, EBossInterruptReason reason)
            {
                CancelledBeams.Add(beamId);
            }

            public void OnBeamActivated(int castInstanceId, int beamId, float durationSeconds, float tickInterval, double activateServerTime)
            {
                ActivatedBeams.Add(beamId);
                ActivatedTimes.Add(activateServerTime);
            }

            public void OnBeamTick(int castInstanceId, int beamId, int hitIndex, double roundServerTime, int hitCount)
            {
                Ticks.Add(new Tick { BeamId = beamId, Index = hitIndex, Time = roundServerTime, Hits = hitCount });
            }

            public void OnBeamEnded(int castInstanceId, int beamId, EBossAreaEnd end, double endServerTime)
            {
                Ends.Add(new Ended { BeamId = beamId, Kind = end, Time = endServerTime });
            }

            public List<Tick> TicksOf(int beamId)
            {
                List<Tick> result = new List<Tick>();
                for (int i = 0; i < Ticks.Count; ++i)
                {
                    if (Ticks[i].BeamId == beamId)
                    {
                        result.Add(Ticks[i]);
                    }
                }

                return result;
            }
        }

        // 공격 FSM에 실제 팩토리가 만든 패턴을 올리고 빔 관리자를 BossCombat처럼 공격 진행 뒤에 부른다
        private sealed class Run
        {
            public FakeCombatTarget    Self      = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossStart };
            public BeamQuery           Query     = new BeamQuery();
            public FakeDamageResolver  Resolver  = new FakeDamageResolver();
            public RecordingListener   Listener  = new RecordingListener();
            public BossFixedArena      Arena     = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossDamageGate      Gate;
            public BossBeamManager     Manager;
            public BossAttackFsm       Fsm       = new BossAttackFsm();
            public PatternDefinition   Definition;
            public BossBeamPattern     Pattern;
            public int                 CastId;
            public bool                Started;

            public Run()
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = LoadDefinition();
                Manager    = new BossBeamManager(Query, Listener);
                CastId     = Gate.IssueInstanceId();
            }

            public FakeCombatTarget Add(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget target = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player,
                    Position = position, IsSummon = isSummon };
                Query.Targets.Add(target);
                return target;
            }

            // 보스에서 지정한 방향으로 10m 떨어진 곳에 선택 대상을 둔다
            public FakeCombatTarget AddAimer(int id, Vector3 direction)
            {
                return Add(id, Self.Position + (direction.normalized * 10f));
            }

            public void Start(int selectedTargetId, double time = 0.0)
            {
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, beams: Manager, arena: Arena, beamListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                Pattern = (BossBeamPattern)pattern;
                BossPatternContext context = new BossPatternContext(Definition, CastId, selectedTargetId, Self, Query, Gate);
                Started = Fsm.Start(Definition, pattern, in context, time);
                Assert.IsTrue(Started);
            }

            public void TickAt(double time)
            {
                Fsm.Tick(time);
                Manager.Tick(time);
            }

            public void TickRange(double from, double to, double step = 0.5)
            {
                for (double t = from; t <= to + 0.000001; t += step)
                {
                    TickAt(t);
                }
            }

            // BossBrain이 사망, 페이즈 전환, 전투 종료에서 하는 순서: FSM 취소 뒤 지속물 정리
            public void CancelAll(EBossInterruptReason reason)
            {
                Fsm.Cancel(reason);
                Manager.Cancel(reason, CombatIds.NONE);
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
            Assert.IsTrue(LoadCatalog().TryGetPattern(P02_ID, out PatternDefinition definition));
            return definition;
        }

        // 선택 대상 10을 두고 한 줄 방향이 +z가 되는 표준 구성
        private static Run StandardRun()
        {
            Run r = new Run();
            r.AddAimer(10, Vector3.forward);
            return r;
        }

        /****************************************
        *      Asset, Factory, Readiness Agree
        ****************************************/

        [Test]
        public void Asset_P02_HasAgreedValuesAndKeepsSelectionData()
        {
            PatternDefinition p = LoadDefinition();

            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EPatternGroup.Large, p.Group);
            Assert.AreEqual(1.5f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(4f, p.ActiveSeconds, TOLERANCE, "빔 유지 시간이 곧 실행 구간이다");
            Assert.AreEqual(0.8f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Line, p.Shape.Shape);
            Assert.AreEqual(2f, p.Shape.Width, TOLERANCE);
            Assert.AreEqual(2, p.RawBeamCount);
            Assert.AreEqual(5f, p.BeamLateralOffset, TOLERANCE);
            Assert.AreEqual(0.5f, p.BeamTickInterval, TOLERANCE);
            Assert.AreEqual(0.08f, p.DamageRef, TOLERANCE);
            Assert.IsFalse(p.Guardable, "방어 불가");
            Assert.IsFalse(p.HasControlWindow, "직접 제어 구간 없음");
            Assert.AreEqual(0f, p.TrackingSeconds, "추적하지 않는다");
            Assert.AreEqual(10f, p.BaseWeight, TOLERANCE);
            Assert.AreEqual(12f, p.EnhancedBaseWeight, TOLERANCE);
            Assert.AreEqual(8, BossAreaManager.ComputeHitCount(p.ActiveSeconds, p.BeamTickInterval), "활성 순간부터 0.5초 간격 8회");
        }

        [Test]
        public void Asset_P02_FactoryNeedsBeamHostAndArena_AndReadinessAgrees()
        {
            PatternDefinition p = LoadDefinition();
            BossBeamManager manager = new BossBeamManager(new BeamQuery());
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);

            Assert.IsFalse(new WolbaekPatternFactory(HREF).CanCreate(p));
            Assert.IsNull(new WolbaekPatternFactory(HREF).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, beams: manager).CanCreate(p), "전장 입력이 없으면 만들 수 없다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, beams: manager).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, arena: arena).CanCreate(p), "빔 창구가 없으면 만들 수 없다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, arena: arena).Create(p));

            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, beams: manager, arena: arena);
            Assert.IsTrue(factory.CanCreate(p));
            Assert.IsInstanceOf<BossBeamPattern>(factory.Create(p));
            Assert.IsTrue(LoadCatalog().IsPatternReady(WOLBAEK_ID, p));
        }

        [Test]
        public void Asset_OtherPhaseTwoWeightsAndPhaseOneStayUntouched()
        {
            Assert.IsTrue(LoadCatalog().IsPhaseTuned(WOLBAEK_ID, 1), "1페이즈 정의는 그대로 유효하다");

            Assert.IsTrue(LoadCatalog().TryGetPattern(BossPatternCatalogSO.WOLBAEK_P2_P03_ID, out PatternDefinition p03));
            Assert.AreEqual(12f, p03.BaseWeight, TOLERANCE);
            Assert.AreEqual(14f, p03.EnhancedBaseWeight, TOLERANCE);
            Assert.IsTrue(LoadCatalog().TryGetPattern(BossPatternCatalogSO.WOLBAEK_P2_P06_ID, out PatternDefinition p06));
            Assert.AreEqual(8f, p06.BaseWeight, TOLERANCE);
            Assert.IsTrue(LoadCatalog().TryGetPattern(BossPatternCatalogSO.WOLBAEK_P2_P01_ID, out PatternDefinition p01));
            Assert.AreEqual(10f, p01.BaseWeight, TOLERANCE);
            Assert.IsTrue(LoadCatalog().IsPatternReady(WOLBAEK_ID, p01), "P01 별형 빔도 수치가 들어 있다");
        }

        /****************************************
        *      Placement At Telegraph Start
        ****************************************/

        [Test]
        public void Start_PlacesTwoBeamsOnTheArenaCenterNotTheBossPosition()
        {
            Run r = StandardRun();
            r.Start(10);

            Assert.AreEqual(2, r.Pattern.BeamCount);
            Assert.AreEqual(2, r.Listener.TelegraphBeams.Count);
            Assert.AreNotEqual(r.Listener.TelegraphBeams[0], r.Listener.TelegraphBeams[1], "빔마다 번호가 다르다");
            Assert.AreEqual(r.CastId, r.Listener.TelegraphCasts[0]);
            Assert.AreEqual(r.CastId, r.Listener.TelegraphCasts[1], "시전 번호는 함께 쓴다");

            BossBeamShape left  = r.Listener.TelegraphShapes[0];
            BossBeamShape right = r.Listener.TelegraphShapes[1];
            Assert.AreEqual(ArenaCenter, left.ArenaCenter);
            Assert.AreEqual(ARENA_RADIUS, left.ArenaRadius, TOLERANCE);
            Assert.AreEqual(Vector3.forward, left.Direction);
            Assert.AreEqual(new Vector3(45f, 0f, -30f), left.LineCenter);
            Assert.AreEqual(new Vector3(55f, 0f, -30f), right.LineCenter);
            Assert.AreEqual(2f, left.Width, TOLERANCE);
            Assert.AreEqual(1.5f, r.Listener.TelegraphSeconds[0], TOLERANCE);
            Assert.AreEqual(4f, r.Listener.TelegraphDurations[0], TOLERANCE);
            Assert.AreEqual(2, r.Manager.ReservedCount);
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [Test]
        public void Start_BaseDirectionIsHorizontalFromBossToTarget()
        {
            Run r = new Run();
            r.Self.Position = new Vector3(20f, 3f, 10f);
            r.Add(10, new Vector3(20f, 9f, 40f)); // 높이만 다르고 +z 쪽
            r.Start(10);
            Assert.AreEqual(0f, r.Pattern.BaseDirection.y, TOLERANCE);
            Assert.AreEqual(Vector3.forward, r.Pattern.BaseDirection);

            Run diagonal = new Run();
            diagonal.Self.Position = new Vector3(0f, 0f, 0f);
            diagonal.Add(10, new Vector3(10f, 0f, 10f));
            diagonal.Start(10);
            Assert.AreEqual(new Vector3(1f, 0f, 1f).normalized, diagonal.Pattern.BaseDirection);
        }

        [Test]
        public void Start_BossFarOutsideCenter_DoesNotChangeArenaAnchoredPlacement()
        {
            Run near = StandardRun();
            near.Self.Position = ArenaCenter + new Vector3(0f, 0f, -17f);
            near.Query.Targets.Clear();
            near.AddAimer(10, Vector3.forward);
            near.Start(10);

            Run far = StandardRun();
            far.Start(10);

            for (int i = 0; i < 2; ++i)
            {
                Assert.AreEqual(near.Listener.TelegraphShapes[i].LineCenter, far.Listener.TelegraphShapes[i].LineCenter);
                Assert.AreEqual(ArenaCenter, far.Listener.TelegraphShapes[i].ArenaCenter);
            }
        }

        [Test]
        public void Start_TargetOnTopOfBoss_UsesTheBossForward()
        {
            Run r = new Run();
            r.Self.Forward = Vector3.right;
            r.Add(10, new Vector3(BossStart.x, 5f, BossStart.z));
            r.Start(10);

            Assert.AreEqual(Vector3.right, r.Pattern.BaseDirection);
            Assert.AreEqual(Vector3.right, r.Listener.TelegraphShapes[0].Direction);
        }

        [Test]
        public void Start_TargetOnTopOfBossWithNoHorizontalForward_FallsBackToWorldForward()
        {
            Run r = new Run();
            r.Self.Forward = Vector3.up;
            r.Add(10, BossStart);
            r.Start(10);

            Assert.AreEqual(Vector3.forward, r.Pattern.BaseDirection);
        }

        /****************************************
        *      Rejection Leaves Nothing Behind
        ****************************************/

        [Test]
        public void Start_TargetLookupFails_RejectsWithNoTelegraphNoSlotNoIds()
        {
            Run r = StandardRun();
            r.Query.FailTryGet = true;
            r.Start(10);

            Assert.IsTrue(r.Pattern.IsStartRejected);
            Assert.AreEqual(0, r.Listener.TelegraphBeams.Count);
            Assert.AreEqual(0, r.Manager.ReservedCount);
            Assert.AreEqual(0, r.Pattern.BeamCount);

            r.TickRange(0.5, 8.0);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.ActivatedBeams.Count);
        }

        [Test]
        public void Start_DeadOrSummonOrEnemyTarget_Rejects()
        {
            Run dead = new Run();
            dead.Add(10, Vector3.zero).LifeState = EParticipantLifeState.Dead;
            dead.Start(10);
            Assert.IsTrue(dead.Pattern.IsStartRejected, "죽은 대상");

            Run summon = new Run();
            summon.Add(10, Vector3.zero, true);
            summon.Start(10);
            Assert.IsTrue(summon.Pattern.IsStartRejected, "소환물은 방향 기준이 아니다");

            Run unknown = new Run();
            unknown.Start(77);
            Assert.IsTrue(unknown.Pattern.IsStartRejected, "없는 대상");
        }

        [Test]
        public void Start_ArenaUnavailable_Rejects()
        {
            Run r = StandardRun();
            r.Arena.Set(ArenaCenter, 0f);
            r.Start(10);

            Assert.IsTrue(r.Pattern.IsStartRejected);
            Assert.AreEqual(0, r.Listener.TelegraphBeams.Count);
            Assert.AreEqual(0, r.Manager.ReservedCount);
        }

        [Test]
        public void Start_NotEnoughSlots_ReleasesTheOnesAlreadyReservedAndRejects()
        {
            Run r = StandardRun();
            for (int i = 0; i < 7; ++i)
            {
                Assert.IsTrue(r.Manager.TryReserve(9000 + i, 9000 + i, "other"));
            }

            r.Start(10);

            Assert.IsTrue(r.Pattern.IsStartRejected);
            Assert.AreEqual(0, r.Listener.TelegraphBeams.Count, "거절된 시전은 예고도 알리지 않는다");
            Assert.AreEqual(7, r.Manager.ReservedCount, "이 시전이 예약한 한 슬롯도 돌려줬다");
        }

        /****************************************
        *      Fixed After Telegraph Start
        ****************************************/

        [Test]
        public void Placement_StaysFixed_WhenTargetMovesDiesOrBossMovesAndArenaChanges()
        {
            Run r = StandardRun();
            FakeCombatTarget victim = r.Add(11, InRight);
            r.Start(10);
            BossBeamShape first = r.Pattern.GetBeamShape(1);

            FakeCombatTarget aimer = (FakeCombatTarget)r.Query.Targets[0];
            aimer.Position = ArenaCenter + new Vector3(-8f, 0f, 3f);
            aimer.LifeState = EParticipantLifeState.Dead;
            r.Self.Position = ArenaCenter;
            r.Self.Forward = Vector3.back;
            r.Arena.Set(Vector3.zero, 5f);

            r.TickRange(0.5, 5.0);

            Assert.AreEqual(first.LineCenter, r.Pattern.GetBeamShape(1).LineCenter);
            Assert.AreEqual(Vector3.forward, r.Pattern.GetBeamShape(1).Direction);
            Assert.AreEqual(8, r.Hits(11), "시작 때 고정한 줄 안의 대상은 끝까지 맞는다");
        }

        /****************************************
        *      Timing: Activation, Eight Hits, End
        ****************************************/

        [Test]
        public void Activation_FirstHitAtActivationAndEightHitsAndNoHitAtEnd()
        {
            Run r = StandardRun();
            r.Add(11, InRight);
            r.Start(10);

            r.TickAt(1.0);
            r.TickAt(1.49);
            Assert.AreEqual(0, r.Hits(11), "예고 중 피해 없음");
            Assert.AreEqual(0, r.Listener.ActivatedBeams.Count);

            r.TickAt(1.5);
            Assert.AreEqual(2, r.Listener.ActivatedBeams.Count);
            Assert.AreEqual(1.5, r.Listener.ActivatedTimes[0], TIME_TOL);
            Assert.AreEqual(1, r.Hits(11), "활성 순간이 첫 타격이다");

            r.TickRange(2.0, 5.0);
            Assert.AreEqual(8, r.Hits(11));
            r.TickAt(BEAM_END);
            Assert.AreEqual(8, r.Hits(11), "끝 시각에 추가 타격이 없다");
            Assert.AreEqual(2, r.Listener.Ends.Count);
            Assert.AreEqual(EBossAreaEnd.Expired, r.Listener.Ends[0].Kind);
            Assert.AreEqual(BEAM_END, r.Listener.Ends[0].Time, TIME_TOL);

            int rightBeam = r.Pattern.GetBeamId(1);
            List<Tick> ticks = r.Listener.TicksOf(rightBeam);
            Assert.AreEqual(8, ticks.Count);
            for (int i = 0; i < 8; ++i)
            {
                Assert.AreEqual(i, ticks[i].Index);
                Assert.AreEqual(1.5 + (i * 0.5), ticks[i].Time, TIME_TOL);
                Assert.AreEqual(1, ticks[i].Hits);
            }

            r.TickRange(5.5, 12.0);
            Assert.AreEqual(8, r.Hits(11), "종료 뒤 밀린 피해가 없다");
            Assert.AreEqual(0, r.Manager.ActiveCount);
            Assert.AreEqual(0, r.Manager.ReservedCount);
        }

        [Test]
        public void Fsm_RunsExecuteWithoutNextSelectionThenRecovery()
        {
            Run r = StandardRun();
            r.Start(10);

            r.TickAt(1.0);
            Assert.AreEqual(EBossAttackState.Telegraph, r.Fsm.State);
            r.TickAt(1.5);
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);
            r.TickAt(5.0);
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State, "4초 동안 다음 패턴을 고르지 않는다");
            r.TickAt(5.5);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(6.2);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(RECOVERY_END + 0.01); // 회복 0.8f의 단정밀도 오차를 넘긴다
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.IsFalse(r.Fsm.IsControlWindowOpen(5.9), "직접 제어 구간 없음");
        }

        [Test]
        public void Damage_IsUnblockableConfiguredAmountWithPerBeamPerRoundActionIds()
        {
            Run r = StandardRun();
            r.Add(11, InLeft);
            r.Add(12, InRight);
            r.Start(10);
            r.TickRange(1.5, 5.5);

            Assert.AreEqual(16, r.Resolver.Requests.Count);
            HashSet<int> actionIds = new HashSet<int>();
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                Assert.AreEqual(RAW_DAMAGE, request.RawDamage, TOLERANCE);
                Assert.IsTrue((request.Flags & EDamageFlags.Unblockable) != 0, "방어 불가");
                Assert.AreEqual(r.CastId, request.CastInstanceId, "시전 번호는 모든 빔과 회차가 함께 쓴다");
                Assert.AreEqual(1, request.AttackerId);
                Assert.AreEqual(P02_ID, request.SourceDefinitionId);
                Assert.IsTrue(actionIds.Add(request.ActionInstanceId), "빔과 회차마다 동작 번호가 다르다");
            }

            Assert.AreEqual(16, actionIds.Count);
            Assert.AreEqual(8, r.Hits(11));
            Assert.AreEqual(8, r.Hits(12));
        }

        /****************************************
        *      Targets And Geometry In Play
        ****************************************/

        [Test]
        public void Targets_OnlyInsideBeamsAreHit_GapOuterAndOutsideArenaAreSafe()
        {
            Run r = StandardRun();
            r.Add(11, InLeft);
            r.Add(12, InRight);
            r.Add(13, InGap);
            r.Add(14, Outer);
            r.Add(15, new Vector3(55f, 0f, -30f + ARENA_RADIUS + 3f));
            r.Add(16, new Vector3(55f, 0f, -30f + 17f));
            r.Start(10);
            r.TickRange(1.5, 5.5);

            Assert.AreEqual(8, r.Hits(11));
            Assert.AreEqual(8, r.Hits(12));
            Assert.AreEqual(0, r.Hits(13), "가운데 안전 구역");
            Assert.AreEqual(0, r.Hits(14), "바깥쪽 안전 구역");
            Assert.AreEqual(0, r.Hits(15), "전장 밖");
            Assert.AreEqual(8, r.Hits(16), "전장 안쪽 끝 근처의 빔 안");
        }

        [Test]
        public void Targets_StripEdgesInclusiveAndSummonsHitDeadDoNot()
        {
            Run r = StandardRun();
            r.Add(11, new Vector3(54f, 0f, -20f)); // 오른쪽 빔 안쪽 변
            r.Add(12, new Vector3(56f, 0f, -20f)); // 바깥쪽 변
            r.Add(13, new Vector3(56.02f, 0f, -20f));
            r.Add(14, InRight, true);
            r.Add(15, InRight).LifeState = EParticipantLifeState.Dead;
            r.Add(16, InRight).Faction = ECombatFaction.Enemy;
            r.Start(10);
            r.TickRange(1.5, 5.5);

            Assert.AreEqual(8, r.Hits(11));
            Assert.AreEqual(8, r.Hits(12));
            Assert.AreEqual(0, r.Hits(13));
            Assert.AreEqual(8, r.Hits(14), "소환물도 피해를 받는다");
            Assert.AreEqual(0, r.Hits(15), "죽은 대상");
            Assert.AreEqual(0, r.Hits(16), "적 진영");
        }

        [Test]
        public void Targets_PositionsAreRequeriedEveryRound()
        {
            Run r = StandardRun();
            FakeCombatTarget mover = r.Add(11, InRight);
            r.Start(10);

            r.TickAt(1.5);
            r.TickAt(2.0);
            Assert.AreEqual(2, r.Hits(11));

            mover.Position = InGap; // 안전 구역으로 이동
            r.TickAt(2.5);
            r.TickAt(3.0);
            Assert.AreEqual(2, r.Hits(11));

            mover.Position = InRight;
            r.TickAt(3.5);
            Assert.AreEqual(3, r.Hits(11), "다시 들어오면 다시 맞는다");
        }

        [Test]
        public void Targets_ARotatedBeamStillUsesTheStripAroundTheArenaCenter()
        {
            Run r = new Run();
            r.Add(10, BossStart + new Vector3(10f, 0f, 0f)); // 방향 +x
            r.Add(11, ArenaCenter + new Vector3(8f, 0f, 5f));   // 방향 +x의 왼쪽(+z) 빔
            r.Add(12, ArenaCenter + new Vector3(8f, 0f, 0f));   // 가운데
            r.Start(10);
            r.TickRange(1.5, 5.5);

            Assert.AreEqual(8, r.Hits(11));
            Assert.AreEqual(0, r.Hits(12));
        }

        /****************************************
        *      Delay: Latest Round Only
        ****************************************/

        [Test]
        public void BigTick_HitsOnlyTheLatestDueRoundPerBeamAndKeepsTheOriginalSchedule()
        {
            Run r = StandardRun();
            r.Add(11, InLeft);
            r.Add(12, InRight);
            r.Start(10);

            r.TickAt(1.5);
            r.TickAt(3.7); // 회차 4가 도래, 1~3은 건너뛴다
            Assert.AreEqual(2, r.Hits(11));
            Assert.AreEqual(2, r.Hits(12), "빔마다 각자 한 회차만");

            int leftBeam = r.Pattern.GetBeamId(0);
            List<Tick> ticks = r.Listener.TicksOf(leftBeam);
            Assert.AreEqual(2, ticks.Count);
            Assert.AreEqual(4, ticks[1].Index);
            Assert.AreEqual(3.5, ticks[1].Time, TIME_TOL, "회차 시각은 원래 일정이다");

            r.TickAt(3.9);
            Assert.AreEqual(2, r.Hits(11), "이미 처리한 회차를 다시 치지 않는다");
            r.TickAt(4.0);
            Assert.AreEqual(3, r.Hits(11), "다음 회차는 원래 일정 기준이다");
        }

        [Test]
        public void BigTick_ActivationTickDelayed_SkipsEarlierRounds()
        {
            Run r = StandardRun();
            r.Add(11, InRight);
            r.Start(10);

            r.TickAt(1.0);
            r.TickAt(2.2); // 활성은 1.5, 회차 0은 건너뛰고 회차 1만 판정한다

            Assert.AreEqual(1, r.Hits(11));
            Assert.AreEqual(1, r.Listener.TicksOf(r.Pattern.GetBeamId(1))[0].Index);
            Assert.AreEqual(1.5, r.Listener.ActivatedTimes[0], TIME_TOL, "활성 시각은 틱이 아니라 단계 경계다");
        }

        [Test]
        public void BigTick_AcrossTheEnd_EndsWithoutPendingRounds()
        {
            Run r = StandardRun();
            r.Add(11, InRight);
            r.Start(10);

            r.TickAt(1.5);
            r.TickAt(9.0); // 종료 시각을 크게 지난 틱

            Assert.AreEqual(1, r.Hits(11), "종료 뒤에는 밀린 회차를 처리하지 않는다");
            Assert.AreEqual(2, r.Listener.Ends.Count);
            Assert.AreEqual(EBossAreaEnd.Expired, r.Listener.Ends[0].Kind);
            r.TickRange(9.5, 12.0);
            Assert.AreEqual(1, r.Hits(11));
        }

        /****************************************
        *      Independence From Areas
        ****************************************/

        [Test]
        public void Beams_DoNotChangeAreaCountsAndAreaDamageIsIndependent()
        {
            Run r = StandardRun();
            r.Add(11, InRight);
            BossAreaManager areas = new BossAreaManager(r.Query);
            BossEffectComposite composite = new BossEffectComposite(areas, r.Manager);

            int legacyCast = r.Gate.IssueInstanceId();
            Assert.IsTrue(areas.TryReserve(legacyCast, "wolbaek_p05", 3));
            BossAreaSpec area = new BossAreaSpec(legacyCast, 1, "wolbaek_p05", InRight, 3f, 6f, 1f, RAW_DAMAGE,
                EDamageFlags.Unblockable, 0.0, r.Gate);
            Assert.IsTrue(areas.TryActivate(in area));

            r.Start(10);
            Assert.AreEqual(1, composite.TotalAreaCount, "빔은 보스 전체 장판 상한에 들어가지 않는다");
            Assert.AreEqual(2, r.Manager.ReservedCount);
            Assert.AreEqual(1, composite.ActiveAreaCount("wolbaek_p05"), "빔은 기존 장판의 개수에 들어가지 않는다");

            for (double t = 0.0; t <= 8.0; t += 0.5)
            {
                r.Fsm.Tick(t);
                composite.Tick(t);
            }

            // 빔 8회와 장판 6회가 서로 대신하지 않고 각자 센다
            Assert.AreEqual(8 + 6, r.Hits(11));
            Assert.AreEqual(0, composite.TotalAreaCount);
        }

        /****************************************
        *      Cancel And Cleanup
        ****************************************/

        [Test]
        public void Cancel_DuringTelegraph_ReleasesSlotsNotifiesEachBeamAndNeverActivates()
        {
            Run r = StandardRun();
            r.Add(11, InRight);
            r.Start(10);
            r.TickAt(1.0);

            r.CancelAll(EBossInterruptReason.Death);
            r.CancelAll(EBossInterruptReason.Death);
            r.TickRange(1.5, 8.0);

            Assert.AreEqual(2, r.Listener.CancelledBeams.Count);
            Assert.AreEqual(0, r.Listener.ActivatedBeams.Count);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Manager.ReservedCount);
            Assert.AreEqual(0, r.Manager.ActiveCount);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        public void Cancel_WhileActive_RemovesBothBeamsAndNoFurtherDamage(EBossInterruptReason reason)
        {
            Run r = StandardRun();
            r.Add(11, InLeft);
            r.Add(12, InRight);
            r.Start(10);
            r.TickRange(1.5, 2.5);
            int before = r.Resolver.Requests.Count;
            Assert.AreEqual(6, before);

            r.CancelAll(reason);
            r.TickRange(3.0, 9.0);

            Assert.AreEqual(before, r.Resolver.Requests.Count, "이미 적용된 피해는 그대로이고 추가 피해는 없다");
            Assert.AreEqual(2, r.Listener.Ends.Count);
            Assert.AreEqual(EBossAreaEnd.Cancelled, r.Listener.Ends[0].Kind);
            Assert.AreEqual(EBossAreaEnd.Cancelled, r.Listener.Ends[1].Kind);
            Assert.AreEqual(0, r.Manager.ActiveCount);
            Assert.AreEqual(0, r.Listener.CancelledBeams.Count, "활성한 뒤의 소멸은 관리자가 알린다");
        }

        [Test]
        public void Cancel_ByCastId_OnlyRemovesThatCastsBeams()
        {
            Run r = StandardRun();
            r.Add(11, InRight);
            r.Start(10);
            r.TickAt(1.5);

            r.Manager.Cancel(EBossInterruptReason.PhaseTransition, r.CastId + 1000);
            Assert.AreEqual(2, r.Manager.ActiveCount, "다른 시전 번호는 건드리지 않는다");

            r.Manager.Cancel(EBossInterruptReason.PhaseTransition, r.CastId);
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [Test]
        public void Cancel_DirectControl_DoesNotRemoveActiveBeams()
        {
            Run r = StandardRun();
            r.Start(10);
            r.TickAt(1.5);

            r.Manager.Cancel(EBossInterruptReason.DirectControl, CombatIds.NONE);
            Assert.AreEqual(2, r.Manager.ActiveCount);
        }

        [Test]
        public void ClosedGate_StopsDamageButBeamsStillEndOnTime()
        {
            Run r = StandardRun();
            r.Add(11, InRight);
            r.Start(10);
            r.TickAt(1.5);
            Assert.AreEqual(1, r.Hits(11));

            r.Gate.CloseForGood();
            r.TickRange(2.0, 6.0);

            Assert.AreEqual(1, r.Hits(11));
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        /****************************************
        *      Manager Contract
        ****************************************/

        [Test]
        public void Manager_RejectsInvalidReserveAndActivate()
        {
            BossBeamManager manager = new BossBeamManager(new BeamQuery());
            BossDamageGate gate = new BossDamageGate(new FakeDamageResolver());
            gate.Open();
            BossBeamShape shape = BossBeamGeometry.Place(ArenaCenter, ARENA_RADIUS, Vector3.forward, 0, 2, 5f, 2f);

            Assert.IsFalse(manager.TryReserve(CombatIds.NONE, 1, "x"));
            Assert.IsFalse(manager.TryReserve(1, CombatIds.NONE, "x"));
            Assert.IsFalse(manager.TryReserve(1, 2, null));
            Assert.IsTrue(manager.TryReserve(1, 2, "x"));
            Assert.IsFalse(manager.TryReserve(1, 2, "x"), "같은 빔 번호의 예약은 두 번 받지 않는다");

            BossBeamSpec noGate = new BossBeamSpec(1, 2, 1, "x", in shape, 4f, 0.5f, 1f, EDamageFlags.None, 0.0, null);
            Assert.IsFalse(manager.TryActivate(in noGate));
            Assert.AreEqual(0, manager.ReservedCount, "잘못된 활성은 슬롯을 돌려준다");

            BossBeamSpec missing = new BossBeamSpec(1, 9, 1, "x", in shape, 4f, 0.5f, 1f, EDamageFlags.None, 0.0, gate);
            Assert.IsFalse(manager.TryActivate(in missing), "예약이 없으면 활성하지 않는다");
        }

        [Test]
        public void Manager_SlotsReturnExactlyOnce()
        {
            Run r = StandardRun();
            r.Start(10);
            Assert.AreEqual(2, r.Manager.ReservedCount);

            r.TickAt(1.5);
            Assert.AreEqual(0, r.Manager.ReservedCount);
            Assert.AreEqual(2, r.Manager.ActiveCount);
            Assert.AreEqual(2, r.Manager.ActiveAreaCount(P02_ID));

            r.TickAt(BEAM_END);
            Assert.AreEqual(0, r.Manager.ActiveCount);
            Assert.AreEqual(0, r.Manager.ActiveAreaCount(P02_ID));
            r.Manager.ReleaseReservation(r.Pattern.GetBeamId(0));
            Assert.AreEqual(0, r.Manager.ReservedCount);
        }

        /****************************************
        *      Brain: Large Group, 25 Percent, Rejection
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = ArenaCenter };
            public BeamQuery          Query    = new BeamQuery();
            public FakeDamageResolver Damage   = new FakeDamageResolver();
            public FakeBossVitals     Vitals   = new FakeBossVitals();
            public FakeBossRandom     Random   = new FakeBossRandom();
            public RecordingListener  Listener = new RecordingListener();
            public BossBeamManager    Manager;
            public BossFixedArena     Arena    = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossBrain          Brain;
            public List<string>       Used     = new List<string>();

            public BrainRig()
            {
                Query.Targets.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player,
                    Position = ArenaCenter + new Vector3(0f, 0f, 3f) });
                Query.Targets.Add(new FakeCombatTarget { CombatId = 11, Faction = ECombatFaction.Player,
                    Position = ArenaCenter + new Vector3(5f, 0f, 3f) });
                Manager = new BossBeamManager(Query, Listener);

                WolbaekPatternFactory inner = new WolbaekPatternFactory(HREF, beams: Manager, arena: Arena, beamListener: Listener);
                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog()),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = new ExcludingPatternFactory(inner, BossPatternCatalogSO.WOLBAEK_P2_P03_ID, BossPatternCatalogSO.WOLBAEK_P2_P06_ID),
                    Effects  = Manager,
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
        public void Brain_SelectsP02AsLargeAndRunsItWithoutPickingAnotherUntilRecoveryEnds()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            string firstId = null;
            for (double t = 0.5; t < 6.5; t += 0.25)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                    firstId = r.Brain.ActivePatternId;
                }

                if ((started >= 0.0) && (t < (started + RECOVERY_END - 0.01)))
                {
                    Assert.AreEqual(firstId, r.Brain.ActivePatternId, "실행 4초 동안 다음 패턴을 고르지 않는다 t=" + t);
                }
            }

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(P02_ID, firstId);
            Assert.AreEqual(EPatternGroup.Large, LoadDefinition().Group);
            Assert.AreEqual(2, r.Listener.TelegraphBeams.Count);
            Assert.AreEqual(2, r.Listener.ActivatedBeams.Count);
            Assert.AreEqual(8, r.Listener.TicksOf(r.Listener.TelegraphBeams[0]).Count);
        }

        [Test]
        public void Brain_HealthCrossing25DuringTheCast_KeepsValuesAndHitCount()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            for (double t = 0.5; t < 7.0; t += 0.25)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                }

                if ((started >= 0.0) && (t >= (started + 2.0)))
                {
                    r.Vitals.HealthRate = 0.2f;
                }
            }

            Assert.GreaterOrEqual(started, 0.0);
            Assert.AreEqual(8, r.Listener.TicksOf(r.Listener.TelegraphBeams[0]).Count, "체력 구간이 바뀌어도 진행 중인 빔은 그대로다");
            Assert.AreEqual(started + TELEGRAPH_END, r.Listener.ActivatedTimes[0], TIME_TOL);
            Assert.AreEqual(2, r.Brain.Phase);
        }

        [Test]
        public void Brain_RejectedStart_LeavesNoRecordAndRetriesWithoutErrors()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            r.Arena.Set(ArenaCenter, 0f);
            r.Run(0.5, 2.0);
            Assert.AreEqual(0, r.Listener.TelegraphBeams.Count, "전장 정보가 없어 시작을 거절하고 예고도 없다");
            Assert.AreEqual(0, r.Used.Count);
            LogAssert.NoUnexpectedReceived();

            r.Arena.Set(ArenaCenter, ARENA_RADIUS);
            r.Run(2.25, 6.0);
            Assert.IsTrue(r.Used.Contains(P02_ID), "시작 실패는 기록하지 않아 직전 사용 x0이 걸리지 않는다");
        }

        [Test]
        public void Brain_CombatEndMidBeam_CleansEverythingAndStopsDamage()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            for (double t = 0.5; t < 4.0; t += 0.25)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                }
            }

            Assert.AreEqual(2, r.Manager.ActiveCount);
            int before = r.Damage.Requests.Count;
            r.Brain.RequestCombatEnd();
            for (double t = 4.0; t < 9.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(0, r.Manager.ActiveCount);
            Assert.AreEqual(before, r.Damage.Requests.Count);
        }
    }
}
