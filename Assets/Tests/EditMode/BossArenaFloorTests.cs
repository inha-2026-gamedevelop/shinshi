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

namespace Shinshi.Tests
{
    public sealed class BossArenaFloorTests
    {
        // 외곽 바닥 제거 뒤의 낙하를 서버 위치 표본으로 확정하는 규칙을 본다. 제거 순간에는 아무도 내주지 않고, 외곽 표본이
        // FALL_SECONDS 이상 이어진 참가자만 한 생명에 한 번 내준다. 부활 지점은 주입한 검증으로 NavMesh 없이 본다

        /****************************************
        *                Fields
        ****************************************/

        private const int    CAST_ID        = 5;
        private const int    PLAYER_A       = 11;
        private const int    PLAYER_B       = 12;
        private const float  CENTRAL_RADIUS = 8f;
        private const double TICK           = 0.1;
        private const double START          = 100.0;
        private const float  EPSILON        = 0.001f;
        private const int    REVIVE_SAMPLES = 17; // 중심 하나와 고리 둘의 후보 수

        private static readonly Vector3 CENTER  = new Vector3(10f, 0f, -5f);
        private static readonly Vector3 INSIDE  = CENTER + new Vector3(3f, 0f, 0f);
        private static readonly Vector3 OUTSIDE = CENTER + new Vector3(CENTRAL_RADIUS + 1f, 0f, 0f);

        private FakeRoster     _roster;
        private BossArenaFloor _floor;
        private List<int>      _fallen;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _roster = new FakeRoster();
            _roster.Add(PLAYER_A, INSIDE);
            _roster.Add(PLAYER_B, INSIDE);
            _floor  = new BossArenaFloor(_roster, _roster, CENTER, CENTRAL_RADIUS, AcceptAll);
            _fallen = new List<int> { 999 };
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Constructor_InvalidArgument_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BossArenaFloor(null, _roster, CENTER, CENTRAL_RADIUS));
            Assert.Throws<ArgumentNullException>(() => new BossArenaFloor(_roster, null, CENTER, CENTRAL_RADIUS));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BossArenaFloor(_roster, _roster, CENTER, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BossArenaFloor(_roster, _roster, CENTER, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BossArenaFloor(_roster, _roster, new Vector3(float.NaN, 0f, 0f), CENTRAL_RADIUS));
        }

        [Test]
        public void CollectFallen_BeforeRemoval_ClearsResultsAndReturnsZero()
        {
            _roster.Move(PLAYER_A, OUTSIDE);

            Assert.AreEqual(0, _floor.CollectFallen(START, _fallen));
            Assert.AreEqual(0, _floor.CollectFallen(START + 10.0, _fallen));

            Assert.IsEmpty(_fallen, "받은 목록을 먼저 비운다");
            Assert.IsFalse(_floor.IsOuterFloorRemoved);
        }

        [Test]
        public void TryRemoveOuterFloor_Repeated_StaysAccepted()
        {
            Assert.IsTrue(_floor.TryRemoveOuterFloor(CAST_ID, START));
            Assert.IsTrue(_floor.TryRemoveOuterFloor(CAST_ID, START + TICK));

            Assert.IsTrue(_floor.IsOuterFloorRemoved);
        }

        [Test]
        public void CollectFallen_StandingOutsideAtRemoval_DoesNotFallOnFirstSample()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);

            Assert.AreEqual(0, _floor.CollectFallen(START, _fallen), "제거 순간 외곽에 있다는 이유만으로는 떨어지지 않는다");
        }

        [Test]
        public void CollectFallen_OutsideForFallSeconds_DeliversOnce()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);

            Assert.AreEqual(0, _floor.CollectFallen(START, _fallen));
            Assert.AreEqual(0, _floor.CollectFallen(START + BossArenaFloor.FALL_SECONDS - TICK, _fallen));
            Assert.AreEqual(1, _floor.CollectFallen(START + BossArenaFloor.FALL_SECONDS, _fallen));
            CollectionAssert.AreEqual(new[] { PLAYER_A }, _fallen);

            Assert.AreEqual(0, _floor.CollectFallen(START + BossArenaFloor.FALL_SECONDS + TICK, _fallen), "같은 생명에서는 한 번만 내준다");
            Assert.AreEqual(0, _floor.CollectFallen(START + 10.0, _fallen), "기믹이 죽이지 않아 살아 있어도 다시 내주지 않는다");
        }

        [Test]
        public void CollectFallen_ReturnsToCenterWithinFallSeconds_Survives()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);
            _floor.CollectFallen(START, _fallen);

            _roster.Move(PLAYER_A, INSIDE);
            Assert.AreEqual(0, _floor.CollectFallen(START + TICK, _fallen));

            // 중앙 표본 하나가 타이머를 지웠으므로 다시 나가면 처음부터 센다
            _roster.Move(PLAYER_A, OUTSIDE);
            Assert.AreEqual(0, _floor.CollectFallen(START + (2 * TICK), _fallen));
            Assert.AreEqual(0, _floor.CollectFallen(START + BossArenaFloor.FALL_SECONDS + TICK, _fallen));
            Assert.AreEqual(1, _floor.CollectFallen(START + (2 * TICK) + BossArenaFloor.FALL_SECONDS, _fallen));
        }

        [Test]
        public void CollectFallen_WalksOutAfterRemoval_FallsByTheSameRule()
        {
            _floor.TryRemoveOuterFloor(CAST_ID, START);
            Assert.AreEqual(0, _floor.CollectFallen(START, _fallen));

            _roster.Move(PLAYER_B, OUTSIDE);
            double leftAt = START + 5.0;
            Assert.AreEqual(0, _floor.CollectFallen(leftAt, _fallen));
            Assert.AreEqual(1, _floor.CollectFallen(leftAt + BossArenaFloor.FALL_SECONDS, _fallen));

            CollectionAssert.AreEqual(new[] { PLAYER_B }, _fallen);
        }

        [Test]
        public void CollectFallen_OnCentralEdge_IsInside()
        {
            _roster.Move(PLAYER_A, CENTER + new Vector3(0f, 3f, CENTRAL_RADIUS));
            _floor.TryRemoveOuterFloor(CAST_ID, START);

            _floor.CollectFallen(START, _fallen);

            Assert.AreEqual(0, _floor.CollectFallen(START + 10.0, _fallen), "반경 위는 중앙이고 높이는 보지 않는다");
        }

        [Test]
        public void CollectFallen_DeadOutside_IsIgnoredAndTimerCleared()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);
            _floor.CollectFallen(START, _fallen);

            _roster.SetLife(PLAYER_A, EParticipantLifeState.Dead);
            Assert.AreEqual(0, _floor.CollectFallen(START + 10.0, _fallen), "쓰러진 사람은 떨어지지 않는다");

            // 사망이 타이머를 지웠으므로 같은 자리에서 다시 살아나도 처음부터 센다
            _roster.SetLife(PLAYER_A, EParticipantLifeState.Alive);
            Assert.AreEqual(0, _floor.CollectFallen(START + 10.0 + TICK, _fallen));
            Assert.AreEqual(1, _floor.CollectFallen(START + 10.0 + TICK + BossArenaFloor.FALL_SECONDS, _fallen));
        }

        [Test]
        public void CollectFallen_ReconnectGraceOutside_StillFalls()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _roster.SetConnected(PLAYER_A, false);
            _floor.TryRemoveOuterFloor(CAST_ID, START);

            _floor.CollectFallen(START, _fallen);

            Assert.AreEqual(1, _floor.CollectFallen(START + BossArenaFloor.FALL_SECONDS, _fallen), "연결을 끊어 추락을 피할 수 없다");
        }

        [Test]
        public void CollectFallen_DepartedOutside_IsIgnoredAndTimerCleared()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);
            _floor.CollectFallen(START, _fallen);

            _roster.SetListed(PLAYER_A, false);
            Assert.AreEqual(0, _floor.CollectFallen(START + 10.0, _fallen));

            // 명단에서 빠진 동안 기록이 지워져 돌아와도 이어 세지 않는다
            _roster.SetListed(PLAYER_A, true);
            Assert.AreEqual(0, _floor.CollectFallen(START + 10.0 + TICK, _fallen));
        }

        [Test]
        public void CollectFallen_RevivedAfterFall_CanFallAgain()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);
            _floor.CollectFallen(START, _fallen);
            double fellAt = START + BossArenaFloor.FALL_SECONDS;
            Assert.AreEqual(1, _floor.CollectFallen(fellAt, _fallen));
            _roster.SetLife(PLAYER_A, EParticipantLifeState.Dead);
            Assert.AreEqual(0, _floor.CollectFallen(fellAt + TICK, _fallen));

            // 부활해 중앙에 섰다가 다시 외곽으로 나간다
            double revivedAt = fellAt + 5.0;
            _roster.Revive(PLAYER_A, revivedAt);
            _roster.Move(PLAYER_A, INSIDE);
            Assert.AreEqual(0, _floor.CollectFallen(revivedAt, _fallen));

            _roster.Move(PLAYER_A, OUTSIDE);
            Assert.AreEqual(0, _floor.CollectFallen(revivedAt + TICK, _fallen));
            Assert.AreEqual(1, _floor.CollectFallen(revivedAt + TICK + BossArenaFloor.FALL_SECONDS, _fallen), "낙하 이력은 생명 단위다");
        }

        [Test]
        public void CollectFallen_RevivedBeforeFall_DoesNotClearDeliveredHistory()
        {
            _roster.Revive(PLAYER_A, START - 1.0);
            _roster.Move(PLAYER_A, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);
            _floor.CollectFallen(START, _fallen);
            Assert.AreEqual(1, _floor.CollectFallen(START + BossArenaFloor.FALL_SECONDS, _fallen), "부활 직후 보호는 낙하를 막지 않는다");

            Assert.AreEqual(0, _floor.CollectFallen(START + 10.0, _fallen), "낙하보다 앞선 부활은 이력을 지우지 않는다");
        }

        [Test]
        public void CollectFallen_RevivedBetweenSamplesWhileOutside_RestartsTimer()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);
            _floor.CollectFallen(START, _fallen);

            // 두 표본 사이에 죽었다가 외곽에서 부활했다
            _roster.Revive(PLAYER_A, START + TICK);
            double sampledAt = START + BossArenaFloor.FALL_SECONDS;
            Assert.AreEqual(0, _floor.CollectFallen(sampledAt, _fallen), "부활은 타이머를 지운다");
            Assert.AreEqual(1, _floor.CollectFallen(sampledAt + BossArenaFloor.FALL_SECONDS, _fallen));
        }

        [Test]
        public void CollectFallen_TwoOutside_DeliversBothInRosterOrder()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _roster.Move(PLAYER_B, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);
            _floor.CollectFallen(START, _fallen);

            Assert.AreEqual(2, _floor.CollectFallen(START + BossArenaFloor.FALL_SECONDS, _fallen));

            CollectionAssert.AreEqual(new[] { PLAYER_A, PLAYER_B }, _fallen);
        }

        [Test]
        public void CollectFallen_PerTick_DoesNotAllocate()
        {
            _roster.Move(PLAYER_A, OUTSIDE);
            _floor.TryRemoveOuterFloor(CAST_ID, START);
            _floor.CollectFallen(START, _fallen);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i <= 64; ++i)
            {
                _floor.CollectFallen(START + (i * TICK), _fallen);
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Test]
        public void TryGetSafeRevivePoint_CenterValid_ReturnsCenter()
        {
            Assert.IsTrue(_floor.TryGetSafeRevivePoint(PLAYER_A, out Vector3 point));

            Assert.AreEqual(CENTER, point);
        }

        [Test]
        public void TryGetSafeRevivePoint_CenterRejected_PicksCandidateWithinHalfRadius()
        {
            var floor = new BossArenaFloor(_roster, _roster, CENTER, CENTRAL_RADIUS, RejectCenter);

            Assert.IsTrue(floor.TryGetSafeRevivePoint(PLAYER_A, out Vector3 point));

            float distance = Vector3.Distance(point, CENTER);
            Assert.Greater(distance, EPSILON);
            Assert.LessOrEqual(distance, (CENTRAL_RADIUS * BossArenaFloor.REVIVE_AREA_RATE) + EPSILON);
        }

        [Test]
        public void TryGetSafeRevivePoint_OnlyOuterRingValid_IsStillFound()
        {
            float outerRing = CENTRAL_RADIUS * BossArenaFloor.REVIVE_AREA_RATE;
            var floor = new BossArenaFloor(_roster, _roster, CENTER, CENTRAL_RADIUS,
                (Vector3 candidate, out Vector3 point) =>
                {
                    point = candidate;
                    return Vector3.Distance(candidate, CENTER) > (outerRing - EPSILON);
                });

            Assert.IsTrue(floor.TryGetSafeRevivePoint(PLAYER_A, out Vector3 found));

            Assert.AreEqual(outerRing, Vector3.Distance(found, CENTER), EPSILON);
        }

        [Test]
        public void TryGetSafeRevivePoint_NoValidCandidate_ReturnsFalseAfterTryingAll()
        {
            int calls = 0;
            var floor = new BossArenaFloor(_roster, _roster, CENTER, CENTRAL_RADIUS,
                (Vector3 candidate, out Vector3 point) =>
                {
                    ++calls;
                    point = candidate;
                    return false;
                });

            Assert.IsFalse(floor.TryGetSafeRevivePoint(PLAYER_A, out _));

            Assert.AreEqual(REVIVE_SAMPLES, calls);
        }

        [Test]
        public void TryGetSafeRevivePoint_ValidatorSnapsOutsideHalfRadius_IsRejected()
        {
            var floor = new BossArenaFloor(_roster, _roster, CENTER, CENTRAL_RADIUS,
                (Vector3 candidate, out Vector3 point) =>
                {
                    point = OUTSIDE;
                    return true;
                });

            Assert.IsFalse(floor.TryGetSafeRevivePoint(PLAYER_A, out _), "맞춘 지점이 외곽으로 밀려나면 쓰지 않는다");
        }

        [Test]
        public void TryGetSafeRevivePoint_StandQueryBlocksAll_ReturnsFalse()
        {
            var floor = new BossArenaFloor(_roster, _roster, CENTER, CENTRAL_RADIUS, AcceptAll, new FakeStand(false));

            Assert.IsFalse(floor.TryGetSafeRevivePoint(PLAYER_A, out _));
        }

        [Test]
        public void TryGetSafeRevivePoint_StandQueryAllows_UsesPlayerBodyRadius()
        {
            var stand = new FakeStand(true);
            var floor = new BossArenaFloor(_roster, _roster, CENTER, CENTRAL_RADIUS, AcceptAll, stand);

            Assert.IsTrue(floor.TryGetSafeRevivePoint(PLAYER_A, out _));

            Assert.AreEqual(Constants.Combat.PLAYER_BODY_RADIUS, stand.LastRadius);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static bool AcceptAll(Vector3 candidate, out Vector3 point)
        {
            point = candidate;
            return true;
        }

        private static bool RejectCenter(Vector3 candidate, out Vector3 point)
        {
            point = candidate;
            return Vector3.Distance(candidate, CENTER) > EPSILON;
        }

        private sealed class FakeStand : IBossStandQuery
        {
            private readonly bool _canStand;

            public float LastRadius { get; private set; }

            public FakeStand(bool canStand)
            {
                _canStand = canStand;
            }

            public bool CanStand(Vector3 point, float bodyRadius)
            {
                LastRadius = bodyRadius;
                return _canStand;
            }
        }

        private sealed class FakeRoster : IBossParticipantRoster, IBossReviveSource
        {
            private sealed class Member
            {
                public int                   CombatId;
                public Vector3               Position;
                public EParticipantLifeState LifeState   = EParticipantLifeState.Alive;
                public bool                  IsConnected = true;
                public bool                  IsListed    = true;
                public bool                  HasRevived;
                public double                RevivedAt;
            }

            private readonly List<Member> _members = new List<Member>();

            public void Add(int combatId, Vector3 position)
            {
                _members.Add(new Member { CombatId = combatId, Position = position });
            }

            public void Move(int combatId, Vector3 position) { Find(combatId).Position = position; }
            public void SetLife(int combatId, EParticipantLifeState state) { Find(combatId).LifeState = state; }
            public void SetConnected(int combatId, bool isConnected) { Find(combatId).IsConnected = isConnected; }
            public void SetListed(int combatId, bool isListed) { Find(combatId).IsListed = isListed; }

            public void Revive(int combatId, double serverTime)
            {
                Member member = Find(combatId);
                member.LifeState  = EParticipantLifeState.Alive;
                member.HasRevived = true;
                member.RevivedAt  = serverTime;
            }

            public int CollectParticipants(List<int> combatIds)
            {
                for (int i = 0; i < _members.Count; ++i)
                {
                    if (_members[i].IsListed)
                    {
                        combatIds.Add(_members[i].CombatId);
                    }
                }

                return combatIds.Count;
            }

            public bool TryGetParticipant(int combatId, out BossParticipantInfo info)
            {
                Member member = Find(combatId);
                if ((member == null) || (!member.IsListed))
                {
                    info = default;
                    return false;
                }

                info = new BossParticipantInfo(member.LifeState, member.IsConnected, true, member.Position);
                return true;
            }

            public bool TryGetLastReviveServerTime(int combatId, out double serverTime)
            {
                Member member = Find(combatId);
                if ((member == null) || (!member.HasRevived))
                {
                    serverTime = 0.0;
                    return false;
                }

                serverTime = member.RevivedAt;
                return true;
            }

            private Member Find(int combatId)
            {
                for (int i = 0; i < _members.Count; ++i)
                {
                    if (_members[i].CombatId == combatId)
                    {
                        return _members[i];
                    }
                }

                return null;
            }
        }
    }
}
