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
        private const float  SIDE_OFFSET   = 1f; // m, 옆으로 비껴 멈춰도 진행 방향 거리만 센다
        private const float  SPEED         = 12f;
        private const float  RADIUS        = 3f;
        private const float  DURATION      = 6f;
        private const float  TICK_INTERVAL = 1f;
        private const double ROUND_TIME    = 12.0;
        private const float  FLOAT_EPSILON = 0.0001f;
        private const double TIME_EPSILON  = 0.000001;

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

            ledger.OnDashStopped(CAST_ID, new Vector3(SIDE_OFFSET, 0f, STOP_DISTANCE), true);

            Assert.AreEqual(STOP_DISTANCE, ledger.Cast.DashStoppedDistance, FLOAT_EPSILON);
            Assert.IsTrue(ledger.Cast.DashStoppedByWall);
            Assert.AreEqual(LENGTH, ledger.Cast.DashDistance, FLOAT_EPSILON);
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

        /****************************************
        *                Helpers
        ****************************************/

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

            public void OnTelegraphRestored(in BossTelegraphSnapshot snapshot)
            {
                RestoredCasts.Add(snapshot.Start.CastId);
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

            public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall)
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
        }
    }
}
