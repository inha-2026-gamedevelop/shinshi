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
    // 월백 2페이즈 P03 반월 연쇄 폭발을 에셋의 실제 정의와 실제 팩토리로 확인한다. 부채꼴 판정은 1페이즈 P02와 같은 BossFanShape다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 단계 경계는 정의의 float 값을 double로 넓혀 더한 값으로 비교한다
    public class BossPhaseTwoSweepTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string P03_ID       = BossPatternCatalogSO.WOLBAEK_P2_P03_ID;
        private const float  HREF         = 1000f;
        private const float  TOLERANCE    = 0.001f;
        private const double TIME_TOL     = 0.0001;
        private const float  ANGLE_TOL    = 0.1f;
        private const int    LEFT         = 0;
        private const int    RIGHT        = 1;

        private static readonly double Interval    = (double)0.25f;
        private static readonly double TelegraphEnd = (double)2.4f;
        private static readonly double ExecuteEnd  = (double)2.4f + (double)0.75f;
        private static readonly double RecoverEnd  = ExecuteEnd + (double)1.6f;

        private static readonly Vector3 Origin = Vector3.zero;
        private static readonly Vector3 Base   = Vector3.forward;

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class RadiusQuery : ICombatTargetQuery
        {
            public List<ICombatTarget> Targets { get; } = new List<ICombatTarget>();

            public bool FailTryGet;
            public int  LivingPlayersBudget = -1;

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

        private struct Zone
        {
            public int     ZoneId;
            public int     Index;
            public Vector3 Direction;
            public float   Arc;
            public float   Seconds;
        }

        private struct Blast
        {
            public int    ZoneId;
            public int    Index;
            public int    HitCount;
            public double Time;
        }

        private sealed class RecordingListener : IBossSweepTelegraphListener
        {
            public int         Started;
            public Vector3     StartOrigin;
            public Vector3     StartBase;
            public bool        StartIsRight;
            public float       StartRadius;
            public float       StartArc;
            public int         StartZones;
            public float       StartSeconds;
            public float       StartSpan;
            public int         StartCast;
            public List<Zone>  Zones  = new List<Zone>();
            public List<Blast> Blasts = new List<Blast>();
            public List<EBossInterruptReason> Cancels = new List<EBossInterruptReason>();

            public void OnSweepStarted(int castInstanceId, Vector3 origin, Vector3 baseDirection, bool isRight, float radius,
                float totalArcAngle, int zoneCount, float telegraphSeconds, float blastSpanSeconds)
            {
                ++Started;
                StartSpan    = blastSpanSeconds;
                StartCast    = castInstanceId;
                StartOrigin  = origin;
                StartBase    = baseDirection;
                StartIsRight = isRight;
                StartRadius  = radius;
                StartArc     = totalArcAngle;
                StartZones   = zoneCount;
                StartSeconds = telegraphSeconds;
            }

            public void OnZoneScheduled(int castInstanceId, int zoneId, int zoneIndex, Vector3 centerDirection, float zoneArcAngle,
                float secondsUntilBlast)
            {
                Zones.Add(new Zone { ZoneId = zoneId, Index = zoneIndex, Direction = centerDirection, Arc = zoneArcAngle,
                    Seconds = secondsUntilBlast });
            }

            public void OnZoneBlasted(int castInstanceId, int zoneId, int zoneIndex, int hitCount, double blastServerTime)
            {
                Blasts.Add(new Blast { ZoneId = zoneId, Index = zoneIndex, HitCount = hitCount, Time = blastServerTime });
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Cancels.Add(reason);
            }
        }

        private sealed class Run
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin, Forward = new Vector3(0f, 0f, -1f) };
            public RadiusQuery        Query    = new RadiusQuery();
            public FakeDamageResolver Resolver = new FakeDamageResolver();
            public RecordingListener  Listener = new RecordingListener();
            public SequenceRandom     Random   = new SequenceRandom();
            public BossDamageGate     Gate;
            public BossAttackFsm      Fsm      = new BossAttackFsm();
            public PatternDefinition  Definition;
            public BossSweepBlastPattern Pattern;
            public int                CastId;
            public FakeCombatTarget   Anchor;

            public Run(int side = RIGHT)
            {
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = LoadDefinition();
                CastId     = Gate.IssueInstanceId();
                Random.Enqueue(side);

                // 기준 방향을 정하는 선택된 타깃, 정면 30m라 반경 밖이다
                Anchor = Add(10, new Vector3(0f, 0f, 30f));
            }

            public FakeCombatTarget Add(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget target = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player,
                    Position = position, IsSummon = isSummon };
                Query.Targets.Add(target);
                return target;
            }

            // 기준 방향 +z에서 오른쪽을 양수로 잰 각도와 거리의 위치
            public static Vector3 At(float angle, float distance)
            {
                return Quaternion.AngleAxis(angle, Vector3.up) * Base * distance;
            }

            public void Start(int selectedTargetId = 10, double time = 0.0)
            {
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, sweepListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                Pattern = (BossSweepBlastPattern)pattern;
                BossPatternContext context = new BossPatternContext(Definition, CastId, selectedTargetId, Self, Query, Gate, Random);
                Assert.IsTrue(Fsm.Start(Definition, pattern, in context, time));
            }

            public void TickAt(double time)
            {
                Fsm.Tick(time);
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

            // 모든 구역 폭발을 순서대로 지나간다
            public void RunAll()
            {
                TickAt(0.5);
                TickAt(TelegraphEnd);
                TickAt(TelegraphEnd + Interval);
                TickAt(TelegraphEnd + (2 * Interval));
                TickAt(TelegraphEnd + (3 * Interval));
                TickAt(RecoverEnd + 0.1);
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
            Assert.IsTrue(LoadCatalog().TryGetPattern(P03_ID, out PatternDefinition definition));
            return definition;
        }

        private static int BlastIndexHitting(Run r, int targetId)
        {
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                if (r.Resolver.Requests[i].HitTargetId == targetId)
                {
                    int action = r.Resolver.Requests[i].ActionInstanceId;
                    for (int z = 0; z < r.Listener.Zones.Count; ++z)
                    {
                        if (r.Listener.Zones[z].ZoneId == action)
                        {
                            return r.Listener.Zones[z].Index;
                        }
                    }
                }
            }

            return -1;
        }

        /****************************************
        *      Asset, Factory, Readiness Agree
        ****************************************/

        [Test]
        public void Asset_P03_HasAgreedValuesAndKeepsSelectionData()
        {
            PatternDefinition p = LoadDefinition();

            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EPatternGroup.Large, p.Group);
            Assert.AreEqual(2.4f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0f, p.TrackingSeconds, "추적하지 않는다");
            Assert.AreEqual(0.75f, p.ActiveSeconds, TOLERANCE, "첫 폭발부터 마지막 폭발까지");
            Assert.AreEqual(1.6f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Fan, p.Shape.Shape);
            Assert.AreEqual(18f, p.Shape.Range, TOLERANCE);
            Assert.AreEqual(180f, p.Shape.ArcAngle, TOLERANCE);
            Assert.AreEqual(4, p.RawSweepZoneCount);
            Assert.AreEqual(0.25f, p.SweepIntervalSeconds, TOLERANCE);
            Assert.AreEqual(0.18f, p.DamageRef, TOLERANCE);
            Assert.IsTrue(p.Guardable);
            Assert.IsFalse(p.HasControlWindow);
            Assert.AreEqual(12f, p.BaseWeight, TOLERANCE);
            Assert.AreEqual(14f, p.EnhancedBaseWeight, TOLERANCE);
        }

        [Test]
        public void Asset_P03_FactoryCanCreateAndPatternIsReadyAgree()
        {
            PatternDefinition p = LoadDefinition();
            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF);

            Assert.IsTrue(factory.CanCreate(p));
            Assert.IsInstanceOf<BossSweepBlastPattern>(factory.Create(p));
            Assert.IsTrue(LoadCatalog().IsPatternReady(WOLBAEK_ID, p));
        }

        [Test]
        public void Asset_PhaseOneP02_StaysSingleFan()
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern("wolbaek_p02", out PatternDefinition p2));

            Assert.AreEqual(1, p2.Phase);
            Assert.AreEqual(0, p2.RawSweepZoneCount);
            Assert.AreEqual(100f, p2.Shape.ArcAngle, TOLERANCE);
            Assert.AreEqual(2f, p2.TelegraphSeconds, TOLERANCE);
        }

        /****************************************
        *       Start: Center, Direction, Side
        ****************************************/

        [Test]
        public void Start_FixesCenterAndBaseDirectionTowardTheTargetAndAnnouncesThem()
        {
            Run r = new Run();
            r.Anchor.Position = new Vector3(10f, 0f, 0f);
            r.Self.Position = new Vector3(0f, 0f, 0f);

            r.Start();

            Assert.AreEqual(1, r.Listener.Started);
            Assert.AreEqual(r.CastId, r.Listener.StartCast);
            Assert.AreEqual(Vector3.zero, r.Listener.StartOrigin);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.right, r.Listener.StartBase), ANGLE_TOL, "유효 타깃을 향한 수평 방향");
            Assert.AreEqual(18f, r.Listener.StartRadius, TOLERANCE);
            Assert.AreEqual(180f, r.Listener.StartArc, TOLERANCE);
            Assert.AreEqual(4, r.Listener.StartZones);
            Assert.AreEqual(2.4f, r.Listener.StartSeconds, TOLERANCE);
            Assert.AreEqual(0.75f, r.Listener.StartSpan, TOLERANCE);
        }

        [Test]
        public void Tick_TargetMovesAndBossTurns_CenterAndBaseDirectionStayFixed()
        {
            Run r = new Run();
            FakeCombatTarget victim = r.Add(11, Run.At(100f, 4f)); // 오른쪽 구역 2
            r.Start();

            r.Anchor.Position = new Vector3(-30f, 0f, 0f);
            r.Self.Position = new Vector3(5f, 0f, 5f);
            r.Self.Forward = Vector3.right;
            r.RunAll();

            Assert.AreEqual(Vector3.zero, r.Pattern.Origin);
            Assert.AreEqual(0f, Vector3.Angle(Base, r.Pattern.BaseDirection), ANGLE_TOL);
            Assert.AreEqual(1, r.Hits(victim.CombatId), "고정한 중심과 방향 기준으로 판정한다");
            Assert.AreEqual(Vector3.zero, r.Resolver.Requests[0].SourcePosition);
        }

        [Test]
        public void Start_SideComesFromTheServerRandomWithTwoOutcomes()
        {
            Run right = new Run(RIGHT);
            right.Start();
            Assert.IsTrue(right.Pattern.IsRight);
            CollectionAssert.AreEqual(new[] { 2 }, right.Random.Arguments);

            Run left = new Run(LEFT);
            left.Start();
            Assert.IsFalse(left.Pattern.IsRight);
        }

        [Test]
        public void Start_ZoneCenterDirectionsFollowTheChosenSideInFortyFiveDegreeSteps()
        {
            foreach (int side in new[] { RIGHT, LEFT })
            {
                Run r = new Run(side);
                r.Start();

                float sign = (side == RIGHT) ? 1f : -1f;
                Assert.AreEqual(4, r.Listener.Zones.Count);
                for (int i = 0; i < 4; ++i)
                {
                    Zone zone = r.Listener.Zones[i];
                    Assert.AreEqual(i, zone.Index);
                    Assert.AreEqual(45f, zone.Arc, TOLERANCE);
                    float signedAngle = Vector3.SignedAngle(Base, zone.Direction, Vector3.up);
                    Assert.AreEqual(sign * 45f * (i + 0.5f), signedAngle, ANGLE_TOL);
                    Assert.AreEqual(2.4f + (i * 0.25f), zone.Seconds, TOLERANCE);
                }
            }
        }

        [Test]
        public void Start_TargetOnTheBossHorizontalPosition_UsesTheBossForwardFacing()
        {
            Run r = new Run();
            r.Anchor.Position = new Vector3(0f, 5f, 0f); // 수평 위치가 보스와 같다
            r.Self.Forward = Vector3.left;

            r.Start();

            Assert.AreEqual(0f, Vector3.Angle(Vector3.left, r.Pattern.BaseDirection), ANGLE_TOL);
        }

        /****************************************
        *       Start Rejection And Recording
        ****************************************/

        [Test]
        public void Start_NoValidTarget_RejectsWithoutAnyNotificationOrRandomUse()
        {
            Run missing = new Run();
            missing.Start(selectedTargetId: 999);
            Assert.IsTrue(missing.Pattern.IsStartRejected);
            Assert.AreEqual(0, missing.Listener.Started);
            Assert.AreEqual(0, missing.Listener.Zones.Count);
            Assert.AreEqual(0, missing.Random.Arguments.Count, "임의 방향이나 좌우를 정하지 않는다");

            Run summon = new Run();
            summon.Add(20, Run.At(10f, 3f), isSummon: true);
            summon.Start(selectedTargetId: 20);
            Assert.IsTrue(summon.Pattern.IsStartRejected, "소환물은 기준 타깃이 아니다");

            Run dead = new Run();
            dead.Anchor.LifeState = EParticipantLifeState.Dead;
            dead.Start();
            Assert.IsTrue(dead.Pattern.IsStartRejected);
        }

        [Test]
        public void Cancel_AfterRejectedStart_NotifiesNothing()
        {
            Run r = new Run();
            r.Start(selectedTargetId: 999);

            r.Fsm.Cancel(EBossInterruptReason.None);
            r.TickAt(100.0);

            Assert.AreEqual(0, r.Listener.Cancels.Count);
            Assert.AreEqual(0, r.Listener.Blasts.Count);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        [Test]
        public void Start_TargetDiesOrLeavesAfterStart_DoesNotCancelTheAttack()
        {
            Run r = new Run();
            FakeCombatTarget bystander = r.Add(11, Run.At(10f, 4f));
            r.Start();

            r.Anchor.LifeState = EParticipantLifeState.Dead;
            r.Query.Targets.Remove(r.Anchor);
            r.RunAll();

            Assert.AreEqual(4, r.Listener.Blasts.Count);
            Assert.AreEqual(1, r.Hits(bystander.CombatId));
            Assert.AreEqual(0, r.Listener.Cancels.Count);
        }

        /****************************************
        *     Geometry: Radius, Zones, Boundaries
        ****************************************/

        [Test]
        public void Blast_EachZoneHitsOnlyItsOwnFortyFiveDegreesOnTheChosenSide()
        {
            foreach (int side in new[] { RIGHT, LEFT })
            {
                Run r = new Run(side);
                float sign = (side == RIGHT) ? 1f : -1f;
                FakeCombatTarget[] inZone =
                {
                    r.Add(21, Run.At(sign * 20f, 5f)), r.Add(22, Run.At(sign * 70f, 5f)),
                    r.Add(23, Run.At(sign * 110f, 5f)), r.Add(24, Run.At(sign * 160f, 5f))
                };
                FakeCombatTarget otherSide = r.Add(30, Run.At(-sign * 20f, 5f));
                r.Start();

                r.RunAll();

                for (int i = 0; i < 4; ++i)
                {
                    Assert.AreEqual(1, r.Hits(inZone[i].CombatId), "구역 " + i);
                    Assert.AreEqual(i, BlastIndexHitting(r, inZone[i].CombatId), "구역 " + i + "의 순서");
                }

                Assert.AreEqual(0, r.Hits(otherSide.CombatId), "반대쪽 반원은 맞지 않는다");
            }
        }

        [Test]
        public void Blast_RadiusBoundary_EighteenMetersIsInsideAndBeyondIsOutside()
        {
            Run r = new Run();
            FakeCombatTarget edge = r.Add(21, Run.At(0f, 18f));
            FakeCombatTarget beyond = r.Add(22, Run.At(0f, 18.01f));
            r.Start();

            r.RunAll();

            Assert.AreEqual(1, r.Hits(edge.CombatId));
            Assert.AreEqual(0, r.Hits(beyond.CombatId));
        }

        [Test]
        public void Blast_SharedZoneBoundary_IsHitOnceInTheEarlierZone()
        {
            Run r = new Run();
            FakeCombatTarget onSeam = r.Add(21, Run.At(45f, 4f));
            r.Start();

            r.RunAll();

            Assert.AreEqual(1, r.Hits(onSeam.CombatId), "공유 경계에서 두 번 맞지 않는다");
            Assert.AreEqual(0, BlastIndexHitting(r, onSeam.CombatId), "먼저 폭발하는 구역에서 처리한다");
        }

        [Test]
        public void Blast_FrontAndBackEdgesAndCenterPoint_AreEachHitExactlyOnce()
        {
            Run r = new Run();
            FakeCombatTarget front = r.Add(21, Run.At(0f, 4f));
            FakeCombatTarget back = r.Add(22, Run.At(180f, 4f));
            FakeCombatTarget center = r.Add(23, Origin);
            r.Start();

            r.RunAll();

            Assert.AreEqual(1, r.Hits(front.CombatId));
            Assert.AreEqual(0, BlastIndexHitting(r, front.CombatId));
            Assert.AreEqual(1, r.Hits(back.CombatId));
            Assert.AreEqual(3, BlastIndexHitting(r, back.CombatId));
            Assert.AreEqual(1, r.Hits(center.CombatId), "보스 중심점은 모든 구역에 걸리지만 한 번만 맞는다");
            Assert.AreEqual(0, BlastIndexHitting(r, center.CombatId));
        }

        [Test]
        public void Blast_SummonsAndLivingPlayersAreTargetsAndDeadOnesAreNot()
        {
            Run r = new Run();
            FakeCombatTarget summon = r.Add(21, Run.At(20f, 3f), isSummon: true);
            FakeCombatTarget dead = r.Add(22, Run.At(30f, 3f));
            dead.LifeState = EParticipantLifeState.Dead;
            FakeCombatTarget alive = r.Add(23, Run.At(25f, 3f));
            r.Start();

            r.RunAll();

            Assert.AreEqual(1, r.Hits(summon.CombatId));
            Assert.AreEqual(1, r.Hits(alive.CombatId));
            Assert.AreEqual(0, r.Hits(dead.CombatId));
        }

        /****************************************
        *       Once Per Cast And Damage Data
        ****************************************/

        [Test]
        public void Blast_TargetMovingAcrossZones_IsHitOnlyOncePerCast()
        {
            Run r = new Run();
            FakeCombatTarget runner = r.Add(21, Run.At(20f, 4f));
            r.Start();

            r.TickAt(0.5);
            r.TickAt(TelegraphEnd);
            runner.Position = Run.At(65f, 4f);
            r.TickAt(TelegraphEnd + Interval);
            runner.Position = Run.At(110f, 4f);
            r.TickAt(TelegraphEnd + (2 * Interval));
            runner.Position = Run.At(160f, 4f);
            r.TickAt(TelegraphEnd + (3 * Interval));

            Assert.AreEqual(1, r.Hits(runner.CombatId), "여러 구역을 가로질러도 같은 시전에서 다시 맞지 않는다");
            Assert.AreEqual(1, r.Pattern.ProcessedCount);
        }

        [Test]
        public void Blast_ClosedGateStillRecordsTheTargetSoItIsNotJudgedAgain()
        {
            Run r = new Run();
            FakeCombatTarget victim = r.Add(21, Run.At(20f, 4f));
            r.Start();

            r.TickAt(0.5);
            r.Gate.CloseForGood();
            r.TickAt(TelegraphEnd);

            Assert.AreEqual(0, r.Hits(victim.CombatId), "게이트가 닫히면 피해 요청이 나가지 않는다");
            Assert.AreEqual(1, r.Pattern.ProcessedCount, "처리한 대상으로 기록해 이후 구역에서 다시 판정하지 않는다");
        }

        [Test]
        public void Blast_DamageIsGuardableOneEighteenthHrefWithOwnActionIdPerZoneAndSharedCastId()
        {
            Run r = new Run();
            r.Add(21, Run.At(20f, 4f));
            r.Add(22, Run.At(65f, 4f));
            r.Add(23, Run.At(110f, 4f));
            r.Add(24, Run.At(160f, 4f));
            r.Start();

            r.RunAll();

            Assert.AreEqual(4, r.Resolver.Requests.Count);
            HashSet<int> actions = new HashSet<int>();
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                Assert.AreEqual(0.18f * HREF, request.RawDamage, TOLERANCE);
                Assert.IsFalse(request.IsUnblockable, "반격 가능");
                Assert.AreEqual(r.CastId, request.CastInstanceId);
                Assert.AreEqual(P03_ID, request.SourceDefinitionId);
                actions.Add(request.ActionInstanceId);
            }

            Assert.AreEqual(4, actions.Count, "회차마다 별도 동작 번호");
            for (int i = 0; i < 4; ++i)
            {
                Assert.AreEqual(r.Listener.Zones[i].ZoneId, r.Listener.Blasts[i].ZoneId, "구역 번호가 그 회차의 동작 번호다");
            }
        }

        /****************************************
        *             Timing And Coarse Ticks
        ****************************************/

        [Test]
        public void Tick_FineSteps_BlastsAtTelegraphEndThenEveryQuarterSecondAndRecoversEightTenths()
        {
            Run r = new Run();
            r.Start();

            r.TickAt(TelegraphEnd - 0.01);
            Assert.AreEqual(0, r.Listener.Blasts.Count, "예고 중에는 폭발하지 않는다");

            r.TickAt(TelegraphEnd);
            Assert.AreEqual(1, r.Listener.Blasts.Count, "예고 종료 순간 첫 구역");
            r.TickAt(TelegraphEnd + Interval - 0.01);
            Assert.AreEqual(1, r.Listener.Blasts.Count);
            r.TickAt(TelegraphEnd + Interval);
            r.TickAt(TelegraphEnd + (2 * Interval));
            r.TickAt(TelegraphEnd + (3 * Interval));
            Assert.AreEqual(4, r.Listener.Blasts.Count, "실행 끝 경계의 마지막 구역을 놓치지 않는다");

            for (int i = 0; i < 4; ++i)
            {
                Assert.AreEqual(i, r.Listener.Blasts[i].Index);
                Assert.AreEqual(TelegraphEnd + (i * Interval), r.Listener.Blasts[i].Time, TIME_TOL);
            }

            r.TickAt(ExecuteEnd + 0.01);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(RecoverEnd - 0.01);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(RecoverEnd + 0.01);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.AreEqual(4, r.Listener.Blasts.Count);
        }

        [Test]
        public void Tick_HugeSteps_BlastEveryZoneOnceInOrderAtScheduledTimesAndOncePerTarget()
        {
            Run r = new Run();
            FakeCombatTarget first = r.Add(21, Run.At(20f, 4f));
            FakeCombatTarget last = r.Add(22, Run.At(160f, 4f));
            r.Start();

            r.TickAt(100.0);
            r.TickAt(101.0);
            r.TickAt(200.0);

            Assert.AreEqual(4, r.Listener.Blasts.Count);
            for (int i = 0; i < 4; ++i)
            {
                Assert.AreEqual(i, r.Listener.Blasts[i].Index);
                Assert.AreEqual(TelegraphEnd + (i * Interval), r.Listener.Blasts[i].Time, TIME_TOL);
            }

            Assert.AreEqual(1, r.Hits(first.CombatId));
            Assert.AreEqual(1, r.Hits(last.CombatId));
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
        }

        [Test]
        public void Tick_HugeStepWithOneTargetInSeveralDueZones_StillHitsOnce()
        {
            Run r = new Run();
            FakeCombatTarget target = r.Add(21, Run.At(45f, 3f));
            r.Start();

            r.TickAt(100.0);

            Assert.AreEqual(1, r.Hits(target.CombatId), "밀린 구역 피해가 한 대상에게 몰리지 않는다");
        }

        /****************************************
        *          Cancel And Priority
        ****************************************/

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        public void Cancel_MidSequence_StopsRemainingZonesAndFurtherDamage(EBossInterruptReason reason)
        {
            Run r = new Run();
            FakeCombatTarget early = r.Add(21, Run.At(20f, 4f));
            FakeCombatTarget late = r.Add(22, Run.At(110f, 4f));
            r.Start();
            r.TickAt(0.5);
            r.TickAt(TelegraphEnd);
            r.TickAt(TelegraphEnd + Interval);
            Assert.AreEqual(2, r.Listener.Blasts.Count);

            r.Gate.CloseForGood();
            r.Fsm.Cancel(reason);
            r.TickAt(TelegraphEnd + (2 * Interval));
            r.TickAt(100.0);

            Assert.AreEqual(2, r.Listener.Blasts.Count, "남은 예약은 나가지 않는다");
            Assert.AreEqual(1, r.Hits(early.CombatId), "이미 적용한 피해는 그대로다");
            Assert.AreEqual(0, r.Hits(late.CombatId));
            CollectionAssert.AreEqual(new[] { reason }, r.Listener.Cancels);
        }

        [Test]
        public void Cancel_BeforeFirstZone_NotifiesOnceAndNeverBlasts()
        {
            Run r = new Run();
            r.Add(21, Run.At(20f, 4f));
            r.Start();
            r.TickAt(0.5);

            r.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            r.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            r.TickAt(100.0);

            Assert.AreEqual(1, r.Listener.Cancels.Count);
            Assert.AreEqual(0, r.Listener.Blasts.Count);
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        /****************************************
        *      Brain: Large Group, 25 Percent, Rejection
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public RadiusQuery        Query    = new RadiusQuery();
            public FakeDamageResolver Damage   = new FakeDamageResolver();
            public FakeBossVitals     Vitals   = new FakeBossVitals();
            public FakeBossRandom     Random   = new FakeBossRandom();
            public RecordingListener  Listener = new RecordingListener();
            public BossBrain          Brain;
            public List<string>       Used     = new List<string>();

            public BrainRig(Vector3 playerPosition)
            {
                Query.Targets.Add(new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = playerPosition });
                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog()),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = new WolbaekPatternFactory(HREF, sweepListener: Listener),
                    Effects  = new FakeEffectLifetime(),
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
        public void Brain_SelectsP03AsLargeAndFlowFromLargeLowersItAfterUse()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 3f));
            r.TransitionAndHandoff();
            r.Random.Roll = 0f; // 가중치가 있는 첫 후보부터

            r.Run(0.5, 6.0);

            LogAssert.NoUnexpectedReceived();
            Assert.GreaterOrEqual(r.Used.Count, 1);
            Assert.AreEqual(P03_ID, r.Used[0]);
            Assert.AreEqual(EPatternGroup.Large, LoadDefinition().Group);
            Assert.AreEqual(1, r.Listener.Started);
        }

        [Test]
        public void Weight_At25PercentBoundary_UsesEnhancedBaseAndLargeFlow()
        {
            BossPatternCatalogSO catalog = LoadCatalog();
            Assert.IsTrue(catalog.TryGetBoss(WOLBAEK_ID, out BossPatternCatalogSO.BossDefinition boss));
            PatternDefinition p = LoadDefinition();

            // 근거리 1.3, 방향 1.0, 주변 1명 1.0, 최근 없음
            float normalAfterLarge = BossPatternSelector.ComputeSecondPhaseWeight(p, boss, 0.2501f, EPatternGroup.Large, 0, 0, 0, 3);
            float enhancedAfterLarge = BossPatternSelector.ComputeSecondPhaseWeight(p, boss, 0.25f, EPatternGroup.Large, 0, 0, 0, 3);
            float first = BossPatternSelector.ComputeSecondPhaseWeight(p, boss, 0.6f, EPatternGroup.None, 0, 0, 0, 3);

            Assert.AreEqual(12f * 1.3f * 0.25f, normalAfterLarge, TOLERANCE, "25% 초과, 직전 Large에서 Large는 x0.25");
            Assert.AreEqual(14f * 1.3f * 0.45f, enhancedAfterLarge, TOLERANCE, "25% 이하, 직전 Large에서 Large는 x0.45");
            Assert.AreEqual(12f * 1.3f, first, TOLERANCE, "첫 선택의 흐름 배율은 1");
        }

        [Test]
        public void Brain_HealthCrossing25DuringTheCast_KeepsOrderAndTiming()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 3f));
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            double started = -1.0;
            for (double t = 0.5; t < 6.0; t += 0.25)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                }

                if ((started >= 0.0) && (t >= (started + 1.25)))
                {
                    r.Vitals.HealthRate = 0.2f;
                }
            }

            Assert.GreaterOrEqual(started, 0.0);
            Assert.AreEqual(4, r.Listener.Blasts.Count);
            for (int i = 0; i < 4; ++i)
            {
                Assert.AreEqual(i, r.Listener.Blasts[i].Index);
                Assert.AreEqual(started + TelegraphEnd + (i * Interval), r.Listener.Blasts[i].Time, TIME_TOL);
            }
        }

        [Test]
        public void Brain_RejectedStart_LeavesNoRecordAndRetriesWithoutErrors()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 3f));
            r.TransitionAndHandoff();
            r.Random.Roll = 0f;

            // 선택 때는 조회되지만 패턴이 시작할 때는 대상 번호 조회가 실패하는 상황
            r.Query.FailTryGet = true;
            r.Run(0.5, 2.0);
            Assert.AreEqual(0, r.Listener.Started, "시작을 거절해 예고도 없다");
            Assert.AreEqual(0, r.Used.Count);
            LogAssert.NoUnexpectedReceived();

            // 다시 조회되면 같은 패턴이 직전 기록 없이 선택된다
            r.Query.FailTryGet = false;
            r.Run(2.25, 6.0);
            Assert.IsTrue(r.Used.Contains(P03_ID), "시작 실패는 기록하지 않아 직전 사용 x0이 걸리지 않는다");
        }
    }
}
