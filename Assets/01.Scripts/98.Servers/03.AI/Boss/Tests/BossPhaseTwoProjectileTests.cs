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
    // 월백 2페이즈 P05 순차 여우불 투사체를 에셋의 실제 정의와 실제 팩토리, 실제 지속물 관리자로 확인한다. 실행은 1페이즈 P04의 투사체 패턴을 그대로 쓰고 발 수와 간격만 정의가 정한다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다. 단계 경계는 정의의 float 값을 double로 넓혀 더한 값으로 비교한다
    public class BossPhaseTwoProjectileTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string P05_ID       = BossPatternCatalogSO.WOLBAEK_P2_P05_ID;
        private const float  HREF         = 1000f;
        private const float  TOLERANCE    = 0.001f;
        private const double TIME_TOL     = 0.0001;
        private const float  ANGLE_TOL    = 0.1f;
        private const float  STEP         = 0.05f;

        // 예고 1.8초 뒤 첫 발, 실행 구간 0.6초가 첫 발부터 마지막 발까지고 회복은 1초다
        private static readonly double FirstShot  = (double)1.8f;
        private static readonly double SecondShot = FirstShot + (double)0.3f;
        private static readonly double ThirdShot  = FirstShot + ((double)0.3f * 2.0);
        private static readonly double ExecuteEnd = FirstShot + (double)0.6f;
        private static readonly double RecoverEnd = ExecuteEnd + (double)1f;

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class RadiusQuery : ICombatTargetQuery
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
                        || (target.IsSummon && !includeSummons))
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

        private sealed class FakeWall : IBossWallQuery
        {
            public float Distance = float.MaxValue;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return Mathf.Min(maxDistance, Distance);
            }
        }

        private struct Launch
        {
            public int     CastId;
            public int     ProjectileId;
            public Vector3 Origin;
            public Vector3 Direction;
            public float   PathLength;
            public double  Time;
        }

        private struct End
        {
            public int                CastId;
            public int                ProjectileId;
            public EBossProjectileEnd Kind;
        }

        private sealed class RecordingListener : IBossProjectileListener, IBossProjectileTelegraphListener
        {
            public List<Launch> Launches = new List<Launch>();
            public List<End>    Ends     = new List<End>();
            public List<Vector3> Locks   = new List<Vector3>();
            public int          TelegraphStarts;
            public int          TelegraphCancels;

            public void OnProjectileLaunched(int castInstanceId, int projectileId, Vector3 origin, Vector3 direction, float speed,
                float width, float pathLength, double launchServerTime)
            {
                Launches.Add(new Launch { CastId = castInstanceId, ProjectileId = projectileId, Origin = origin,
                    Direction = direction, PathLength = pathLength, Time = launchServerTime });
            }

            public void OnProjectileEnded(int castInstanceId, int projectileId, EBossProjectileEnd end, Vector3 position, int hitTargetId)
            {
                Ends.Add(new End { CastId = castInstanceId, ProjectileId = projectileId, Kind = end });
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
                float telegraphSeconds)
            {
                ++TelegraphStarts;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length) { }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                Locks.Add(direction);
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                ++TelegraphCancels;
            }
        }

        // 공격 FSM에 실제 팩토리가 만든 패턴을 올리고 지속물 관리자를 BossCombat처럼 공격 진행 뒤에 부른다
        private sealed class Run
        {
            public FakeCombatTarget    Self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Vector3.zero };
            public FakeCombatTarget    Tracked  = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public RadiusQuery         Query    = new RadiusQuery();
            public FakeDamageResolver  Resolver = new FakeDamageResolver();
            public FakeWall            Wall     = new FakeWall();
            public RecordingListener   Listener = new RecordingListener();
            public BossDamageGate      Gate;
            public BossProjectileManager Manager;
            public BossAttackFsm       Fsm      = new BossAttackFsm();
            public PatternDefinition   Definition;
            public int                 CastId;

            public Run()
            {
                Query.Targets.Add(Tracked);
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = LoadDefinition();
                Manager    = new BossProjectileManager(Query, Wall, Listener);
                CastId     = Gate.IssueInstanceId();
            }

            public void Start(double time = 0.0)
            {
                WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, projectiles: Manager, projectileListener: Listener);
                IBossPattern pattern = factory.Create(Definition);
                Assert.IsNotNull(pattern);
                BossPatternContext context = new BossPatternContext(Definition, CastId, Tracked.CombatId, Self, Query, Gate);
                Assert.IsTrue(Fsm.Start(Definition, pattern, in context, time));
            }

            public void TickAt(double time)
            {
                Fsm.Tick(time);
                Manager.Tick(time);
            }

            public void TickRange(double from, double to, float step = STEP)
            {
                int count = (int)Math.Round((to - from) / step);
                for (int i = 1; i <= count; ++i)
                {
                    TickAt(from + (i * (double)step));
                }
            }

            public FakeCombatTarget Add(int id, Vector3 position, bool isSummon = false)
            {
                FakeCombatTarget target = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player,
                    Position = position, IsSummon = isSummon };
                Query.Targets.Add(target);
                return target;
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
            Assert.IsTrue(LoadCatalog().TryGetPattern(P05_ID, out PatternDefinition definition));
            return definition;
        }

        private static float AimDegrees(Vector3 direction)
        {
            return Vector3.SignedAngle(Vector3.forward, direction, Vector3.up);
        }

        /****************************************
        *      Asset, Factory, Readiness Agree
        ****************************************/

        [Test]
        public void Asset_P05_HasAgreedValuesAndKeepsSelectionData()
        {
            PatternDefinition p = LoadDefinition();

            Assert.AreEqual(2, p.Phase);
            Assert.AreEqual(EPatternGroup.Basic, p.Group);
            Assert.AreEqual(1.8f, p.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0.45f, p.TrackingSeconds, TOLERANCE);
            Assert.AreEqual(120f, p.TurnRate, TOLERANCE);
            Assert.AreEqual(0.6f, p.ActiveSeconds, TOLERANCE, "첫 발부터 마지막 발까지");
            Assert.AreEqual(1f, p.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(3, p.ProjectileCount);
            Assert.AreEqual(0.3f, p.ProjectileIntervalSeconds, TOLERANCE);
            Assert.AreEqual(EEnemyAttackShape.Projectile, p.Shape.Shape);
            Assert.AreEqual(35f, p.Shape.Range, TOLERANCE);
            Assert.AreEqual(1f, p.Shape.Width, TOLERANCE);
            Assert.AreEqual(10f, p.Shape.ProjectileSpeed, TOLERANCE);
            Assert.AreEqual(0.18f, p.DamageRef, TOLERANCE);
            Assert.IsTrue(p.Guardable);
            Assert.AreEqual(EBossActionState.Recover, p.ControlPhase);
            Assert.AreEqual(1f, p.ControlWindowSeconds, TOLERANCE, "제어 구간이 후딜 전체다");
            Assert.IsFalse(p.ControlCancelsPattern);
            Assert.AreEqual(22f, p.BaseWeight, TOLERANCE);
            Assert.AreEqual(18f, p.EnhancedBaseWeight, TOLERANCE);
        }

        [Test]
        public void Asset_P05_FactoryCanCreateAndPatternIsReadyAgree()
        {
            PatternDefinition p = LoadDefinition();
            BossProjectileManager manager = new BossProjectileManager(new RadiusQuery());

            Assert.IsFalse(new WolbaekPatternFactory(HREF).CanCreate(p), "발사 창구가 없으면 만들 수 없다");
            Assert.IsNull(new WolbaekPatternFactory(HREF).Create(p));

            WolbaekPatternFactory factory = new WolbaekPatternFactory(HREF, projectiles: manager);
            Assert.IsTrue(factory.CanCreate(p));
            Assert.IsInstanceOf<BossProjectilePattern>(factory.Create(p));
            Assert.IsTrue(LoadCatalog().IsPatternReady(WOLBAEK_ID, p));
        }

        [Test]
        public void Asset_PhaseOneP04_StaysSingleShot()
        {
            Assert.IsTrue(LoadCatalog().TryGetPattern("wolbaek_p04", out PatternDefinition p4));

            Assert.AreEqual(1, p4.Phase);
            Assert.AreEqual(1, p4.ProjectileCount, "단발");
            Assert.AreEqual(0, p4.RawProjectileCount);
            Assert.AreEqual(1.8f, p4.TelegraphSeconds, TOLERANCE);
            Assert.AreEqual(0f, p4.ActiveSeconds);
            Assert.AreEqual(1f, p4.RecoverySeconds, TOLERANCE);
            Assert.AreEqual(EPatternGroup.None, p4.Group);
        }

        /****************************************
        *         Aim, Schedule, Direction
        ****************************************/

        [Test]
        public void Tick_TrackingTurnsThenLocks_AndAllThreeShotsShareTheLockedDirection()
        {
            Run r = new Run();
            r.Tracked.Position = new Vector3(10f, 0f, 0f);
            r.Start();

            r.TickRange(0.0, 0.4);
            Assert.AreEqual(0, r.Listener.Locks.Count);
            r.TickAt(0.45);
            Assert.AreEqual(1, r.Listener.Locks.Count);
            Assert.AreEqual(90f, AimDegrees(r.Listener.Locks[0]), ANGLE_TOL, "부드러운 조준이라 0.45초 안에 대상 방향(90도)에 닿는다");

            r.Tracked.Position = new Vector3(-10f, 0f, 0f);
            r.TickRange(0.45, RecoverEnd + 0.2);

            Assert.AreEqual(3, r.Listener.Launches.Count);
            for (int i = 0; i < 3; ++i)
            {
                Assert.AreEqual(0f, Vector3.Angle(r.Listener.Locks[0], r.Listener.Launches[i].Direction), ANGLE_TOL, "유도하지 않는다");
            }
        }

        [Test]
        public void Tick_FineSteps_FiresAtTelegraphEndThenEveryThreeTenthsAndRecoversHalfSecond()
        {
            Run r = new Run();
            r.Start();

            r.TickRange(0.0, 0.85);
            Assert.AreEqual(0, r.Listener.Launches.Count);

            r.TickAt(FirstShot);
            Assert.AreEqual(1, r.Listener.Launches.Count, "예고 종료 순간 1발째");
            Assert.AreEqual(EBossAttackState.Execute, r.Fsm.State);

            r.TickAt(SecondShot - 0.01);
            Assert.AreEqual(1, r.Listener.Launches.Count);
            r.TickAt(SecondShot);
            Assert.AreEqual(2, r.Listener.Launches.Count);
            r.TickAt(ThirdShot);
            Assert.AreEqual(3, r.Listener.Launches.Count, "실행 끝 경계의 마지막 발을 놓치지 않는다");

            Assert.AreEqual(FirstShot, r.Listener.Launches[0].Time, TIME_TOL);
            Assert.AreEqual(SecondShot, r.Listener.Launches[1].Time, TIME_TOL);
            Assert.AreEqual(ThirdShot, r.Listener.Launches[2].Time, TIME_TOL);

            r.TickAt(ExecuteEnd + 0.01);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(RecoverEnd - 0.01);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            r.TickAt(RecoverEnd + 0.01);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.AreEqual(3, r.Listener.Launches.Count);
        }

        [Test]
        public void Tick_HugeStep_FiresThreeShotsOnceAtScheduledTimes()
        {
            Run r = new Run();
            r.Start();

            r.TickAt(100.0);
            r.TickAt(101.0);
            r.TickAt(200.0);

            Assert.AreEqual(3, r.Listener.Launches.Count);
            Assert.AreEqual(FirstShot, r.Listener.Launches[0].Time, TIME_TOL);
            Assert.AreEqual(SecondShot, r.Listener.Launches[1].Time, TIME_TOL);
            Assert.AreEqual(ThirdShot, r.Listener.Launches[2].Time, TIME_TOL);
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);
            Assert.AreEqual(0, r.Manager.ActiveCount, "비행 시간이 지나 모두 끝났다");
            Assert.AreEqual(3, r.Listener.Ends.Count);
        }

        [Test]
        public void Tick_StepStraddlingTelegraphEnd_StillFiresFirstShotAtItsBoundary()
        {
            Run r = new Run();
            r.Start();

            r.TickAt(FirstShot - 0.05);
            r.TickAt(FirstShot + 0.05);

            Assert.AreEqual(1, r.Listener.Launches.Count);
            Assert.AreEqual(FirstShot, r.Listener.Launches[0].Time, TIME_TOL, "발사 시각은 틱 시각이 아니라 경계다");
        }

        /****************************************
        *        Per-Shot Identity And Damage
        ****************************************/

        [Test]
        public void Launch_EachShotHasItsOwnProjectileIdAndSharesTheCastId()
        {
            Run r = new Run();
            r.Start();

            r.TickRange(0.0, RecoverEnd + 0.1);

            Assert.AreEqual(3, r.Listener.Launches.Count);
            HashSet<int> ids = new HashSet<int>();
            for (int i = 0; i < 3; ++i)
            {
                Assert.AreEqual(r.CastId, r.Listener.Launches[i].CastId);
                Assert.AreNotEqual(r.CastId, r.Listener.Launches[i].ProjectileId);
                ids.Add(r.Listener.Launches[i].ProjectileId);
            }

            Assert.AreEqual(3, ids.Count);
        }

        [Test]
        public void Tick_SameTargetInThePath_IsHitOncePerShotUpToThreeTimesWithOwnActionIds()
        {
            Run r = new Run();
            r.Start();

            r.TickRange(0.0, RecoverEnd + 1.0);

            Assert.AreEqual(3, r.Hits(r.Tracked.CombatId));
            HashSet<int> actions = new HashSet<int>();
            for (int i = 0; i < r.Resolver.Requests.Count; ++i)
            {
                DamageRequest request = r.Resolver.Requests[i];
                Assert.AreEqual(r.CastId, request.CastInstanceId);
                Assert.AreEqual(0.18f * HREF, request.RawDamage, TOLERANCE);
                Assert.IsFalse(request.IsUnblockable, "반격 가능");
                Assert.AreEqual(P05_ID, request.SourceDefinitionId);
                actions.Add(request.ActionInstanceId);
            }

            Assert.AreEqual(3, actions.Count, "발마다 별도 동작 번호");
            Assert.AreEqual(3, r.Listener.Ends.Count);
            for (int i = 0; i < 3; ++i)
            {
                Assert.AreEqual(EBossProjectileEnd.Hit, r.Listener.Ends[i].Kind);
            }
        }

        [Test]
        public void Tick_OneShotEnds_OthersKeepFlying()
        {
            Run r = new Run();
            r.Tracked.Position = new Vector3(0f, 0f, 3f);
            r.Start();

            r.TickRange(0.0, 0.85);
            r.TickAt(FirstShot);
            Assert.AreEqual(1, r.Manager.ActiveCount);
            r.TickAt(SecondShot + 0.1);

            Assert.AreEqual(1, r.Listener.Ends.Count, "첫 발만 3m 앞 대상에 닿았다");
            Assert.AreEqual(r.Listener.Launches[0].ProjectileId, r.Listener.Ends[0].ProjectileId);
            Assert.AreEqual(1, r.Manager.ActiveCount, "둘째 발은 계속 난다");
        }

        [Test]
        public void Launch_OriginFollowsBossAtEachShotAndPathLengthIsMeasuredPerShot()
        {
            Run r = new Run();
            r.Tracked.Position = new Vector3(0f, 0f, 200f);
            r.Start();

            r.TickRange(0.0, 0.85);
            r.TickAt(FirstShot);
            r.Self.Position = new Vector3(0f, 0f, 1f);
            r.Wall.Distance = 12f;
            r.TickAt(SecondShot);
            r.Self.Position = new Vector3(0f, 0f, 2f);
            r.Wall.Distance = 20f;
            r.TickAt(ThirdShot);

            Assert.AreEqual(Vector3.zero, r.Listener.Launches[0].Origin);
            Assert.AreEqual(new Vector3(0f, 0f, 1f), r.Listener.Launches[1].Origin);
            Assert.AreEqual(new Vector3(0f, 0f, 2f), r.Listener.Launches[2].Origin);
            Assert.AreEqual(35f, r.Listener.Launches[0].PathLength, TOLERANCE);
            Assert.AreEqual(12f, r.Listener.Launches[1].PathLength, TOLERANCE);
            Assert.AreEqual(20f, r.Listener.Launches[2].PathLength, TOLERANCE);
        }

        /****************************************
        *      Cancel And Flight After Recovery
        ****************************************/

        [TestCase(EBossInterruptReason.Death)]
        [TestCase(EBossInterruptReason.CombatEnd)]
        [TestCase(EBossInterruptReason.PhaseTransition)]
        public void Cancel_MidSequence_StopsRemainingShotsAndClearsFlyingOnes(EBossInterruptReason reason)
        {
            Run r = new Run();
            r.Tracked.Position = new Vector3(0f, 0f, 200f);
            r.Start();
            r.TickRange(0.0, 0.85);
            r.TickAt(FirstShot);
            r.TickAt(SecondShot);
            Assert.AreEqual(2, r.Listener.Launches.Count);

            // BossBrain은 게이트를 먼저 닫고 공격과 지속물을 함께 정리한다
            r.Gate.CloseForGood();
            r.Fsm.Cancel(reason);
            r.Manager.Cancel(reason, CombatIds.NONE);
            r.TickAt(ThirdShot);
            r.TickAt(100.0);

            Assert.AreEqual(2, r.Listener.Launches.Count, "남은 예약은 나가지 않는다");
            Assert.AreEqual(0, r.Manager.ActiveCount);
            Assert.AreEqual(2, r.Listener.Ends.Count);
            Assert.AreEqual(EBossProjectileEnd.Cancelled, r.Listener.Ends[0].Kind);
            Assert.AreEqual(0, r.Listener.TelegraphCancels, "발사한 뒤에는 예고 취소 알림이 없다");
            Assert.AreEqual(0, r.Resolver.Requests.Count);
        }

        [Test]
        public void Cancel_BeforeFirstShot_NotifiesTelegraphCancelAndNeverFires()
        {
            Run r = new Run();
            r.Start();
            r.TickRange(0.0, 0.5);

            r.Fsm.Cancel(EBossInterruptReason.PhaseTransition);
            r.Manager.Cancel(EBossInterruptReason.PhaseTransition, CombatIds.NONE);
            r.TickAt(100.0);

            Assert.AreEqual(1, r.Listener.TelegraphCancels);
            Assert.AreEqual(0, r.Listener.Launches.Count);
        }

        [Test]
        public void DirectControlAndFinish_DuringRecoveryKeepFlyingProjectiles()
        {
            Run r = new Run();
            r.Tracked.Position = new Vector3(0f, 0f, 200f);
            r.Start();
            r.TickRange(0.0, ExecuteEnd + 0.05);
            Assert.AreEqual(EBossAttackState.Recover, r.Fsm.State);
            Assert.AreEqual(3, r.Manager.ActiveCount);

            r.Fsm.Cancel(EBossInterruptReason.DirectControl);
            r.Manager.Cancel(EBossInterruptReason.DirectControl, r.CastId);
            Assert.AreEqual(3, r.Manager.ActiveCount, "직접 제어는 이미 쏜 투사체를 지우지 않는다");
            Assert.AreEqual(EBossAttackState.Ready, r.Fsm.State);

            Run finished = new Run();
            finished.Tracked.Position = new Vector3(0f, 0f, 200f);
            finished.Start();
            finished.TickRange(0.0, RecoverEnd + 0.1);
            Assert.AreEqual(EBossAttackState.Ready, finished.Fsm.State);
            Assert.AreEqual(3, finished.Manager.ActiveCount, "정상 종료도 비행 중인 투사체를 지우지 않는다");
        }

        [Test]
        public void Control_OnlyRecoveryWindowAllowsDirectControl()
        {
            Run r = new Run();
            r.Start();

            r.TickRange(0.0, 0.85);
            Assert.IsFalse(r.Fsm.IsControlWindowOpen(0.85), "예고 중 없음");
            r.TickAt(FirstShot);
            r.TickAt(SecondShot);
            Assert.IsFalse(r.Fsm.IsControlWindowOpen(SecondShot), "발사 중 없음");
            r.TickAt(ExecuteEnd + 0.01);
            Assert.IsFalse(r.Fsm.IsControlWindowOpen(ExecuteEnd - 0.01), "마지막 발이 나가는 실행 구간에는 닫혀 있다");
            Assert.IsTrue(r.Fsm.IsControlWindowOpen(ExecuteEnd), "후딜 시작 순간부터 열린다");
            Assert.IsTrue(r.Fsm.IsControlWindowOpen(ExecuteEnd + 0.01), "회복 중 허용");
            Assert.IsTrue(r.Fsm.IsControlWindowOpen(RecoverEnd - 0.001), "후딜이 끝나기 직전까지 열려 있다");
            r.TickAt(RecoverEnd);
            Assert.IsFalse(r.Fsm.IsControlWindowOpen(RecoverEnd), "후딜이 끝난 뒤에는 닫혀 있다");
        }

        [Test]
        public void Manager_DuplicateLaunchOfTheSameShot_IsRejected()
        {
            RadiusQuery query = new RadiusQuery();
            RecordingListener listener = new RecordingListener();
            BossProjectileManager manager = new BossProjectileManager(query, null, listener);
            BossDamageGate gate = new BossDamageGate(new FakeDamageResolver());
            gate.Open();
            BossProjectileSpec spec = new BossProjectileSpec(5, 6, 1, P05_ID, Vector3.zero, Vector3.forward, 10f, 35f, 1f, 1f,
                EDamageFlags.None, 0.0, gate);

            Assert.IsTrue(manager.TryLaunch(in spec));
            Assert.IsFalse(manager.TryLaunch(in spec));
            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(1, listener.Launches.Count);
        }

        /****************************************
        *   Brain: P05 And P06, Selection Flow
        ****************************************/

        private sealed class BrainRig
        {
            public FakeCombatTarget   Self    = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Vector3.zero };
            public FakeCombatTarget   Player  = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 20f) };
            public RadiusQuery        Query   = new RadiusQuery();
            public FakeDamageResolver Damage  = new FakeDamageResolver();
            public FakeBossVitals     Vitals  = new FakeBossVitals();
            public FakeBossRandom     Random  = new FakeBossRandom();
            public RecordingListener  Listener = new RecordingListener();
            public BossProjectileManager Projectiles;
            public BossBrain          Brain;
            public List<string>       Used    = new List<string>();

            public BrainRig(Vector3 playerPosition)
            {
                Player.Position = playerPosition;
                Query.Targets.Add(Player);
                Projectiles = new BossProjectileManager(Query, null, Listener);

                Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
                {
                    Catalog  = new BossPatternCatalogSource(LoadCatalog()),
                    Self     = Self,
                    Vitals   = Vitals,
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = new ExcludingPatternFactory(new WolbaekPatternFactory(HREF, projectiles: Projectiles, projectileListener: Listener),
                        BossPatternCatalogSO.WOLBAEK_P2_P03_ID),
                    Effects  = Projectiles,
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

            // 새 공격이 시작될 때마다 패턴 ID를 기록하며 지정한 시각까지 진행한다
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
        public void Brain_FarTarget_SelectsP05ThenRequeriesQuietlyBecauseP06IsZeroAndRecentIsZero()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 20f));
            r.TransitionAndHandoff();

            r.Run(0.5, 30.0);

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, r.Used.Count, "원거리 P06은 0이고 직전 P05도 0이라 후보가 비어 재조회만 한다");
            Assert.AreEqual(P05_ID, r.Used[0]);
            Assert.AreEqual(EBossFlowState.Combat, r.Brain.FlowState);
            Assert.AreEqual(EBossPhaseState.Active, r.Brain.PhaseState);
        }

        [Test]
        public void Brain_NearTarget_RecentMultiplierAndFlowAlternatePatterns()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 3f));
            r.TransitionAndHandoff();
            r.Random.Roll = 0f; // 가중치가 있는 첫 후보부터 뽑는다, 후보 순서는 카탈로그 순서(P05가 P06 앞)

            r.Run(0.5, 12.0);

            Assert.GreaterOrEqual(r.Used.Count, 3);
            Assert.AreEqual(P05_ID, r.Used[0]);
            Assert.AreEqual(BossPatternCatalogSO.WOLBAEK_P2_P06_ID, r.Used[1], "직전 P05는 0이라 남은 후보는 P06뿐이다");
            Assert.AreEqual(P05_ID, r.Used[2], "2회 전 사용 0.3배라 P05가 다시 후보가 된다");
        }

        [Test]
        public void Brain_HealthCrossing25DuringSequence_KeepsThreeShotsAndTiming()
        {
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 200f));
            r.TransitionAndHandoff();

            double started = -1.0;
            for (double t = 0.5; t < 10.0; t += 0.25)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                }

                if ((started >= 0.0) && (t >= (started + 0.5)))
                {
                    r.Vitals.HealthRate = 0.2f;
                }
            }

            Assert.GreaterOrEqual(started, 0.0);
            Assert.AreEqual(3, r.Listener.Launches.Count);
            Assert.AreEqual(started + FirstShot, r.Listener.Launches[0].Time, TIME_TOL);
            Assert.AreEqual(started + SecondShot, r.Listener.Launches[1].Time, TIME_TOL);
            Assert.AreEqual(started + ThirdShot, r.Listener.Launches[2].Time, TIME_TOL);
        }

        [Test]
        public void Brain_DeathOrTransitionMidSequence_ClearsReservationsAndFlyingProjectiles()
        {
            FakeDeathPolicy death = new FakeDeathPolicy { Confirm = true };
            BrainRig r = new BrainRig(new Vector3(0f, 0f, 200f));
            r.Brain = new BossBrain(WOLBAEK_ID, new BossBrainDependencies
            {
                Catalog = new BossPatternCatalogSource(LoadCatalog()), Self = r.Self, Vitals = r.Vitals, Targets = r.Query,
                Damage = r.Damage,
                Patterns = new ExcludingPatternFactory(new WolbaekPatternFactory(HREF, projectiles: r.Projectiles, projectileListener: r.Listener),
                    BossPatternCatalogSO.WOLBAEK_P2_P03_ID),
                Effects = r.Projectiles, Control = new FakeControlSource(), Random = r.Random, DeathPolicy = death
            });
            r.TransitionAndHandoff();

            double started = -1.0;
            for (double t = 0.5; t < 6.0; t += 0.25)
            {
                r.Brain.Tick(t);
                if ((started < 0.0) && (r.Brain.AttackState != EBossAttackState.Ready))
                {
                    started = t;
                }

                if ((started >= 0.0) && (t >= (started + FirstShot + 0.35)))
                {
                    r.Vitals.HealthRate = 0f;
                    r.Vitals.IsHealthZero = true;
                    r.Brain.Tick(t + 0.25);
                    break;
                }
            }

            int launched = r.Listener.Launches.Count;
            r.Brain.Tick(100.0);

            Assert.AreEqual(EBossFlowState.Defeated, r.Brain.FlowState);
            Assert.AreEqual(0, r.Projectiles.ActiveCount);
            Assert.AreEqual(launched, r.Listener.Launches.Count, "죽은 뒤에는 새 발이 나가지 않는다");
            Assert.AreEqual(0, r.Damage.Requests.Count);
        }
    }
}
