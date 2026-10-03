// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    // 결계 전개 시퀀스를 가짜 의존성과 서버 시각 직접 주입으로 확인한다. 값은 임시 설정이라 기획 수치가 아니다
    // 실제 서버 틱, 복제, 화면은 거치지 않는다
    public class BossBarrierDirectorTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  ARENA_RADIUS   = 18f;
        private const float  HREF           = 1000f;
        private const float  MOVE_SPEED     = 10f;
        private const float  MOVE_TIMEOUT   = 3f;
        private const float  TELEGRAPH      = 2f;
        private const float  DEPLOY         = 1f;
        private const float  AREA_RATIO     = 0.25f;
        private const float  INTERVAL       = 1f;
        private const float  DAMAGE_REF     = 0.02f;
        private const double STEP           = 0.1;
        private const float  ARRIVE_SECONDS = 1f; // 9m를 초속 10m로 가면 0.9초
        private const float  SAFE_DISTANCE  = 5f;
        private const float  HAZARD_DISTANCE = 17f;
        private const float  OUTSIDE_DISTANCE = 30f;
        private const float  FLOAT_TOLERANCE = 0.001f;

        /****************************************
        *               Fakes
        ****************************************/

        private sealed class FakeMovement : IBossMovement
        {
            public FakeCombatTarget Self;
            public bool             Blocked;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return maxDistance;
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                if (Blocked)
                {
                    return from;
                }

                Self.Position = to;
                return to;
            }
        }

        private sealed class RadiusQuery : ICombatTargetQuery
        {
            public List<ICombatTarget> Players { get; } = new List<ICombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                results.AddRange(Players);
                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Players.Count; ++i)
                {
                    Vector3 offset = Players[i].Position - center;
                    offset.y = 0f;
                    if (offset.magnitude <= radius)
                    {
                        results.Add(Players[i]);
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
                return false;
            }
        }

        private sealed class RecordingListener : IBossBarrierListener
        {
            public int StartedCount;
            public int ActivatedCount;
            public int CompletedCount;
            public int ClearedCount;
            public int LastBarrierId;
            public BossBarrierStart LastStart;

            public void OnBarrierStarted(in BossBarrierStart start)
            {
                ++StartedCount;
                LastBarrierId = start.BarrierId;
                LastStart     = start;
            }

            public void OnBarrierActivated(int barrierId, double activateServerTime) { ++ActivatedCount; }
            public void OnBarrierCompleted(int barrierId, double completeServerTime) { ++CompletedCount; }
            public void OnBarrierCleared(int barrierId, EBossInterruptReason reason) { ++ClearedCount; }
        }

        private FakeCombatTarget  _boss;
        private FakeCombatTarget  _player;
        private FakeMovement      _movement;
        private RadiusQuery       _query;
        private FakeDamageResolver _resolver;
        private RecordingListener _listener;
        private BossBarrierDirector _director;
        private double            _now;

        /****************************************
        *               Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _boss     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = new Vector3(0f, 0f, -9f) };
            _player   = new FakeCombatTarget { CombatId = 2, Faction = ECombatFaction.Player, Position = new Vector3(SAFE_DISTANCE, 0f, 0f) };
            _movement = new FakeMovement { Self = _boss };
            _query    = new RadiusQuery();
            _query.Players.Add(_player);
            _resolver = new FakeDamageResolver();
            _listener = new RecordingListener();

            BossBarrierSettings settings = new BossBarrierSettings(MOVE_SPEED, MOVE_TIMEOUT, TELEGRAPH, DEPLOY, AREA_RATIO,
                INTERVAL, DAMAGE_REF);
            _director = new BossBarrierDirector(settings, _boss, _movement, _query, _resolver, Vector3.zero, ARENA_RADIUS,
                HREF, _listener);
            _now = 100d;
        }

        private void Run(double seconds, bool pending = true)
        {
            double end = _now + seconds;
            while (_now < end)
            {
                _now += STEP;
                _director.Tick(_now, pending);
            }
        }

        private void RunToActive()
        {
            _director.Tick(_now, true);
            Run(ARRIVE_SECONDS + TELEGRAPH + STEP * 3);
        }

        /****************************************
        *               Tests
        ****************************************/

        [Test]
        public void Tick_NotPending_DoesNotStart()
        {
            Run(5d, false);

            Assert.AreEqual(EBossBarrierState.Idle, _director.State);
            Assert.AreEqual(0, _listener.StartedCount);
        }

        [Test]
        public void Tick_PendingRepeatedly_StartsOnlyOnce()
        {
            Run(20d);
            int firstId = _listener.LastBarrierId;
            Run(20d);

            Assert.AreEqual(1, _listener.StartedCount);
            Assert.AreEqual(firstId, _listener.LastBarrierId);
            Assert.AreEqual(1, _listener.CompletedCount);
        }

        [Test]
        public void Tick_Normal_MovesBossThenRunsTelegraphActivateComplete()
        {
            _director.Tick(_now, true);
            Assert.AreEqual(EBossBarrierState.Moving, _director.State);

            Run(ARRIVE_SECONDS);
            Assert.AreEqual(EBossBarrierState.Telegraph, _director.State);
            Assert.AreEqual(0f, Vector3.Distance(_boss.Position, Vector3.zero), 0.1f);
            Assert.AreEqual(1, _listener.StartedCount);
            Assert.AreEqual(0, _listener.ActivatedCount);

            Run(TELEGRAPH + STEP * 2);
            Assert.AreEqual(1, _listener.ActivatedCount);
            Assert.IsTrue(_director.IsHazardActive);

            Run(DEPLOY + STEP * 2);
            Assert.AreEqual(EBossBarrierState.Completed, _director.State);
            Assert.AreEqual(1, _listener.CompletedCount);
            Assert.IsTrue(_director.IsCompleted);
        }

        [Test]
        public void Tick_MovementBlocked_StartsTelegraphAfterTimeout()
        {
            _movement.Blocked = true;

            _director.Tick(_now, true);
            Run(MOVE_TIMEOUT * 0.5f);
            Assert.AreEqual(EBossBarrierState.Moving, _director.State);

            Run(MOVE_TIMEOUT);
            Assert.AreEqual(EBossBarrierState.Telegraph, _director.State);
        }

        [Test]
        public void Tick_Telegraph_DoesNotDamage()
        {
            _player.Position = new Vector3(HAZARD_DISTANCE, 0f, 0f);

            _director.Tick(_now, true);
            Run(ARRIVE_SECONDS + TELEGRAPH * 0.9f);

            Assert.AreEqual(EBossBarrierState.Telegraph, _director.State);
            Assert.IsFalse(_director.IsHazardActive);
            Assert.AreEqual(0, _resolver.Requests.Count);
        }

        [Test]
        public void Tick_Active_DamagesOnlyHazardRing()
        {
            _query.Players.Add(new FakeCombatTarget { CombatId = 3, Faction = ECombatFaction.Player,
                Position = new Vector3(HAZARD_DISTANCE, 0f, 0f) });
            _query.Players.Add(new FakeCombatTarget { CombatId = 4, Faction = ECombatFaction.Player,
                Position = new Vector3(OUTSIDE_DISTANCE, 0f, 0f) });

            RunToActive();
            Run(INTERVAL);

            Assert.Greater(_resolver.Requests.Count, 0);
            for (int i = 0; i < _resolver.Requests.Count; ++i)
            {
                Assert.AreEqual(3, _resolver.Requests[i].HitTargetId);
                Assert.AreEqual(DAMAGE_REF * HREF, _resolver.Requests[i].RawDamage, FLOAT_TOLERANCE);
            }
        }

        [Test]
        public void Tick_Active_DamageRepeatsEveryInterval()
        {
            _player.Position = new Vector3(HAZARD_DISTANCE, 0f, 0f);

            RunToActive();
            int afterActivate = _resolver.Requests.Count;
            Run(INTERVAL * 2);

            Assert.AreEqual(1, afterActivate);
            Assert.AreEqual(afterActivate + 2, _resolver.Requests.Count);
        }

        [Test]
        public void Tick_AfterComplete_HazardStaysActive()
        {
            _player.Position = new Vector3(HAZARD_DISTANCE, 0f, 0f);

            RunToActive();
            Run(DEPLOY + 5d);
            int count = _resolver.Requests.Count;
            Run(INTERVAL * 2);

            Assert.IsTrue(_director.IsHazardActive);
            Assert.AreEqual(EBossBarrierState.Completed, _director.State);
            Assert.AreEqual(count + 2, _resolver.Requests.Count);
        }

        [Test]
        public void Cancel_DuringTelegraph_ClearsWithoutDamage()
        {
            _player.Position = new Vector3(HAZARD_DISTANCE, 0f, 0f);

            _director.Tick(_now, true);
            Run(ARRIVE_SECONDS + STEP * 2);
            _director.Cancel(EBossInterruptReason.CombatEnd);
            Run(TELEGRAPH + DEPLOY + 3d);

            Assert.AreEqual(EBossBarrierState.Cleared, _director.State);
            Assert.AreEqual(1, _listener.ClearedCount);
            Assert.AreEqual(0, _listener.ActivatedCount);
            Assert.AreEqual(0, _listener.CompletedCount);
            Assert.AreEqual(0, _resolver.Requests.Count);
            Assert.IsFalse(_director.TryGetSnapshot(out BossBarrierStart _));
        }

        [Test]
        public void Cancel_AfterComplete_StopsDamageAndClears()
        {
            _player.Position = new Vector3(HAZARD_DISTANCE, 0f, 0f);

            RunToActive();
            Run(DEPLOY + STEP * 2);
            _director.Cancel(EBossInterruptReason.Death);
            int count = _resolver.Requests.Count;
            Run(INTERVAL * 3);

            Assert.AreEqual(EBossBarrierState.Cleared, _director.State);
            Assert.IsFalse(_director.IsHazardActive);
            Assert.AreEqual(1, _listener.ClearedCount);
            Assert.AreEqual(count, _resolver.Requests.Count);
        }

        [Test]
        public void Cancel_Twice_ClearsOnce()
        {
            RunToActive();
            _director.Cancel(EBossInterruptReason.CombatEnd);
            _director.Cancel(EBossInterruptReason.CombatEnd);

            Assert.AreEqual(1, _listener.ClearedCount);
        }

        [Test]
        public void Tick_AfterCancel_DoesNotRestart()
        {
            RunToActive();
            _director.Cancel(EBossInterruptReason.CombatEnd);
            Run(10d);

            Assert.AreEqual(EBossBarrierState.Cleared, _director.State);
            Assert.AreEqual(1, _listener.StartedCount);
        }

        [Test]
        public void Reset_AfterRun_AllowsRestartWithNewId()
        {
            RunToActive();
            int firstId = _listener.LastBarrierId;
            _director.Reset();
            Assert.AreEqual(EBossBarrierState.Idle, _director.State);
            Assert.IsFalse(_director.IsCompleted);

            _boss.Position = new Vector3(0f, 0f, -9f);
            _director.Tick(_now, true);
            Run(ARRIVE_SECONDS + STEP * 2);

            Assert.AreEqual(2, _listener.StartedCount);
            Assert.AreNotEqual(firstId, _listener.LastBarrierId);
            Assert.AreEqual(EBossBarrierState.Telegraph, _director.State);
        }

        [Test]
        public void Reset_AfterRun_ClearsPreviousBarrier()
        {
            RunToActive();
            _director.Reset();

            Assert.AreEqual(1, _listener.ClearedCount);
            Assert.IsFalse(_director.TryGetSnapshot(out BossBarrierStart _));
        }

        [Test]
        public void TryGetSnapshot_DuringBarrier_ReturnsTimes()
        {
            RunToActive();

            Assert.IsTrue(_director.TryGetSnapshot(out BossBarrierStart start));
            Assert.AreEqual(start.TelegraphServerTime + TELEGRAPH, start.ActivateServerTime, FLOAT_TOLERANCE);
            Assert.AreEqual(start.ActivateServerTime + DEPLOY, start.CompleteServerTime, FLOAT_TOLERANCE);
            Assert.Less(start.InnerRadius, start.OuterRadius);
        }

        [Test]
        public void Settings_InnerRadius_MatchesAreaRatio()
        {
            BossBarrierSettings settings = new BossBarrierSettings(MOVE_SPEED, MOVE_TIMEOUT, TELEGRAPH, DEPLOY, AREA_RATIO,
                INTERVAL, DAMAGE_REF);
            float inner = settings.GetInnerRadius(ARENA_RADIUS);

            float hazardArea = Mathf.PI * ((ARENA_RADIUS * ARENA_RADIUS) - (inner * inner));
            float arenaArea  = Mathf.PI * ARENA_RADIUS * ARENA_RADIUS;
            Assert.AreEqual(AREA_RATIO, hazardArea / arenaArea, FLOAT_TOLERANCE);
        }

        [Test]
        public void Track_Phase_FollowsServerTimesAndClear()
        {
            BossBarrierStart start = new BossBarrierStart(1, 7, Vector3.zero, ARENA_RADIUS, 10f, INTERVAL, 10d, 12d, 13d);
            BossBarrierTrack track = new BossBarrierTrack(in start);

            Assert.AreEqual(EBossBarrierPhase.None, track.GetPhase(9d));
            Assert.AreEqual(EBossBarrierPhase.Telegraph, track.GetPhase(11.9d));
            Assert.AreEqual(EBossBarrierPhase.Active, track.GetPhase(12d));
            Assert.AreEqual(EBossBarrierPhase.Active, track.GetPhase(500d));

            Assert.IsTrue(track.TryClear());
            Assert.IsFalse(track.TryClear());
            Assert.AreEqual(EBossBarrierPhase.None, track.GetPhase(500d));
        }
    }
}
