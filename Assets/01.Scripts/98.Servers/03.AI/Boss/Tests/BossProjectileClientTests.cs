// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 투사체 클라이언트 모델(BossProjectileTrack)과 예고 모델의 전이 규칙을 확인하고, 실제 서버 패턴과 지속물 관리자가 내는 알림을
    // 그대로 모델에 넘겨 표시 경로와 서버 충돌 결과가 같은지 비교한다. 이 테스트는 Mirror와 실제 서버, 화면 재생을 거치지 않는다
    public class BossProjectileClientTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    BOSS_ID    = 1;
        private const float  SPEED      = 10f;
        private const float  WIDTH      = 1f;
        private const float  TOLERANCE  = 0.002f;
        private const float  ANGLE_TOL  = 0.05f;
        private const float  STEP       = 0.01f;
        private const float  TEST_HREF  = 3541f;
        private const string P04_ID     = "wolbaek_p04";

        private static readonly Vector3 Origin = new Vector3(3f, 0f, -2f);

        /****************************************
        *               Helpers
        ****************************************/

        private static BossProjectileStart Shot(int id = 7, float pathLength = 35f, double launch = 10.0)
        {
            return new BossProjectileStart(BOSS_ID, id, Origin, Vector3.forward, SPEED, WIDTH, pathLength, launch);
        }

        private static float AimDegrees(Vector3 aim)
        {
            return Vector3.SignedAngle(Vector3.forward, aim, Vector3.up);
        }

        /****************************************
        *          Projectile Track Model
        ****************************************/

        [Test]
        public void Distance_ConstantSpeedFromLaunchTime_ClampedAtPathLength()
        {
            BossProjectileTrack track = new BossProjectileTrack(Shot(pathLength: 12f));

            Assert.AreEqual(0f, track.GetDistance(9.0), 0.0001f);
            Assert.AreEqual(0f, track.GetDistance(10.0), 0.0001f);
            Assert.AreEqual(5f, track.GetDistance(10.5), 0.0001f);
            Assert.AreEqual(12f, track.GetDistance(11.2), 0.0001f);
            Assert.AreEqual(12f, track.GetDistance(999.0), 0.0001f, "벽 앞 경로 길이를 넘어 날지 않는다");
            Assert.AreEqual(1.2f, track.FlightSeconds, 0.0001f);
        }

        [Test]
        public void Position_FollowsServerDirectionFromServerOrigin()
        {
            BossProjectileTrack track = new BossProjectileTrack(Shot());

            Vector3 position = track.GetPosition(10.3);

            Assert.AreEqual(Origin.x, position.x, 0.0001f);
            Assert.AreEqual(Origin.z + 3f, position.z, 0.0001f);
        }

        [Test]
        public void HasReachedEnd_OnlyAtPathLengthOrWhenServerEnded()
        {
            BossProjectileTrack track = new BossProjectileTrack(Shot(pathLength: 12f));

            Assert.IsFalse(track.HasReachedEnd(11.0));
            Assert.IsTrue(track.HasReachedEnd(11.2));
        }

        [Test]
        public void End_ServerHit_SnapsToServerPositionAndFreezes()
        {
            BossProjectileTrack track = new BossProjectileTrack(Shot());

            Assert.IsTrue(track.TryEnd(EBossProjectileFate.Hit, Origin + (Vector3.forward * 4f)));
            Assert.IsFalse(track.TryEnd(EBossProjectileFate.Wall, Origin + (Vector3.forward * 9f)));

            Assert.IsTrue(track.IsEnded);
            Assert.AreEqual(EBossProjectileFate.Hit, track.Fate);
            Assert.AreEqual(4f, track.GetDistance(10.1), 0.0001f);
            Assert.AreEqual(4f, track.GetDistance(99.0), 0.0001f);
        }

        [Test]
        public void End_FlyingIsNotAnEnd_AndPositionBeyondPathIsClamped()
        {
            BossProjectileTrack track = new BossProjectileTrack(Shot(pathLength: 8f));

            Assert.IsFalse(track.TryEnd(EBossProjectileFate.Flying, Origin));
            Assert.IsTrue(track.TryEnd(EBossProjectileFate.Wall, Origin + (Vector3.forward * 50f)));

            Assert.AreEqual(8f, track.GetDistance(99.0), 0.0001f);
        }

        [Test]
        public void Cancel_EndsAtOnce_AndNothingReopensIt()
        {
            BossProjectileTrack track = new BossProjectileTrack(Shot());

            Assert.IsTrue(track.TryCancel());

            Assert.IsFalse(track.TryCancel());
            Assert.IsFalse(track.TryEnd(EBossProjectileFate.Hit, Origin));
            Assert.AreEqual(EBossProjectileFate.Cancelled, track.Fate);
        }

        [Test]
        public void Snapshot_RoundTrip_ContinuesFromSameServerTime()
        {
            BossProjectileTrack original = new BossProjectileTrack(Shot(pathLength: 20f));
            BossProjectileTrack flying = BossProjectileTrack.FromSnapshot(original.ToSnapshot());
            original.TryEnd(EBossProjectileFate.Hit, Origin + (Vector3.forward * 6f));
            BossProjectileTrack ended = BossProjectileTrack.FromSnapshot(original.ToSnapshot());

            Assert.AreEqual(6f, ended.GetDistance(10.3), 0.0001f);
            Assert.AreEqual(3f, flying.GetDistance(10.3), 0.0001f);
            Assert.AreEqual(EBossProjectileFate.Hit, ended.Fate);
            Assert.AreEqual(6f, ended.GetDistance(99.0), 0.0001f);
        }

        // 예고 모델: 투사체 예고는 발사로 마무리되고, 발사 뒤 취소는 이미 확정한 표현을 지우지 않는다
        [Test]
        public void TelegraphCast_ProjectileShape_StrikeOnLaunch_ThenCancelRejected()
        {
            BossTelegraphCast cast = new BossTelegraphCast(BossTelegraphStart.Projectile(BOSS_ID, 7, P04_ID, Origin,
                Vector3.forward, 35f, WIDTH, 10.0, 0.9f));

            Assert.AreEqual(EBossTelegraphShape.Projectile, cast.Shape);
            Assert.IsTrue(cast.TryStrike(10.9));
            Assert.IsFalse(cast.TryStrike(11.0));
            Assert.IsFalse(cast.TryCancel());
            Assert.IsTrue(cast.TryFinish());
        }

        /****************************************
        *      Server Pattern To Client Model
        ****************************************/

        // 네트워크 수신부가 할 변환의 예시다. 서버 알림에 서버 시각을 붙여 모델에 넘긴다
        private sealed class ClientBridge : IBossProjectileTelegraphListener, IBossProjectileListener
        {
            public double Now;

            public BossTelegraphCast  Cast;
            public BossProjectileTrack Track;
            public int                EndCount;

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
                float telegraphSeconds)
            {
                Cast = new BossTelegraphCast(BossTelegraphStart.Projectile(BOSS_ID, castInstanceId, P04_ID, origin, direction,
                    length, width, Now, telegraphSeconds));
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length)
            {
                Cast.TryUpdateAim(direction, length, Now);
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                Cast.TryLockDirection(direction, length);
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Cast.TryCancel();
            }

            public void OnProjectileLaunched(int castInstanceId, Vector3 origin, Vector3 direction, float speed, float width,
                float pathLength, double launchServerTime)
            {
                Track = new BossProjectileTrack(new BossProjectileStart(BOSS_ID, castInstanceId, origin, direction, speed, width,
                    pathLength, launchServerTime));
                Cast.TryStrike(launchServerTime);
            }

            public void OnProjectileEnded(int castInstanceId, EBossProjectileEnd end, Vector3 position, int hitTargetId)
            {
                ++EndCount;
                EBossProjectileFate fate = EBossProjectileFate.Cancelled;
                switch (end)
                {
                    case EBossProjectileEnd.Hit:
                        fate = EBossProjectileFate.Hit;
                        break;
                    case EBossProjectileEnd.Wall:
                        fate = EBossProjectileFate.Wall;
                        break;
                    case EBossProjectileEnd.Range:
                        fate = EBossProjectileFate.Range;
                        break;
                }

                if (fate == EBossProjectileFate.Cancelled)
                {
                    Track.TryCancel();
                }
                else
                {
                    Track.TryEnd(fate, position);
                }
            }
        }

        private sealed class GridQuery : ICombatTargetQuery
        {
            public List<FakeCombatTarget> Targets { get; } = new List<FakeCombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                results.AddRange(Targets);
                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    Vector3 offset = Targets[i].Position - center;
                    offset.y = 0f;
                    if ((Targets[i].LifeState == EParticipantLifeState.Alive) && (Targets[i].Faction == faction)
                        && (includeSummons || !Targets[i].IsSummon) && (offset.sqrMagnitude <= (radius * radius)))
                    {
                        results.Add(Targets[i]);
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

        private sealed class PlaneWall : IBossWallQuery
        {
            public float WallZ = float.PositiveInfinity;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                if ((float.IsPositiveInfinity(WallZ)) || (direction.z <= 0.0001f))
                {
                    return maxDistance;
                }

                return Mathf.Clamp((WallZ - bodyRadius - origin.z) / direction.z, 0f, maxDistance);
            }
        }

        private sealed class Run
        {
            public FakeCombatTarget   Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin };
            public FakeCombatTarget   Tracked  { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player };
            public GridQuery          Query    { get; } = new GridQuery();
            public FakeDamageResolver Resolver { get; } = new FakeDamageResolver();
            public ClientBridge       Bridge   { get; } = new ClientBridge();
            public PlaneWall          Wall     { get; } = new PlaneWall();
            public BossAttackFsm      Fsm      { get; } = new BossAttackFsm();
            public BossDamageGate     Gate     { get; }
            public BossProjectileManager Manager { get; }
            public BossProjectilePattern Pattern { get; }
            public PatternDefinition  Definition { get; }

            public Run(float trackedDegrees)
            {
                Tracked.Position = Origin + (new Vector3(Mathf.Sin(trackedDegrees * Mathf.Deg2Rad), 0f, Mathf.Cos(trackedDegrees * Mathf.Deg2Rad)) * 4f);
                Query.Targets.Add(Tracked);
                Gate = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = new PatternDefinition(P04_ID, BossTestFixtures.BOSS_ID, 1, 20f,
                    new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                    0.9f, 0.45f, 120f, 0f, 0.5f, EnemyCatalogSO.AttackShapeSpec.Projectile(35f, WIDTH, SPEED), 0.18f, true);
                Manager = new BossProjectileManager(Query, Wall, Bridge);
                Pattern = new BossProjectilePattern(TEST_HREF, Manager, Bridge);
            }

            public void FillGrid()
            {
                int id = 100;
                for (int i = 0; i < 24; ++i)
                {
                    for (int j = 0; j < 36; ++j)
                    {
                        Query.Targets.Add(new FakeCombatTarget
                        {
                            CombatId = id, Faction = ECombatFaction.Player,
                            Position = Origin + new Vector3(-6.03f + (0.531f * i), 0f, -1.37f + (1.013f * j))
                        });
                        ++id;
                    }
                }
            }

            public void Start(double serverTime)
            {
                Bridge.Now = serverTime;
                BossPatternContext context = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }

            public void TickTo(double from, double to)
            {
                int count = (int)Math.Round((to - from) / STEP);
                for (int i = 1; i <= count; ++i)
                {
                    double time = from + (i * (double)STEP);
                    Bridge.Now = time;
                    Fsm.Tick(time);
                    Manager.Tick(time);
                }
            }
        }

        // 서버가 맞힌 대상은 표시한 경로 직사각형 안에서 경로상 가장 가까운(같은 거리면 CombatId가 작은) 대상이다
        private static void AssertServerHitIsNearestInsideDisplayedPath(Run run)
        {
            BossTelegraphCast cast = run.Bridge.Cast;
            BossProjectileTrack track = run.Bridge.Track;

            int expected = CombatIds.NONE;
            float bestAlong = float.MaxValue;
            for (int i = 0; i < run.Query.Targets.Count; ++i)
            {
                FakeCombatTarget target = run.Query.Targets[i];
                if (!BossTelegraphRules.IsInside(cast.Shape, cast.Origin, cast.Direction, track.PathLength, cast.Width, 0f, target.Position))
                {
                    continue;
                }

                Vector3 offset = target.Position - cast.Origin;
                float along = (offset.x * cast.Direction.x) + (offset.z * cast.Direction.z);
                if ((along < (bestAlong - 0.001f)) || ((Mathf.Abs(along - bestAlong) <= 0.001f) && (target.CombatId < expected)))
                {
                    bestAlong = along;
                    expected  = target.CombatId;
                }
            }

            if (expected == CombatIds.NONE)
            {
                Assert.AreEqual(0, run.Resolver.Requests.Count);
                return;
            }

            Assert.AreEqual(1, run.Resolver.Requests.Count);
            Assert.AreEqual(expected, run.Resolver.Requests[0].HitTargetId);
        }

        [Test]
        public void ServerOpenField_DisplayedPathAndNearestHitAgree_AndClientDistanceMatchesServerSpeed()
        {
            Run run = new Run(25f);
            run.FillGrid();
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 0.9 + 0.5 + 1.0);

            BossTelegraphCast cast = run.Bridge.Cast;
            BossProjectileTrack track = run.Bridge.Track;
            Assert.AreEqual(EBossTelegraphShape.Projectile, cast.Shape);
            Assert.AreEqual(WIDTH, cast.Width);
            Assert.IsTrue(cast.IsDirectionLocked);
            Assert.AreEqual(25f, AimDegrees(cast.Direction), ANGLE_TOL);
            Assert.AreEqual(35f, track.PathLength, TOLERANCE);
            Assert.AreEqual(cast.Length, track.PathLength, TOLERANCE, "예고 길이와 비행 경로 길이가 같다");
            Assert.AreEqual(0f, Vector3.Angle(cast.Direction, track.Direction), ANGLE_TOL);
            Assert.AreEqual(SPEED, track.Speed);
            Assert.AreEqual(EBossTelegraphPhase.Execute, cast.Phase);
            Assert.AreEqual(5f, new BossProjectileTrack(track.Start).GetDistance(track.LaunchServerTime + 0.5), TOLERANCE);
            Assert.AreEqual(1, run.Bridge.EndCount);
            Assert.AreEqual(EBossProjectileFate.Hit, track.Fate);
            AssertServerHitIsNearestInsideDisplayedPath(run);
        }

        // 벽이 가까우면 서버가 보낸 줄어든 경로가 예고이자 비행이고, 벽 뒤 대상은 맞지 않는다
        [Test]
        public void ServerNearWall_DisplayedPathEqualsFlightAndNothingBehindWallIsHit()
        {
            Run run = new Run(0f);
            run.Wall.WallZ = Origin.z + 9f;
            run.FillGrid();
            run.Tracked.LifeState = EParticipantLifeState.Dead;
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 0.9 + 0.5 + 1.5);

            BossTelegraphCast cast = run.Bridge.Cast;
            BossProjectileTrack track = run.Bridge.Track;
            Assert.AreEqual(8.5f, cast.Length, TOLERANCE);
            Assert.AreEqual(8.5f, track.PathLength, TOLERANCE);
            AssertServerHitIsNearestInsideDisplayedPath(run);
            if (run.Resolver.Requests.Count == 0)
            {
                Assert.AreEqual(EBossProjectileFate.Wall, track.Fate);
            }

            Assert.LessOrEqual(track.GetDistance(99.0), 8.5f + TOLERANCE, "클라이언트가 벽을 지나 날지 않는다");
        }

        [Test]
        public void ServerNoTarget_RangeEndAtMaxRangeAndClientStopsThere()
        {
            Run run = new Run(0f);
            run.Tracked.LifeState = EParticipantLifeState.Dead;
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 0.9 + 3.6);

            BossProjectileTrack track = run.Bridge.Track;
            Assert.AreEqual(EBossProjectileFate.Range, track.Fate);
            Assert.AreEqual(35f, track.GetDistance(99.0), TOLERANCE);
            Assert.AreEqual(0, run.Resolver.Requests.Count);
        }

        [Test]
        public void ServerCancelBeforeLaunch_TelegraphCancelledAndNoProjectileModel()
        {
            Run run = new Run(0f);
            run.Start(10.0);
            run.TickTo(10.0, 10.3);

            run.Fsm.Cancel(EBossInterruptReason.PhaseTransition);

            Assert.AreEqual(EBossTelegraphPhase.Cancelled, run.Bridge.Cast.Phase);
            Assert.IsNull(run.Bridge.Track);
        }

        [Test]
        public void ServerManagerCancel_InFlight_ClientTrackCancelled_AndPatternFinishIsSeparate()
        {
            Run run = new Run(0f);
            run.Tracked.LifeState = EParticipantLifeState.Dead;
            run.Start(10.0);
            run.TickTo(10.0, 10.0 + 0.9 + 0.6);
            Assert.AreEqual(EBossTelegraphPhase.Execute, run.Bridge.Cast.Phase);
            Assert.IsFalse(run.Bridge.Track.IsEnded);

            run.Manager.Cancel(EBossInterruptReason.Death, CombatIds.NONE);

            Assert.AreEqual(EBossProjectileFate.Cancelled, run.Bridge.Track.Fate);
            Assert.IsTrue(run.Bridge.Cast.TryFinish(), "패턴 종료는 투사체 소멸과 별개다");
        }
    }
}
