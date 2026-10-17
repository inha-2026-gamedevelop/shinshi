// System
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class BossEffectLedgerTests
    {
        // 늦은 접속, 재접속 복원 기록이 진행 중인 것만 남기고 끝난 것은 지우는지, 스폰 초기 상태로 그대로 오가는지 본다

        /****************************************
        *                Fields
        ****************************************/

        private const int    BOSS_ID       = 3;
        private const int    CAST_ID       = 11;
        private const int    OTHER_ID      = 12;
        private const int    AREA_ID       = 13;
        private const int    LATE_ROUND    = 2;
        private const string PATTERN_ID    = "wolbaek_p01";
        private const float  LENGTH        = 8f;
        private const float  WIDTH         = 4f;
        private const float  TELEGRAPH     = 1.2f;
        private const double START_TIME    = 10.0;
        private const double EXECUTE_TIME  = 11.2;
        private const float  DASH_SECONDS  = 0.45f;
        private const float  DASH_SPEED    = 17.7f;
        private const float  STOP_DISTANCE = 5f;
        private const double STOP_TIME     = 11.5;
        private const float  SIDE_OFFSET   = 1f; // m, 옆으로 비껴 멈춰도 진행 방향 거리만 센다
        private const float  SPEED         = 12f;
        private const float  RADIUS        = 3f;
        private const float  DURATION      = 6f;
        private const float  TICK_INTERVAL = 1f;
        private const double ROUND_TIME    = 12.0;
        private const float  FLOAT_EPSILON = 0.0001f;
        private const double TIME_EPSILON  = 0.000001;
        private const int    BEAM_ID       = 14;
        private const int    STAR_ID       = 15;
        private const int    ZONE_ID       = 16;
        private const float  ARENA_RADIUS  = 20f;
        private const float  BEAM_OFFSET   = 2f;
        private const float  RING_RADIUS   = 8f;
        private const int    VERTEX_COUNT  = 5;
        private const int    CONNECT_STEP  = 2;
        private const float  TURN_RATE     = -30f; // 도/초, 왼쪽 회전
        private const float  ARC_ANGLE     = 45f;
        private const int    BARRIER_ID    = 17;
        private const float  INNER_RADIUS  = 17.3f;
        private const double COMPLETE_TIME = 12.7;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Telegraph_StruckThenFinished_ClearsCast()
        {
            var ledger = new BossEffectLedger();
            BossTelegraphStart start = MakeLine(CAST_ID);

            ledger.OnTelegraphStarted(in start);
            ledger.OnAimChanged(CAST_ID, Vector3.right, LENGTH, START_TIME);
            ledger.OnDirectionLocked(CAST_ID, Vector3.right, LENGTH);
            ledger.OnStruck(CAST_ID, EXECUTE_TIME);

            Assert.IsTrue(ledger.HasCast);
            Assert.AreEqual(EBossTelegraphPhase.Execute, ledger.Cast.Phase);
            Assert.AreEqual(EXECUTE_TIME, ledger.Cast.ExecuteServerTime, TIME_EPSILON);
            Assert.IsTrue(ledger.Cast.IsDirectionLocked);
            Assert.AreEqual(Vector3.right, ledger.Cast.Direction);

            ledger.OnFinished(CAST_ID);

            Assert.IsFalse(ledger.HasCast, "끝난 공격은 복원에 넣지 않는다");
        }

        [Test]
        public void Telegraph_EventsForOtherCast_Ignored()
        {
            var ledger = new BossEffectLedger();
            BossTelegraphStart start = MakeLine(CAST_ID);
            ledger.OnTelegraphStarted(in start);

            ledger.OnAimChanged(OTHER_ID, Vector3.right, LENGTH, START_TIME);
            ledger.OnCancelled(OTHER_ID);

            Assert.IsTrue(ledger.HasCast);
            Assert.AreEqual(Vector3.forward, ledger.Cast.Direction);
        }

        [Test]
        public void Dash_Stopped_RecordsDistanceAlongDirection()
        {
            var ledger = new BossEffectLedger();
            BossTelegraphStart start = BossTelegraphStart.Dash(BOSS_ID, CAST_ID, PATTERN_ID, Vector3.zero, Vector3.forward,
                LENGTH, WIDTH, START_TIME, TELEGRAPH);

            ledger.OnTelegraphStarted(in start);
            ledger.OnDashStarted(CAST_ID, LENGTH, DASH_SECONDS, DASH_SPEED, EXECUTE_TIME);

            Assert.AreEqual(EBossTelegraphPhase.Execute, ledger.Cast.Phase);
            Assert.Less(ledger.Cast.DashStoppedDistance, 0f, "가는 중이면 음수다");
            Assert.IsTrue(double.IsNaN(ledger.Cast.DashStoppedServerTime), "가는 중이면 정지 시각이 없다");

            ledger.OnDashStopped(CAST_ID, new Vector3(SIDE_OFFSET, 0f, STOP_DISTANCE), true, STOP_TIME);

            Assert.AreEqual(STOP_DISTANCE, ledger.Cast.DashStoppedDistance, FLOAT_EPSILON);
            Assert.IsTrue(ledger.Cast.DashStoppedByWall);
            Assert.AreEqual(LENGTH, ledger.Cast.DashDistance, FLOAT_EPSILON);
            Assert.AreEqual(STOP_TIME, ledger.Cast.DashStoppedServerTime, TIME_EPSILON);
        }

        [Test]
        public void WriteRead_StoppedDash_KeepsStopServerTime()
        {
            var source = new BossEffectLedger();
            BossTelegraphStart start = BossTelegraphStart.Dash(BOSS_ID, CAST_ID, PATTERN_ID, Vector3.zero, Vector3.forward,
                LENGTH, WIDTH, START_TIME, TELEGRAPH);
            source.OnTelegraphStarted(in start);
            source.OnDashStarted(CAST_ID, LENGTH, DASH_SECONDS, DASH_SPEED, EXECUTE_TIME);
            source.OnDashStopped(CAST_ID, new Vector3(0f, 0f, STOP_DISTANCE), true, STOP_TIME);

            var target = new BossEffectLedger();
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                source.Write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    target.Read(reader);
                    Assert.AreEqual(0, reader.Remaining, "쓴 만큼 정확히 읽는다");
                }
            }

            Assert.AreEqual(STOP_DISTANCE, target.Cast.DashStoppedDistance, FLOAT_EPSILON);
            Assert.AreEqual(STOP_TIME, target.Cast.DashStoppedServerTime, TIME_EPSILON);
        }

        [Test]
        public void Projectile_LaunchEndsTelegraphAndEndRemoves()
        {
            var ledger = new BossEffectLedger();
            BossTelegraphStart telegraph = BossTelegraphStart.Projectile(BOSS_ID, CAST_ID, PATTERN_ID, Vector3.zero,
                Vector3.forward, LENGTH, WIDTH, START_TIME, TELEGRAPH);
            ledger.OnTelegraphStarted(in telegraph);

            BossProjectileStart projectile = MakeProjectile(CAST_ID);
            ledger.OnProjectileLaunched(in projectile);

            Assert.IsFalse(ledger.HasCast, "발사하면 예고는 끝난다");
            Assert.AreEqual(1, ledger.ProjectileCount);
            Assert.AreEqual(EBossProjectileFate.Flying, ledger.GetProjectile(0).Fate);

            ledger.OnProjectileEnded(CAST_ID, EBossProjectileFate.Hit, Vector3.zero);

            Assert.AreEqual(0, ledger.ProjectileCount);
        }

        [Test]
        public void Area_TicksKeepLatestAndEndRemoves()
        {
            var ledger = new BossEffectLedger();
            BossAreaStart area = MakeArea(AREA_ID);
            ledger.OnAreaActivated(in area);

            ledger.OnAreaTicked(AREA_ID, 1, ROUND_TIME);
            ledger.OnAreaTicked(AREA_ID, 0, START_TIME);

            Assert.AreEqual(1, ledger.AreaCount);
            Assert.AreEqual(1, ledger.GetArea(0).LastTickIndex, "늦게 온 이전 회차는 무시한다");
            Assert.AreEqual(ROUND_TIME, ledger.GetArea(0).LastTickServerTime, TIME_EPSILON);

            ledger.OnAreaEnded(AREA_ID, EBossAreaFate.Cancelled);

            Assert.AreEqual(0, ledger.AreaCount);
        }

        [Test]
        public void WriteRead_RoundTripsEverythingInProgress()
        {
            var source = new BossEffectLedger();
            BossTelegraphStart telegraph = MakeLine(CAST_ID);
            source.OnTelegraphStarted(in telegraph);
            source.OnDirectionLocked(CAST_ID, Vector3.left, LENGTH);
            BossProjectileStart projectile = MakeProjectile(OTHER_ID);
            source.OnProjectileLaunched(in projectile);
            BossAreaStart area = MakeArea(AREA_ID);
            source.OnAreaActivated(in area);
            source.OnAreaTicked(AREA_ID, LATE_ROUND, ROUND_TIME);

            var target = new BossEffectLedger();
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                source.Write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    target.Read(reader);
                    Assert.AreEqual(0, reader.Remaining, "쓴 만큼 정확히 읽는다");
                }
            }

            Assert.IsTrue(target.HasCast);
            Assert.AreEqual(CAST_ID, target.Cast.Start.CastId);
            Assert.AreEqual(PATTERN_ID, target.Cast.Start.PatternId);
            Assert.AreEqual(EBossTelegraphShape.Line, target.Cast.Start.Shape);
            Assert.AreEqual(START_TIME, target.Cast.Start.StartServerTime, TIME_EPSILON);
            Assert.AreEqual(Vector3.left, target.Cast.Direction);
            Assert.IsTrue(target.Cast.IsDirectionLocked);

            Assert.AreEqual(1, target.ProjectileCount);
            Assert.AreEqual(OTHER_ID, target.GetProjectile(0).Start.ProjectileId);
            Assert.AreEqual(SPEED, target.GetProjectile(0).Start.Speed, FLOAT_EPSILON);

            Assert.AreEqual(1, target.AreaCount);
            Assert.AreEqual(LATE_ROUND, target.GetArea(0).LastTickIndex);
            Assert.AreEqual(DURATION, target.GetArea(0).Start.DurationSeconds, FLOAT_EPSILON);
        }

        [Test]
        public void Read_CountOverLimit_Throws()
        {
            var ledger = new BossEffectLedger();
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WriteBool(false);
                writer.WriteVarUInt((uint)(Constants.Network.BOSS_RESTORE_MAX_PROJECTILES + 1));
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    Assert.Throws<InvalidDataException>(() => ledger.Read(reader));
                }
            }
        }

        [Test]
        public void Replay_SendsCastProjectilesAndAreasAsRestores()
        {
            var ledger = new BossEffectLedger();
            BossTelegraphStart telegraph = MakeLine(CAST_ID);
            ledger.OnTelegraphStarted(in telegraph);
            BossProjectileStart projectile = MakeProjectile(OTHER_ID);
            ledger.OnProjectileLaunched(in projectile);
            BossAreaStart area = MakeArea(AREA_ID);
            ledger.OnAreaActivated(in area);
            var listener = new RecordingStateListener();

            ledger.Replay(listener);

            CollectionAssert.AreEqual(new[] { CAST_ID }, listener.RestoredCasts);
            CollectionAssert.AreEqual(new[] { OTHER_ID }, listener.RestoredProjectiles);
            CollectionAssert.AreEqual(new[] { AREA_ID }, listener.RestoredAreas);
        }

        [Test]
        public void Beam_TelegraphActivateTick_KeepsProgressAndEndRemoves()
        {
            var ledger = new BossEffectLedger();
            BossBeamStart beam = MakeBeam(BEAM_ID);
            ledger.OnBeamTelegraphStarted(in beam);

            Assert.AreEqual(1, ledger.BeamCount, "예고 중인 빔도 복원에 넣는다");
            Assert.IsFalse(ledger.GetBeam(0).IsActivated);

            ledger.OnBeamActivated(BEAM_ID, EXECUTE_TIME);
            ledger.OnBeamTicked(BEAM_ID, 1, ROUND_TIME);
            ledger.OnBeamTicked(BEAM_ID, 0, EXECUTE_TIME);

            Assert.IsTrue(ledger.GetBeam(0).IsActivated);
            Assert.AreEqual(EXECUTE_TIME, ledger.GetBeam(0).ActivateServerTime, TIME_EPSILON);
            Assert.AreEqual(1, ledger.GetBeam(0).LastTickIndex, "늦게 온 이전 회차는 무시한다");

            ledger.OnBeamEnded(BEAM_ID, EBossAreaFate.Expired);

            Assert.AreEqual(0, ledger.BeamCount);
        }

        [Test]
        public void Star_CancelledBeforeActivation_Removes()
        {
            var ledger = new BossEffectLedger();
            BossStarStart star = MakeStar(STAR_ID);
            ledger.OnStarTelegraphStarted(in star);

            Assert.AreEqual(1, ledger.StarCount);

            ledger.OnStarCancelled(CAST_ID, STAR_ID);

            Assert.AreEqual(0, ledger.StarCount, "활성 전에 취소된 별형 빔은 복원에 넣지 않는다");
        }

        [Test]
        public void WriteRead_RoundTripsBeamsStarsAndGroupId()
        {
            var source = new BossEffectLedger();
            BossTelegraphStart zone = BossTelegraphStart.Fan(BOSS_ID, ZONE_ID, PATTERN_ID, Vector3.zero, Vector3.forward,
                RADIUS, ARC_ANGLE, START_TIME, TELEGRAPH, CAST_ID);
            source.OnTelegraphStarted(in zone);
            BossBeamStart beam = MakeBeam(BEAM_ID);
            source.OnBeamTelegraphStarted(in beam);
            source.OnBeamActivated(BEAM_ID, EXECUTE_TIME);
            BossStarStart star = MakeStar(STAR_ID);
            source.OnStarTelegraphStarted(in star);

            var target = new BossEffectLedger();
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                source.Write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    target.Read(reader);
                    Assert.AreEqual(0, reader.Remaining, "쓴 만큼 정확히 읽는다");
                }
            }

            Assert.AreEqual(ZONE_ID, target.Cast.Start.CastId);
            Assert.AreEqual(CAST_ID, target.Cast.Start.GroupId, "묶음 번호도 오간다");

            Assert.AreEqual(1, target.BeamCount);
            BossBeamSnapshot beamCopy = target.GetBeam(0);
            Assert.AreEqual(BEAM_ID, beamCopy.Start.BeamId);
            Assert.AreEqual(CAST_ID, beamCopy.Start.CastId);
            Assert.IsTrue(beamCopy.IsActivated);
            Assert.AreEqual(BEAM_OFFSET, beamCopy.Start.Shape.LateralOffset, FLOAT_EPSILON);
            Assert.AreEqual(ARENA_RADIUS, beamCopy.Start.Shape.ArenaRadius, FLOAT_EPSILON);

            Assert.AreEqual(1, target.StarCount);
            BossStarSnapshot starCopy = target.GetStar(0);
            Assert.AreEqual(STAR_ID, starCopy.Start.StarId);
            Assert.IsFalse(starCopy.IsActivated);
            Assert.AreEqual(VERTEX_COUNT, starCopy.Start.Shape.VertexCount);
            Assert.AreEqual(CONNECT_STEP, starCopy.Start.Shape.ConnectStep);
            Assert.AreEqual(TURN_RATE, starCopy.Start.Shape.SignedDegreesPerSecond, FLOAT_EPSILON);
        }

        [Test]
        public void Replay_SendsBeamsAndStarsAsRestores()
        {
            var ledger = new BossEffectLedger();
            BossBeamStart beam = MakeBeam(BEAM_ID);
            ledger.OnBeamTelegraphStarted(in beam);
            BossStarStart star = MakeStar(STAR_ID);
            ledger.OnStarTelegraphStarted(in star);
            var listener = new RecordingStateListener();

            ledger.Replay(listener);

            CollectionAssert.AreEqual(new[] { BEAM_ID }, listener.RestoredBeams);
            CollectionAssert.AreEqual(new[] { STAR_ID }, listener.RestoredStars);
        }

        [Test]
        public void Barrier_StartedThenCleared_KeepsUntilClearOfSameId()
        {
            var ledger = new BossEffectLedger();
            BossBarrierStart barrier = MakeBarrier(BARRIER_ID);
            ledger.OnBarrierStarted(in barrier);
            ledger.OnBarrierActivated(BARRIER_ID, EXECUTE_TIME);
            ledger.OnBarrierCompleted(BARRIER_ID, COMPLETE_TIME);

            Assert.IsTrue(ledger.HasBarrier, "활성, 완료 뒤에도 정리 전까지 남는다");
            Assert.AreEqual(BARRIER_ID, ledger.Barrier.BarrierId);

            ledger.OnBarrierCleared(OTHER_ID);
            Assert.IsTrue(ledger.HasBarrier, "다른 번호의 정리는 무시한다");

            ledger.OnBarrierCleared(BARRIER_ID);
            Assert.IsFalse(ledger.HasBarrier);
        }

        [Test]
        public void WriteRead_RoundTripsBarrierAndReplaysRestore()
        {
            var source = new BossEffectLedger();
            BossBarrierStart barrier = MakeBarrier(BARRIER_ID);
            source.OnBarrierStarted(in barrier);

            var target = new BossEffectLedger();
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                source.Write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    target.Read(reader);
                    Assert.AreEqual(0, reader.Remaining, "쓴 만큼 정확히 읽는다");
                }
            }

            Assert.IsTrue(target.HasBarrier);
            BossBarrierStart copy = target.Barrier;
            Assert.AreEqual(BOSS_ID, copy.BossId);
            Assert.AreEqual(BARRIER_ID, copy.BarrierId);
            Assert.AreEqual(Vector3.one, copy.Center);
            Assert.AreEqual(ARENA_RADIUS, copy.OuterRadius, FLOAT_EPSILON);
            Assert.AreEqual(INNER_RADIUS, copy.InnerRadius, FLOAT_EPSILON);
            Assert.AreEqual(TICK_INTERVAL, copy.TickInterval, FLOAT_EPSILON);
            Assert.AreEqual(START_TIME, copy.TelegraphServerTime, TIME_EPSILON);
            Assert.AreEqual(EXECUTE_TIME, copy.ActivateServerTime, TIME_EPSILON);
            Assert.AreEqual(COMPLETE_TIME, copy.CompleteServerTime, TIME_EPSILON);

            var listener = new RecordingStateListener();
            target.Replay(listener);

            CollectionAssert.AreEqual(new[] { BARRIER_ID }, listener.RestoredBarriers, "늦게 연결한 수신부도 결계를 복원으로 받는다");
        }

        [Test]
        public void WriteRead_NoBarrier_RoundTripsEmpty()
        {
            var source = new BossEffectLedger();
            BossBarrierStart barrier = MakeBarrier(BARRIER_ID);
            source.OnBarrierStarted(in barrier);
            source.OnBarrierCleared(BARRIER_ID);

            var target = new BossEffectLedger();
            target.OnBarrierStarted(in barrier);
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                source.Write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    target.Read(reader);
                    Assert.AreEqual(0, reader.Remaining);
                }
            }

            Assert.IsFalse(target.HasBarrier, "받은 값으로 바꾸므로 이전 결계가 남지 않는다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static BossBarrierStart MakeBarrier(int barrierId)
        {
            return new BossBarrierStart(BOSS_ID, barrierId, Vector3.one, ARENA_RADIUS, INNER_RADIUS, TICK_INTERVAL, START_TIME,
                EXECUTE_TIME, COMPLETE_TIME);
        }

        private static BossBeamStart MakeBeam(int beamId)
        {
            var shape = new BossBeamShape(Vector3.zero, ARENA_RADIUS, Vector3.forward, BEAM_OFFSET, WIDTH);
            return new BossBeamStart(BOSS_ID, beamId, CAST_ID, in shape, START_TIME, TELEGRAPH, DURATION, TICK_INTERVAL);
        }

        private static BossStarStart MakeStar(int starId)
        {
            var shape = new BossStarShape(Vector3.zero, ARENA_RADIUS, Vector3.forward, RING_RADIUS, VERTEX_COUNT, CONNECT_STEP,
                WIDTH, TURN_RATE);
            return new BossStarStart(BOSS_ID, starId, CAST_ID, in shape, START_TIME, TELEGRAPH, DURATION, TICK_INTERVAL);
        }

        private static BossTelegraphStart MakeLine(int castId)
        {
            return BossTelegraphStart.Line(BOSS_ID, castId, PATTERN_ID, Vector3.zero, Vector3.forward, LENGTH, WIDTH,
                START_TIME, TELEGRAPH);
        }

        private static BossProjectileStart MakeProjectile(int projectileId)
        {
            return new BossProjectileStart(BOSS_ID, projectileId, Vector3.zero, Vector3.forward, SPEED, WIDTH, LENGTH,
                EXECUTE_TIME);
        }

        private static BossAreaStart MakeArea(int areaId)
        {
            return new BossAreaStart(BOSS_ID, areaId, Vector3.one, RADIUS, DURATION, TICK_INTERVAL, EXECUTE_TIME);
        }

        private sealed class RecordingStateListener : IBossStateListener
        {
            public List<int> RestoredCasts       { get; } = new List<int>();
            public List<int> RestoredProjectiles { get; } = new List<int>();
            public List<int> RestoredAreas       { get; } = new List<int>();
            public List<int> RestoredBeams       { get; } = new List<int>();
            public List<int> RestoredStars       { get; } = new List<int>();
            public List<int> RestoredBarriers    { get; } = new List<int>();

            public void OnBarrierRestored(in BossBarrierStart start)
            {
                RestoredBarriers.Add(start.BarrierId);
            }

            public void OnTelegraphRestored(in BossTelegraphSnapshot snapshot)
            {
                RestoredCasts.Add(snapshot.Start.CastId);
            }

            public void OnBeamRestored(in BossBeamSnapshot snapshot)
            {
                RestoredBeams.Add(snapshot.Start.BeamId);
            }

            public void OnStarRestored(in BossStarSnapshot snapshot)
            {
                RestoredStars.Add(snapshot.Start.StarId);
            }

            public void OnProjectileRestored(in BossProjectileSnapshot snapshot)
            {
                RestoredProjectiles.Add(snapshot.Start.ProjectileId);
            }

            public void OnAreaRestored(in BossAreaSnapshot snapshot)
            {
                RestoredAreas.Add(snapshot.Start.AreaId);
            }

            public void OnTelegraphStarted(in BossTelegraphStart start)
            {
            }

            public void OnAimChanged(int castId, Vector3 direction, float length, double serverTime)
            {
            }

            public void OnDirectionLocked(int castId, Vector3 direction, float length)
            {
            }

            public void OnStruck(int castId, double serverTime)
            {
            }

            public void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
            {
            }

            public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall, double stopServerTime)
            {
            }

            public void OnFinished(int castId)
            {
            }

            public void OnCancelled(int castId)
            {
            }

            public void OnProjectileLaunched(in BossProjectileStart start)
            {
            }

            public void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
            {
            }

            public void OnAreaActivated(in BossAreaStart start)
            {
            }

            public void OnAreaTicked(int areaId, int hitIndex, double roundServerTime)
            {
            }

            public void OnAreaEnded(int areaId, EBossAreaFate fate)
            {
            }

            public void OnSweepStarted(in BossSweepStart start)
            {
            }

            public void OnSweepZoneScheduled(in BossTelegraphStart zone)
            {
            }

            public void OnSweepZoneBlasted(int zoneId, double blastServerTime)
            {
            }

            public void OnSweepCancelled(int castId)
            {
            }

            public void OnBeamTelegraphStarted(in BossBeamStart start)
            {
            }

            public void OnBeamCancelled(int castId, int beamId)
            {
            }

            public void OnBeamActivated(int beamId, double activateServerTime)
            {
            }

            public void OnBeamTicked(int beamId, int hitIndex, double roundServerTime)
            {
            }

            public void OnBeamEnded(int beamId, EBossAreaFate fate)
            {
            }

            public void OnStarTelegraphStarted(in BossStarStart start)
            {
            }

            public void OnStarCancelled(int castId, int starId)
            {
            }

            public void OnStarActivated(int starId, double activateServerTime)
            {
            }

            public void OnStarTicked(int starId, int hitIndex, double roundServerTime)
            {
            }

            public void OnStarEnded(int starId, EBossAreaFate fate)
            {
            }

            public void OnBarrierStarted(in BossBarrierStart start)
            {
            }

            public void OnBarrierActivated(int barrierId, double activateServerTime)
            {
            }

            public void OnBarrierCompleted(int barrierId, double completeServerTime)
            {
            }

            public void OnBarrierCleared(int barrierId)
            {
            }
        }
    }
}
