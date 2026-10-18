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
    // 월백 2페이즈 P01 별형 회전 빔을 에셋의 실제 정의와 실제 팩토리, 실제 별형 관리자로 확인한다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 전장 중심과 반지름은 고정 전장 입력으로 넣는다
    public class BossPhaseTwoStarTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string P01_ID       = BossPatternCatalogSO.WOLBAEK_P2_P01_ID;
        private const float  HREF         = 1000f;
        private const float  RAW_DAMAGE   = 0.03f * HREF;
        private const float  TOLERANCE    = 0.001f;
        private const double TIME_TOL     = 0.0001;
        private const double ACTIVATE     = 4.0;
        private const double END          = 10.0;
        private const double RECOVERY_END = 12.0;
        private const float  ARENA_RADIUS = 18f;
        private const float  DURATION     = 6f;
        private const float  APOTHEM      = 4.326f;

        private static readonly Vector3 ArenaCenter = new Vector3(50f, 0f, -30f);
        private static readonly Vector3 BossStart   = new Vector3(100f, 0f, 100f);

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class StarQuery : ICombatTargetQuery
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

        // 지정한 값을 차례로 돌려주는 난수, 기본은 0(왼쪽)이다. 1이면 오른쪽
        private sealed class SequenceRandom : IBossRandom
        {
            private readonly Queue<int> _values = new Queue<int>();

            public int Calls;

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
                ++Calls;
                return (_values.Count > 0) ? (_values.Dequeue() % maxExclusive) : 0;
            }
        }

        private struct Tick
        {
            public int    StarId;
            public int    Index;
            public double Time;
        }

        private struct Ended
        {
            public int          StarId;
            public EBossAreaEnd Kind;
            public double       Time;
        }

        private sealed class RecordingListener : IBossStarTelegraphListener, IBossStarListener
        {
            public List<int>           TelegraphCasts  = new List<int>();
            public List<int>           TelegraphStars  = new List<int>();
            public List<BossStarShape> TelegraphShapes = new List<BossStarShape>();
            public List<float>         TelegraphSeconds = new List<float>();
            public List<int>           CancelledStars  = new List<int>();
            public List<int>           ActivatedStars  = new List<int>();
            public List<double>        ActivatedTimes  = new List<double>();
            public List<Tick>          Ticks           = new List<Tick>();
            public List<Ended>         Ends            = new List<Ended>();

            public void OnTelegraphStarted(int castInstanceId, int starId, in BossStarShape shape, float telegraphSeconds,
                float durationSeconds, float tickInterval)
            {
                TelegraphCasts.Add(castInstanceId);
                TelegraphStars.Add(starId);
                TelegraphShapes.Add(shape);
                TelegraphSeconds.Add(telegraphSeconds);
            }

            public void OnCancelled(int castInstanceId, int starId, EBossInterruptReason reason)
            {
                CancelledStars.Add(starId);
            }

            public void OnStarActivated(int castInstanceId, int starId, float durationSeconds, float tickInterval, double activateServerTime)
            {
                ActivatedStars.Add(starId);
                ActivatedTimes.Add(activateServerTime);
            }

            public void OnStarTick(int castInstanceId, int starId, int hitIndex, double roundServerTime, int hitCount)
            {
                Ticks.Add(new Tick { StarId = starId, Index = hitIndex, Time = roundServerTime });
            }

            public void OnStarEnded(int castInstanceId, int starId, EBossAreaEnd end, double endServerTime)
            {
                Ends.Add(new Ended { StarId = starId, Kind = end, Time = endServerTime });
            }
        }

        // 공격 FSM에 실제 팩토리가 만든 패턴을 올리고 별형 관리자를 BossCombat처럼 공격 진행 뒤에 부른다
        private sealed class Run
        {
            public FakeCombatTarget    Self      = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = BossStart };
            public StarQuery           Query     = new StarQuery();
            public FakeDamageResolver  Resolver  = new FakeDamageResolver();
            public RecordingListener   Listener  = new RecordingListener();
            public SequenceRandom      Random    = new SequenceRandom();
            public BossFixedArena      Arena     = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossDamageGate      Gate;
            public BossStarManager     Manager;
            public BossAttackFsm       Fsm       = new BossAttackFsm();
            public PatternDefinition   Definition;
            public BossStarPattern     Pattern;
            public int                 CastId;

            public Run()
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = LoadDefinition();
                Manager    = new BossStarManager(Query, Listener);
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

            public void Start(int selectedTargetId, double time = 0.0, bool useRandom = true)
            {
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, stars: Manager, arena: Arena, starListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                Pattern = (BossStarPattern)pattern;
                BossPatternContext context = new BossPatternContext(Definition, CastId, selectedTargetId, Self, Query, Gate,
                    useRandom ? Random : null);
                Assert.IsTrue(Fsm.Start(Definition, pattern, in context, time));
            }

            public void TickAt(double time)
            {
                Fsm.Tick(time);
                Manager.Tick(time);
            }

            public void TickRange(double from, double to, double step = 0.25)
            {
                for (double t = from; t <= to + 0.000001; t += step)
                {
                    TickAt(t);
                }
            }

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

            // 고정한 별 위의 한 점, 기준 방향에서 오른쪽으로 degrees도 반지름 radius
            public Vector3 Polar(float degrees, float radius)
            {
                BossStarShape shape = Pattern.Shape;
                return shape.ArenaCenter + ((Quaternion.AngleAxis(degrees, Vector3.up) * shape.BaseDirection) * radius);
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
            Assert.IsTrue(LoadCatalog().TryGetPattern(P01_ID, out PatternDefinition definition));
            return definition;
        }

        private static Run StandardRun(bool right = true)
        {
            Run r = new Run();
            r.AddAimer(10);
            r.Random.Enqueue(right ? 1 : 0);
            return r;
        }

        /****************************************
        *      Asset, Factory, Readiness Agree
        ****************************************/

        [Test]
        public void Asset_P01_HasStartingValuesAndKeepsSelectionData()
        {
            PatternDefinition p = LoadDefinition();

            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EPatternGroup.Large, p.Group);
            Assert.AreEqual(4f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(6f, p.ActiveSeconds, TOLERANCE);
            Assert.AreEqual(2f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Line, p.Shape.Shape);
            Assert.AreEqual(1.5f, p.Shape.Width, TOLERANCE);
            Assert.AreEqual(5, p.StarVertexCount);
            Assert.AreEqual(14f, p.StarRingRadius, TOLERANCE);
            Assert.AreEqual(2, p.StarConnectStep);
            Assert.AreEqual(20f, p.StarRotationDegrees, TOLERANCE);
            Assert.AreEqual(0.25f, p.BeamTickInterval, TOLERANCE);
            Assert.AreEqual(0.03f, p.DamageRef, TOLERANCE);
            Assert.IsFalse(p.Guardable);
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(0f, p.TrackingSeconds);
            Assert.AreEqual(10f, p.BaseWeight, TOLERANCE);
            Assert.AreEqual(12f, p.EnhancedBaseWeight, TOLERANCE);
            Assert.AreEqual(24, BossAreaManager.ComputeHitCount(p.ActiveSeconds, p.BeamTickInterval), "활성 순간부터 0.25초 간격 24회");
        }

        [Test]
        public void Asset_P01_FactoryNeedsStarHostAndArena_AndReadinessAgrees()
        {
            PatternDefinition p = LoadDefinition();
            BossStarManager manager = new BossStarManager(new StarQuery());
            BossFixedArena arena = new BossFixedArena(ArenaCenter, ARENA_RADIUS);

            Assert.IsFalse(new WolbaekPatternFactory(HREF).CanCreate(p));
            Assert.IsNull(new WolbaekPatternFactory(HREF).Create(p));
            Assert.IsFalse(new WolbaekPatternFactory(HREF, stars: manager).CanCreate(p), "전장 입력이 없으면 만들 수 없다");
            Assert.IsFalse(new WolbaekPatternFactory(HREF, arena: arena).CanCreate(p), "별형 창구가 없으면 만들 수 없다");
            Assert.IsNull(new WolbaekPatternFactory(HREF, arena: arena).Create(p));

            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, stars: manager, arena: arena);
            Assert.IsTrue(factory.CanCreate(p));
            Assert.IsInstanceOf<BossStarPattern>(factory.Create(p));
            Assert.IsTrue(LoadCatalog().IsPatternReady(WOLBAEK_ID, p));
        }

        [Test]
        public void Asset_P02AndOtherPatternsAreUntouched()
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern(BossPatternCatalogSO.WOLBAEK_P2_P02_ID, out PatternDefinition p02));
            Assert.AreEqual(2, p02.RawBeamCount);
            Assert.AreEqual(0, p02.StarVertexCount, "P02는 별형이 아니다");
            Assert.AreEqual(0.5f, p02.BeamTickInterval, TOLERANCE);
            Assert.IsTrue(LoadCatalog().TryGetPattern(BossPatternCatalogSO.WOLBAEK_P2_P03_ID, out PatternDefinition p03));
            Assert.AreEqual(12f, p03.BaseWeight, TOLERANCE);
            Assert.IsTrue(LoadCatalog().IsTuned(WOLBAEK_ID), "1페이즈 정의는 그대로 유효하다");
        }

        /****************************************
        *      Placement At Telegraph Start
        ****************************************/

        [Test]
        public void Start_PlacesTheStarOnTheArenaCenterWithVertexZeroTowardTheTarget()
        {
            Run r = StandardRun();
            r.Start(10);

            Assert.IsFalse(r.Pattern.IsStartRejected);
            Assert.AreEqual(1, r.Listener.TelegraphStars.Count);
            Assert.AreEqual(r.CastId, r.Listener.TelegraphCasts[0]);
            Assert.AreEqual(4f, r.Listener.TelegraphSeconds[0], TOLERANCE);

            BossStarShape shape = r.Listener.TelegraphShapes[0];
            Assert.AreEqual(ArenaCenter, shape.ArenaCenter, "보스 위치가 아니라 전장 중심");
            Assert.AreEqual(ARENA_RADIUS, shape.ArenaRadius, TOLERANCE);
            Assert.AreEqual(0f, (Vector3.forward - shape.BaseDirection).magnitude, TOLERANCE);
            Assert.AreEqual(14f, shape.RingRadius, TOLERANCE);
            Assert.AreEqual(5, shape.VertexCount);
            Assert.AreEqual(2, shape.ConnectStep);
            Assert.AreEqual(1.5f, shape.BeamWidth, TOLERANCE);
            Assert.AreEqual(1, r.Manager.ReservedCount);
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [Test]
        public void Start_RotationDirectionComesFromTheBrainRandom_RightOrLeft()
        {
            Run right = StandardRun(true);
            right.Start(10);
            Assert.IsTrue(right.Listener.TelegraphShapes[0].RotatesRight);
            Assert.AreEqual(20f, right.Listener.TelegraphShapes[0].SignedDegreesPerSecond, TOLERANCE);
            Assert.AreEqual(1, right.Random.Calls, "좌우 추첨은 한 번이다");

            Run left = StandardRun(false);
            left.Start(10);
            Assert.IsFalse(left.Listener.TelegraphShapes[0].RotatesRight);
            Assert.AreEqual(-20f, left.Listener.TelegraphShapes[0].SignedDegreesPerSecond, TOLERANCE);
        }

        [Test]
        public void Start_BossPositionDoesNotMatterAndSameHorizontalSpotUsesTheBossForward()
        {
            Run far = StandardRun();
            far.Start(10);

            Run near = new Run();
            near.Self.Position = ArenaCenter + new Vector3(0f, 0f, -17f);
            near.Add(10, near.Self.Position + (Vector3.forward * 5f));
            near.Start(10);
            Assert.AreEqual(far.Listener.TelegraphShapes[0].ArenaCenter, near.Listener.TelegraphShapes[0].ArenaCenter);

            Run onTop = new Run();
            onTop.Self.Forward = Vector3.right;
            onTop.Add(10, new Vector3(BossStart.x, 5f, BossStart.z));
            onTop.Start(10);
            Assert.AreEqual(0f, (Vector3.right - onTop.Pattern.Shape.BaseDirection).magnitude, TOLERANCE);
        }

        [Test]
        public void Start_RejectsWithNothingLeft_WhenTargetArenaRandomOrSlotIsMissing()
        {
            Run noTarget = StandardRun();
            noTarget.Query.FailTryGet = true;
            noTarget.Start(10);
            Assert.IsTrue(noTarget.Pattern.IsStartRejected);
            Assert.AreEqual(0, noTarget.Listener.TelegraphStars.Count);
            Assert.AreEqual(0, noTarget.Manager.ReservedCount);

            Run noArena = StandardRun();
            noArena.Arena.Set(ArenaCenter, 0f);
            noArena.Start(10);
            Assert.IsTrue(noArena.Pattern.IsStartRejected);
            Assert.AreEqual(0, noArena.Manager.ReservedCount);

            Run smallArena = StandardRun();
            smallArena.Arena.Set(ArenaCenter, 10f);
            smallArena.Start(10);
            Assert.IsTrue(smallArena.Pattern.IsStartRejected, "배치 반경 14m가 전장 반지름보다 크면 거절");

            Run noRandom = StandardRun();
            noRandom.Start(10, 0.0, false);
            Assert.IsTrue(noRandom.Pattern.IsStartRejected, "난수가 없으면 좌우를 임의로 정하지 않는다");

            Run noSlot = StandardRun();
            for (int i = 0; i < 4; ++i)
            {
                Assert.IsTrue(noSlot.Manager.TryReserve(9000 + i, 9000 + i, "other"));
            }

            noSlot.Start(10);
            Assert.IsTrue(noSlot.Pattern.IsStartRejected);
            Assert.AreEqual(4, noSlot.Manager.ReservedCount);

            Run dead = new Run();
            dead.Add(10, Vector3.zero).LifeState = EParticipantLifeState.Dead;
            dead.Start(10);
            Assert.IsTrue(dead.Pattern.IsStartRejected);
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
            Assert.AreEqual(first.SignedDegreesPerSecond, r.Pattern.Shape.SignedDegreesPerSecond);
            Assert.AreEqual(0, r.Resolver.Requests.Count, "예고 중에는 피해와 회전이 없다");
        }

        /****************************************
        *      Timing: Activation, 24 Hits, End
        ****************************************/

        [Test]
        public void Activation_FirstHitAtActivation_TwentyFourHitsAndNoHitAtTheEnd()
        {
            Run r = StandardRun();
            r.Start(10);
            // 별 중심선 가운데에서 시작하는 점은 처음에 맞는다
            r.Add(11, r.Polar(72f, APOTHEM));

            r.TickAt(ACTIVATE / 2.0);
            r.TickAt(ACTIVATE - 0.01);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Listener.ActivatedStars.Count);

            r.TickAt(ACTIVATE);
            Assert.AreEqual(1, r.Listener.ActivatedStars.Count);
            Assert.AreEqual(ACTIVATE, r.Listener.ActivatedTimes[0], TIME_TOL);
            Assert.AreEqual(1, r.Hits(11), "활성 순간이 첫 타격이다");

            r.TickRange(ACTIVATE + 0.25, ACTIVATE + 5.75);
            r.TickAt(END);

            Assert.AreEqual(24, r.Listener.Ticks.Count);
            for (int i = 0; i < 24; ++i)
            {
                Assert.AreEqual(i, r.Listener.Ticks[i].Index);
                Assert.AreEqual(ACTIVATE + (i * 0.25), r.Listener.Ticks[i].Time, TIME_TOL);
            }

            Assert.AreEqual(1, r.Listener.Ends.Count);
            Assert.AreEqual(EBossAreaEnd.Expired, r.Listener.Ends[0].Kind);
            Assert.AreEqual(END, r.Listener.Ends[0].Time, TIME_TOL);
            int hitsAtEnd = r.Resolver.Requests.Count;

            r.TickRange(END + 0.25, RECOVERY_END);
            Assert.AreEqual(hitsAtEnd, r.Resolver.Requests.Count, "종료 뒤 밀린 피해가 없다");
            Assert.AreEqual(0, r.Manager.ActiveCount);
            Assert.AreEqual(0, r.Manager.ReservedCount);
        }

        [Test]
        public void Fsm_RunsSixSecondsOfRotationWithoutNextSelectionThenRecovery()
        {
            Run r = StandardRun();
            r.Start(10);

            r.TickAt(ACTIVATE / 2.0);
            Assert.AreEqual(EBossAttackState.Telegraph, r.Fsm.State);
            r.TickAt(ACTIVATE);
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);
            r.TickAt(ACTIVATE + 5.5);
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State, "6초 동안 다음 패턴을 고르지 않는다");
            r.TickAt(END);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(RECOVERY_END + 0.01);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.IsFalse(r.Fsm.IsControlWindowOpen(8.5), "직접 제어 구간 없음");
        }

        [Test]
        public void Damage_IsUnblockableConfiguredAmountWithOneActionIdPerRound()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, r.Polar(72f, APOTHEM));
            r.Add(12, r.Polar(72f + 72f, APOTHEM)); // 다른 빔 위, 같은 회차에 둘이 함께 맞는다
            r.TickRange(ACTIVATE, ACTIVATE + 1.0);

            Assert.AreEqual(10, r.Resolver.Requests.Count, "5회차 x 둘, 대상마다 한 번");
            HashSet<int> roundIds = new HashSet<int>();
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                Assert.AreEqual(RAW_DAMAGE, request.RawDamage, TOLERANCE);
                Assert.IsTrue((request.Flags & EDamageFlags.Unblockable) != 0, "방어 불가");
                Assert.AreEqual(r.CastId, request.CastInstanceId, "시전 번호 공유");
                Assert.AreEqual(1, request.AttackerId);
                Assert.AreEqual(P01_ID, request.SourceDefinitionId);
                roundIds.Add(request.ActionInstanceId);
            }

            Assert.AreEqual(5, roundIds.Count, "회차마다 동작 번호가 하나, 같은 회차의 두 대상은 같은 번호");
        }

        /****************************************
        *      Geometry In Play
        ****************************************/

        [Test]
        public void Targets_CenterIsAlwaysSafe_AndSummonsHitDeadAndEnemiesDoNot()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, ArenaCenter);
            r.Add(12, r.Polar(72f, APOTHEM), true);
            r.Add(13, r.Polar(72f, APOTHEM)).LifeState = EParticipantLifeState.Dead;
            r.Add(14, r.Polar(72f, APOTHEM)).Faction = ECombatFaction.Enemy;
            r.TickRange(ACTIVATE, ACTIVATE + 5.75);

            Assert.AreEqual(0, r.Hits(11), "정중앙 안전 구역");
            Assert.GreaterOrEqual(r.Hits(12), 1, "소환물도 피해를 받는다");
            Assert.AreEqual(0, r.Hits(13));
            Assert.AreEqual(0, r.Hits(14));
        }

        [Test]
        public void Targets_HitsMatchTheGeometryAtEachRoundAngle_AndRightLeftDiffer()
        {
            Run right = StandardRun(true);
            right.Start(10);
            Run left = StandardRun(false);
            left.Start(10);
            BossStarShape rightShape = right.Pattern.Shape;
            BossStarShape leftShape  = left.Pattern.Shape;

            // 방향에 따라 맞는 횟수가 다른 점을 찾는다(별은 72도마다 같은 모양이라 모든 점이 다르지는 않다)
            Vector3 point = Vector3.zero;
            int expectedRight = 0;
            int expectedLeft = 0;
            bool found = false;
            for (int degrees = 0; (degrees < 360) && (!found); ++degrees)
            {
                for (float radius = 6f; radius <= 13.5f; radius += 0.25f)
                {
                    Vector3 candidate = right.Polar(degrees, radius);
                    int rightHits = CountExpected(in rightShape, candidate);
                    int leftHits  = CountExpected(in leftShape, candidate);
                    if (System.Math.Abs(rightHits - leftHits) >= 2)
                    {
                        point = candidate;
                        expectedRight = rightHits;
                        expectedLeft = leftHits;
                        found = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(found);
            right.Add(11, point);
            right.TickRange(ACTIVATE, ACTIVATE + 5.75);
            left.Add(11, point);
            left.TickRange(ACTIVATE, ACTIVATE + 5.75);

            Assert.AreEqual(expectedRight, right.Hits(11), "회차 시각의 별 각도에서 판정한다(오른쪽)");
            Assert.AreEqual(expectedLeft, left.Hits(11), "회차 시각의 별 각도에서 판정한다(왼쪽)");
            Assert.AreNotEqual(right.Hits(11), left.Hits(11));
        }

        private static int CountExpected(in BossStarShape shape, Vector3 point)
        {
            int hits = 0;
            for (int i = 0; i < 24; ++i)
            {
                float rotation = BossStarGeometry.RotationAt(in shape, i * 0.25, DURATION);
                hits += BossStarGeometry.Contains(in shape, point, rotation) ? 1 : 0;
            }

            return hits;
        }

        [Test]
        public void Targets_CrossingOfTwoBeamsHitsOncePerRound()
        {
            Run r = StandardRun();
            r.Start(10);
            Vector3 crossing = r.Polar(36f, APOTHEM / Mathf.Cos(36f * Mathf.Deg2Rad));
            r.Add(11, crossing);

            r.TickAt(ACTIVATE);
            Assert.AreEqual(1, r.Hits(11), "빔 둘이 겹쳐도 한 회차에 한 번");
        }

        [Test]
        public void Targets_PositionsAreRequeriedEveryRound()
        {
            Run r = StandardRun();
            r.Start(10);
            FakeCombatTarget mover = r.Add(11, r.Polar(72f, APOTHEM));

            r.TickAt(ACTIVATE);
            Assert.AreEqual(1, r.Hits(11));

            mover.Position = ArenaCenter;
            r.TickRange(ACTIVATE + 0.25, ACTIVATE + 1.0);
            Assert.AreEqual(1, r.Hits(11), "안전 구역으로 옮기면 맞지 않는다");

            mover.Position = r.Polar(72f + (BossStarGeometry.RotationAt(r.Pattern.Shape, 1.25, DURATION)), APOTHEM);
            r.TickAt(ACTIVATE + 1.25);
            Assert.AreEqual(2, r.Hits(11), "다시 빔 위로 오면 다시 맞는다");
        }

        /****************************************
        *      Delay: Latest Round Only, Server Time Angle
        ****************************************/

        [Test]
        public void BigTick_HitsOnlyTheLatestDueRoundAndKeepsTheOriginalSchedule()
        {
            Run r = StandardRun();
            r.Start(10);
            // 어느 각도에서도 자주 맞도록 별의 한가운데 빔 근처 한 점
            r.Add(11, r.Polar(72f, APOTHEM));

            r.TickAt(ACTIVATE);
            r.TickAt(ACTIVATE + 1.55); // 회차 6이 도래, 1~5는 건너뛴다
            Assert.AreEqual(2, r.Listener.Ticks.Count);
            Assert.AreEqual(6, r.Listener.Ticks[1].Index);
            Assert.AreEqual(ACTIVATE + 1.5, r.Listener.Ticks[1].Time, TIME_TOL, "회차 시각은 원래 일정이다");

            r.TickAt(ACTIVATE + 1.7);
            Assert.AreEqual(2, r.Listener.Ticks.Count, "이미 처리한 회차를 다시 치지 않는다");
            r.TickAt(ACTIVATE + 1.75);
            Assert.AreEqual(3, r.Listener.Ticks.Count);
            Assert.AreEqual(7, r.Listener.Ticks[2].Index, "다음 회차는 원래 일정 기준");
        }

        [Test]
        public void BigTick_UsesTheRotationAtTheTickServerTime_NotAtTheRoundTime()
        {
            Run r = StandardRun();
            r.Start(10);
            BossStarShape shape = r.Pattern.Shape;

            // 활성 1.1초 뒤 서버 시각에서는 2.2초 흘러 44도, 회차 시각 활성 1초 뒤에서는 40도다. 두 각도의 판정이 다른 점을 찾는다
            float tickRotation  = BossStarGeometry.RotationAt(in shape, 1.1, DURATION);
            float roundRotation = BossStarGeometry.RotationAt(in shape, 1.0, DURATION);
            Vector3 found = Vector3.zero;
            bool hasPoint = false;
            for (float degrees = 0f; (degrees < 360f) && (!hasPoint); degrees += 1f)
            {
                for (float radius = 6f; radius <= 13f; radius += 0.1f)
                {
                    Vector3 p = r.Polar(degrees, radius);
                    if (BossStarGeometry.Contains(in shape, p, tickRotation) && (!BossStarGeometry.Contains(in shape, p, roundRotation)))
                    {
                        found = p;
                        hasPoint = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(hasPoint);
            r.Add(11, found);
            r.TickAt(ACTIVATE);
            int before = r.Hits(11);
            r.TickAt(ACTIVATE + 1.1);

            Assert.AreEqual(before + 1, r.Hits(11), "지연된 틱은 현재 서버 시각의 회전 배치로 판정한다");
        }

        [Test]
        public void BigTick_AcrossTheEnd_EndsWithoutPendingRounds()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, r.Polar(72f, APOTHEM));

            r.TickAt(ACTIVATE);
            int afterFirst = r.Resolver.Requests.Count;
            r.TickAt(20.0);

            Assert.AreEqual(afterFirst, r.Resolver.Requests.Count, "종료 뒤에는 밀린 회차를 처리하지 않는다");
            Assert.AreEqual(1, r.Listener.Ends.Count);
            Assert.AreEqual(EBossAreaEnd.Expired, r.Listener.Ends[0].Kind);
        }

        /****************************************
        *      Independence From Areas And Beams
        ****************************************/

        [Test]
        public void StarDamageIsIndependentFromAreasAndFixedBeams()
        {
            Run r = StandardRun();
            r.Start(10);
            Vector3 point = r.Polar(72f, APOTHEM);
            r.Add(11, point);

            BossAreaManager areas = new BossAreaManager(r.Query);
            BossBeamManager beams = new BossBeamManager(r.Query);
            BossEffectComposite composite = new BossEffectComposite(areas, beams, r.Manager);

            int legacyCast = r.Gate.IssueInstanceId();
            Assert.IsTrue(areas.TryReserve(legacyCast, "wolbaek_p05", 3));
            BossAreaSpec area = new BossAreaSpec(legacyCast, 1, "wolbaek_p05", point, 3f, 6f, 1f, RAW_DAMAGE,
                EDamageFlags.Unblockable, 0.0, r.Gate);
            Assert.IsTrue(areas.TryActivate(in area));

            Assert.AreEqual(1, composite.TotalAreaCount, "별형 구조는 보스 전체 장판 상한에 들어가지 않는다");

            r.TickAt(ACTIVATE);
            composite.Tick(ACTIVATE);
            Assert.GreaterOrEqual(r.Hits(11), 2, "장판 한 번과 별 한 번이 서로 대신하지 않는다");
        }

        /****************************************
        *      Cancel And Cleanup
        ****************************************/

        [Test]
        public void Cancel_DuringTelegraph_ReleasesTheSlotNotifiesOnceAndNeverActivates()
        {
            Run r = StandardRun();
            r.Add(11, r.Self.Position);
            r.Start(10);
            r.TickAt(ACTIVATE / 2.0);

            r.CancelAll(EBossInterruptReason.Death);
            r.CancelAll(EBossInterruptReason.Death);
            r.TickRange(ACTIVATE, END + 1.0);

            Assert.AreEqual(1, r.Listener.CancelledStars.Count);
            Assert.AreEqual(0, r.Listener.ActivatedStars.Count);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
            Assert.AreEqual(0, r.Manager.ReservedCount);
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        public void Cancel_WhileActive_RemovesTheStarAndNoFurtherDamage(EBossInterruptReason reason)
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, r.Polar(72f, APOTHEM));
            r.TickRange(ACTIVATE, ACTIVATE + 0.5);
            int before = r.Resolver.Requests.Count;
            Assert.GreaterOrEqual(before, 1);

            r.CancelAll(reason);
            r.TickRange(ACTIVATE + 0.75, END + 2.0);

            Assert.AreEqual(before, r.Resolver.Requests.Count);
            Assert.AreEqual(1, r.Listener.Ends.Count);
            Assert.AreEqual(EBossAreaEnd.Cancelled, r.Listener.Ends[0].Kind);
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [Test]
        public void Cancel_DirectControlDoesNotRemoveAnActiveStar_AndClosedGateStopsDamage()
        {
            Run r = StandardRun();
            r.Start(10);
            r.Add(11, r.Polar(72f, APOTHEM));
            r.TickAt(ACTIVATE);

            r.Manager.Cancel(EBossInterruptReason.DirectControl, CombatIds.NONE);
            Assert.AreEqual(1, r.Manager.ActiveCount);

            int before = r.Hits(11);
            r.Gate.CloseForGood();
            r.TickRange(ACTIVATE + 0.25, END + 0.5);
            Assert.AreEqual(before, r.Hits(11));
            Assert.AreEqual(0, r.Manager.ActiveCount);
        }

        [Test]
        public void Manager_RejectsInvalidReserveAndActivate_AndSlotsReturnOnce()
        {
            BossStarManager manager = new BossStarManager(new StarQuery());
            BossDamageGate gate = new BossDamageGate(new FakeDamageResolver());
            gate.Open();
            BossStarShape shape = BossStarGeometry.Place(ArenaCenter, ARENA_RADIUS, Vector3.forward, 14f, 5, 2, 1.5f, 20f, true);

            Assert.IsFalse(manager.TryReserve(CombatIds.NONE, 1, "x"));
            Assert.IsFalse(manager.TryReserve(1, CombatIds.NONE, "x"));
            Assert.IsFalse(manager.TryReserve(1, 2, null));
            Assert.IsTrue(manager.TryReserve(1, 2, "x"));
            Assert.IsFalse(manager.TryReserve(1, 2, "x"), "같은 구조 번호의 예약은 두 번 받지 않는다");

            BossStarSpec noGate = new BossStarSpec(1, 2, 1, "x", in shape, 6f, 0.25f, 1f, EDamageFlags.None, 0.0, null);
            Assert.IsFalse(manager.TryActivate(in noGate));
            Assert.AreEqual(0, manager.ReservedCount, "잘못된 활성은 슬롯을 돌려준다");

            BossStarSpec missing = new BossStarSpec(1, 9, 1, "x", in shape, 6f, 0.25f, 1f, EDamageFlags.None, 0.0, gate);
            Assert.IsFalse(manager.TryActivate(in missing));
        }

        /****************************************
        *      Brain: Large Group, 25 Percent, Rejection
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = ArenaCenter };
            public StarQuery          Query    = new StarQuery();
            public FakeDamageResolver Damage   = new FakeDamageResolver();
            public FakeBossVitals     Vitals   = new FakeBossVitals();
            public FakeBossRandom     Random   = new FakeBossRandom();
            public RecordingListener  Listener = new RecordingListener();
            public BossStarManager    Manager;
            public BossFixedArena     Arena    = new BossFixedArena(ArenaCenter, ARENA_RADIUS);
            public BossBrain          Brain;
            public List<string>       Used     = new List<string>();

            public BrainRig()
            {
                Query.Targets.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player,
                    Position = ArenaCenter + new Vector3(0f, 0f, 3f) });
                Manager = new BossStarManager(Query, Listener);

                WolbaekPatternFactory inner = new WolbaekPatternFactory(HREF, stars: Manager, arena: Arena, starListener: Listener);
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
        public void Brain_SelectsP01AsLargeAndRunsItWithoutPickingAnotherUntilRecoveryEnds()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            string firstId = null;
            for (double t = 0.5; t < 14.0; t += 0.25)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                    firstId = r.Brain.ActivePatternId;
                }

                if ((started >= 0.0) && (t < (started + RECOVERY_END - 0.01)))
                {
                    Assert.AreEqual(firstId, r.Brain.ActivePatternId, "회전 6초 동안 다음 패턴을 고르지 않는다 t=" + t);
                }
            }

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(P01_ID, firstId);
            Assert.AreEqual(EPatternGroup.Large, LoadDefinition().Group);
            Assert.AreEqual(1, r.Listener.TelegraphStars.Count);
            Assert.AreEqual(1, r.Listener.ActivatedStars.Count);
            Assert.AreEqual(24, r.Listener.Ticks.Count);
        }

        [Test]
        public void Brain_HealthCrossing25DuringTheCast_KeepsValuesAndHitCount()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            for (double t = 0.5; t < 14.0; t += 0.25)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                }

                if ((started >= 0.0) && (t >= (started + ACTIVATE + 1.0)))
                {
                    r.Vitals.HealthRate = 0.2f;
                }
            }

            Assert.GreaterOrEqual(started, 0.0);
            Assert.AreEqual(24, r.Listener.Ticks.Count, "체력 구간이 바뀌어도 진행 중인 별은 그대로다");
            Assert.AreEqual(20f, Mathf.Abs(r.Listener.TelegraphShapes[0].SignedDegreesPerSecond), TOLERANCE);
        }

        [Test]
        public void Brain_RejectedStart_LeavesNoRecordAndRetriesWithoutErrors()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            r.Arena.Set(ArenaCenter, 0f);
            r.Run(0.5, 2.0);
            Assert.AreEqual(0, r.Listener.TelegraphStars.Count);
            Assert.AreEqual(0, r.Used.Count);
            LogAssert.NoUnexpectedReceived();

            r.Arena.Set(ArenaCenter, ARENA_RADIUS);
            r.Run(2.25, 6.0);
            Assert.IsTrue(r.Used.Contains(P01_ID), "시작 실패는 기록하지 않아 직전 사용 x0이 걸리지 않는다");
        }

        [Test]
        public void Brain_CombatEndMidRotation_CleansEverythingAndStopsDamage()
        {
            BrainRig r = new BrainRig();
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            for (double t = 0.5; t < 7.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(1, r.Manager.ActiveCount);
            int before = r.Damage.Requests.Count;
            r.Brain.RequestCombatEnd();
            for (double t = 7.0; t < 14.0; t += 0.25)
            {
                r.Brain.Tick(t);
            }

            Assert.AreEqual(0, r.Manager.ActiveCount);
            Assert.AreEqual(before, r.Damage.Requests.Count);
        }
    }
}
