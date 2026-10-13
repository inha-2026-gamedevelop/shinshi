// System
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    // 시각은 이진 분수로 넘겨 경계 판정에 부동소수 오차가 끼지 않게 한다. 예고 1.2초는 이진 분수가 아니라서 판정 시각 앞뒤(4.0, 4.25)로 확인한다.
    // 하드 시간표는 진입 t=0, 전환 끝 3, 첫 고리 예고 3(판정 4.2), 직선 예고 5(판정 6.2), 고리 예고 7(판정 8.2)이다.
    // 기준점은 전장 중심(원점)이라 구조물 중심을 정규화하면 바깥쪽 방향이다
    public class WolbaekTalismanFormationTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  HREF          = 3541f;
        private const float  TRIGGER_RATE  = 0.75f;
        private const float  NEXT_RATE     = 0.5f;
        private const float  ARENA_RADIUS  = 18f;
        private const int    FIRST_PLAYER  = 10;
        private const float  DAMAGE_REF    = 0.12f;
        private const float  PUSH_DISTANCE = 1f;
        private const float  INNER_HALF_OFFSET = 2f; // 구조물 중심에서 안쪽 또는 바깥쪽으로 옮기는 거리, 반경 3m 안이다

        /****************************************
        *               Doubles
        ****************************************/

        private sealed class Participant
        {
            public FakeCombatTarget Target    { get; set; }
            public bool             Connected { get; set; } = true;
            public bool             Revivable { get; set; } = true;
            public bool             Present   { get; set; } = true;
        }

        private sealed class FakeRoster : IBossParticipantRoster
        {
            public List<Participant> Members { get; } = new List<Participant>();

            public int CollectParticipants(List<int> combatIds)
            {
                for (int i = 0; i < Members.Count; ++i)
                {
                    if (Members[i].Present)
                    {
                        combatIds.Add(Members[i].Target.CombatId);
                    }
                }

                return combatIds.Count;
            }

            public bool TryGetParticipant(int combatId, out BossParticipantInfo info)
            {
                for (int i = 0; i < Members.Count; ++i)
                {
                    Participant member = Members[i];
                    if ((member.Target.CombatId == combatId) && (member.Present))
                    {
                        info = new BossParticipantInfo(member.Target.LifeState, member.Connected, member.Revivable, member.Target.Position);
                        return true;
                    }
                }

                info = default;
                return false;
            }
        }

        private sealed class FakeArena : IBossArenaSource
        {
            public bool Available { get; set; } = true;

            public bool TryGetArena(out Vector3 center, out float radius)
            {
                center = Vector3.zero;
                radius = ARENA_RADIUS;
                return Available;
            }
        }

        // 서버 몸체 대역: 이동 요청을 기록하고 벽(WallX 아래로는 못 감)과 이동 불가(CanMove)를 흉내 낸다
        private sealed class MovingBoss : ICombatTarget, IBossMovement
        {
            public int                   CombatId      => BossHarness.SELF_ID;
            public ECombatFaction        Faction       => ECombatFaction.Enemy;
            public EParticipantLifeState LifeState     => EParticipantLifeState.Alive;
            public bool                  IsSummon      => false;
            public int                   OwnerCombatId => CombatIds.NONE;
            public Vector3               Position      { get; set; }
            public Vector3               Forward       => Vector3.forward;
            public float                 BodyRadius    => 2f;
            public float                 WallX         { get; set; } = float.NegativeInfinity;
            public bool                  CanMove       { get; set; } = true;
            public int                   MoveCalls     { get; private set; }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                ++MoveCalls;
                if (!CanMove)
                {
                    return Position;
                }

                Vector3 destination = to;
                if (to.x < WallX)
                {
                    destination = (from.x <= WallX) ? from : Vector3.Lerp(from, to, (from.x - WallX) / (from.x - to.x));
                }

                Position = destination;
                return Position;
            }

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return maxDistance;
            }
        }

        private sealed class PartyQuery : ICombatTargetQuery
        {
            public List<FakeCombatTarget> Players { get; } = new List<FakeCombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Players.Count; ++i)
                {
                    if (Players[i].LifeState == EParticipantLifeState.Alive)
                    {
                        results.Add(Players[i]);
                    }
                }

                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Players.Count; ++i)
                {
                    FakeCombatTarget target = Players[i];
                    float dx = target.Position.x - center.x;
                    float dz = target.Position.z - center.z;
                    if ((target.LifeState == EParticipantLifeState.Alive) && (((dx * dx) + (dz * dz)) <= (radius * radius)))
                    {
                        results.Add(target);
                    }
                }

                return results.Count;
            }

            public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
            {
                return 0;
            }

            public bool TryGet(int combatId, out ICombatTarget target)
            {
                for (int i = 0; i < Players.Count; ++i)
                {
                    if (Players[i].CombatId == combatId)
                    {
                        target = Players[i];
                        return true;
                    }
                }

                target = null;
                return false;
            }
        }

        // 큐에 넣은 값을 순서대로 쓰고 비면 0이다. 진입 뒤에 값을 넣어 공격 모양 선택만 정할 수 있다
        private sealed class ScriptedRandom : IBossRandom
        {
            public Queue<int> Ints { get; } = new Queue<int>();

            public float NextFloat01()
            {
                return 0f;
            }

            public int NextInt(int maxExclusive)
            {
                return (Ints.Count > 0) ? (Ints.Dequeue() % maxExclusive) : 0;
            }
        }

        private sealed class Push
        {
            public int     CombatId;
            public Vector3 Direction;
            public float   Distance;
        }

        private sealed class FakeDisplacer : IBossHitDisplacer
        {
            public List<Push> Pushes { get; } = new List<Push>();

            public float TryPush(int combatId, Vector3 direction, float maxDistance)
            {
                Pushes.Add(new Push { CombatId = combatId, Direction = direction, Distance = maxDistance });
                return maxDistance;
            }
        }

        private sealed class FakeHazard : IBossAmbientHazard
        {
            public int          SuspendCount { get; private set; }
            public List<double> Resumes      { get; } = new List<double>();

            public void Suspend()
            {
                ++SuspendCount;
            }

            public void Resume(double serverTime)
            {
                Resumes.Add(serverTime);
            }
        }

        private sealed class Recorder : IWolbaekTalismanListener
        {
            public List<string>                Events  { get; } = new List<string>();
            public List<WolbaekTalismanAttack> Attacks { get; } = new List<WolbaekTalismanAttack>();
            public List<int>                   Hits    { get; } = new List<int>();
            public WolbaekTalismanStart        Start   { get; private set; }

            public void OnFormationStarted(in WolbaekTalismanStart start)
            {
                Start = start;
                Events.Add("start");
            }

            public void OnHoldStarted(int castInstanceId, double serverTime)
            {
                Events.Add("holdStart@" + serverTime);
            }

            public void OnHoldReset(int castInstanceId, double serverTime, EWolbaekHoldReset reason, int combatId)
            {
                Events.Add("reset:" + reason + ":" + combatId);
            }

            public void OnAttackTelegraphed(int castInstanceId, in WolbaekTalismanAttack attack)
            {
                Attacks.Add(attack);
                Events.Add("telegraph:" + attack.Kind);
            }

            public void OnAttackStruck(int castInstanceId, int attackId, double strikeServerTime, int hitCount)
            {
                Hits.Add(hitCount);
                Events.Add("strike:" + attackId);
            }

            public void OnAttackCancelled(int castInstanceId, int attackId)
            {
                Events.Add("cancel:" + attackId);
            }

            public void OnFormationSucceeded(int castInstanceId, double serverTime, double counterEndServerTime)
            {
                Events.Add("success@" + serverTime + "/" + counterEndServerTime);
            }

            public void OnCounterEnded(int castInstanceId, double serverTime)
            {
                Events.Add("counterEnd@" + serverTime);
            }

            public void OnFormationFailed(int castInstanceId, double serverTime, EWolbaekFormationFail reason)
            {
                Events.Add("fail:" + reason);
            }

            public void OnFormationHalted(int castInstanceId, double serverTime, EWolbaekHaltReason reason)
            {
                Events.Add("halt:" + reason);
            }

            public void OnFormationCleared(int castInstanceId, EBossInterruptReason reason)
            {
                Events.Add("cleared:" + reason);
            }
        }

        // 필수 참가자와 기믹과 가짜 의존성을 한 번에 엮는다
        private sealed class Rig
        {
            public MovingBoss               Boss      { get; } = new MovingBoss();
            public FakeRoster               Roster    { get; } = new FakeRoster();
            public FakeArena                Arena     { get; } = new FakeArena();
            public PartyQuery               Query     { get; } = new PartyQuery();
            public FakeDamageResolver       Damage    { get; } = new FakeDamageResolver();
            public FakeBossVitals           Vitals    { get; } = new FakeBossVitals();
            public ScriptedRandom           Random    { get; } = new ScriptedRandom();
            public FakeDisplacer            Displacer { get; } = new FakeDisplacer();
            public FakeHazard               Hazard    { get; } = new FakeHazard();
            public Recorder                 Listener  { get; } = new Recorder();
            public WolbaekTalismanSpec      Spec      { get; }
            public WolbaekTalismanFormation Gimmick   { get; }

            public int DamageCount => Damage.Requests.Count;

            public Rig(int partyCount, bool isHard, bool useHazard = true, Vector3 bossStart = default)
            {
                for (int i = 0; i < partyCount; ++i)
                {
                    AddParticipant(FIRST_PLAYER + i);
                }

                Boss.Position = bossStart;
                Spec          = MakeSpec(isHard);
                Gimmick       = new WolbaekTalismanFormation(Spec, Boss, HREF, Vitals, Roster, Arena, Boss, Damage, Query, Random,
                    Displacer, useHazard ? Hazard : null, Listener);
            }

            public Participant AddParticipant(int combatId)
            {
                FakeCombatTarget target = new FakeCombatTarget { CombatId = combatId, Faction = ECombatFaction.Player };
                Participant member = new Participant { Target = target };
                Roster.Members.Add(member);
                Query.Players.Add(target);
                return member;
            }

            public Participant Member(int combatId)
            {
                for (int i = 0; i < Roster.Members.Count; ++i)
                {
                    if (Roster.Members[i].Target.CombatId == combatId)
                    {
                        return Roster.Members[i];
                    }
                }

                return null;
            }

            // 체력을 경계로 내리고 첫 틱에서 진입시킨다
            public void Enter(double serverTime = 0.0)
            {
                Vitals.HealthRate = TRIGGER_RATE;
                Gimmick.Tick(serverTime, 1);
            }

            // 모든 필수 참가자를 자기 구조물 중심에 세운다
            public void PlaceAll()
            {
                for (int i = 0; i < Gimmick.Structures.Count; ++i)
                {
                    Member(Gimmick.Structures[i].AssignedCombatId).Target.Position = Gimmick.Structures[i].Center;
                }
            }

            public Participant Occupant(int structureIndex)
            {
                return Member(Gimmick.Structures[structureIndex].AssignedCombatId);
            }

            public Vector3 Center(int structureIndex)
            {
                return Gimmick.Structures[structureIndex].Center;
            }

            // 구조물 중심에서 기준점 반대쪽(바깥)을 향하는 단위 방향
            public Vector3 Outward(int structureIndex)
            {
                return Center(structureIndex).normalized;
            }

            public void Tick(double serverTime)
            {
                Gimmick.Tick(serverTime, 1);
            }
        }

        private static WolbaekTalismanSpec MakeSpec(bool isHard)
        {
            WolbaekTalismanAttackSpec attack = isHard
                ? new WolbaekTalismanAttackSpec(7f, 10f, 13f, 3f, 1.2f, 2f, DAMAGE_REF, PUSH_DISTANCE, true)
                : null;
            return new WolbaekTalismanSpec(TRIGGER_RATE, NEXT_RATE, 3f, 5f, 6f, 10f, 3f, attack);
        }

        /****************************************
        *              Entry
        ****************************************/

        [Test]
        public void Tick_AboveTrigger_StaysDormant()
        {
            Rig r = new Rig(2, true);
            r.Vitals.HealthRate = 0.8f;

            r.Tick(0.0);

            Assert.AreEqual(EWolbaekTalismanState.Dormant, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.ConsumeCleanupRequest());
            Assert.IsFalse(r.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(r.Gimmick.BlocksSelection);
        }

        [Test]
        public void Tick_AtTrigger_EntersTransitionAndRequestsCleanupOnce()
        {
            Rig r = new Rig(2, true);

            r.Enter();

            Assert.AreEqual(EWolbaekTalismanState.Transition, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.ConsumeCleanupRequest());
            Assert.IsFalse(r.Gimmick.ConsumeCleanupRequest());
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.AreNotEqual(CombatIds.NONE, r.Gimmick.CastInstanceId);
            Assert.AreEqual("start", r.Listener.Events[0]);
        }

        [Test]
        public void Tick_AtTrigger_SuspendsAmbientHazardOnce()
        {
            Rig r = new Rig(2, true);

            r.Enter();
            r.Tick(1.0);

            Assert.AreEqual(1, r.Hazard.SuspendCount);
            Assert.AreEqual(0, r.Hazard.Resumes.Count);
        }

        [Test]
        public void Tick_SecondPhase_NeverStartsAndReleasesBoundary()
        {
            Rig r = new Rig(2, true);
            r.Vitals.HealthRate = TRIGGER_RATE;

            r.Gimmick.Tick(0.0, 2);

            Assert.AreEqual(EWolbaekTalismanState.Completed, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.AreEqual(25f, r.Gimmick.CapDamage(100f, 100f, 25f), 0.0001f);
        }

        [Test]
        public void Tick_AfterCompletion_NeverStartsAgain()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(8.0);
            r.Tick(14.0);
            Assert.AreEqual(EWolbaekTalismanState.Completed, r.Gimmick.State);
            r.Gimmick.ConsumeCleanupRequest();

            r.Vitals.HealthRate = TRIGGER_RATE;
            r.Tick(20.0);

            Assert.AreEqual(EWolbaekTalismanState.Completed, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.ConsumeCleanupRequest());
            Assert.AreEqual(1, r.Listener.Events.FindAll(e => e == "start").Count);
        }

        [Test]
        public void Tick_ArenaUnavailable_HaltsInsteadOfSkippingAndKeepsBossProtected()
        {
            Rig r = new Rig(2, true, true, new Vector3(8f, 0f, 0f));
            r.Arena.Available = false;
            LogAssert.Expect(LogType.Error, new Regex("전장 정보"));

            r.Enter();

            Assert.AreEqual(EWolbaekTalismanState.Halted, r.Gimmick.State);
            Assert.AreEqual(EWolbaekHaltReason.ArenaUnavailable, r.Gimmick.HaltReason);
            Assert.IsTrue(r.Gimmick.ConsumeCleanupRequest());
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(75f, 100f, 10f));
            CollectionAssert.DoesNotContain(r.Listener.Events, "start");
            CollectionAssert.Contains(r.Listener.Events, "halt:ArenaUnavailable");
            Assert.AreEqual(0, r.Boss.MoveCalls);
            Assert.AreEqual(1, r.Hazard.SuspendCount);

            r.Tick(5.0);
            r.Tick(20.0);
            Assert.AreEqual(EWolbaekTalismanState.Halted, r.Gimmick.State);
            Assert.AreEqual(0, r.Listener.Attacks.Count);
            Assert.AreEqual(0, r.DamageCount);
        }

        [Test]
        public void Tick_NoParticipants_FailsAsWipeWithoutStartNotice()
        {
            Rig r = new Rig(0, true);

            r.Enter();

            Assert.AreEqual(EWolbaekTalismanState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekFormationFail.Wipe, r.Gimmick.FailReason);
            CollectionAssert.DoesNotContain(r.Listener.Events, "start");
        }

        /****************************************
        *            Boundary Cap
        ****************************************/

        [Test]
        public void CapDamage_BeforeTrigger_StopsExactlyAtBoundaryAndDiscardsExcess()
        {
            Rig r = new Rig(2, true);
            r.Vitals.HealthRate = 1f;

            Assert.AreEqual(25f, r.Gimmick.CapDamage(100f, 100f, 40f), 0.0001f);
            Assert.AreEqual(5f, r.Gimmick.CapDamage(80f, 100f, 10f), 0.0001f);
            Assert.AreEqual(5f, r.Gimmick.CapDamage(90f, 100f, 5f), 0.0001f);
        }

        [Test]
        public void CapDamage_AtBoundaryBeforeEntry_DiscardsEverything()
        {
            Rig r = new Rig(2, true);
            r.Vitals.HealthRate = TRIGGER_RATE;

            Assert.AreEqual(0f, r.Gimmick.CapDamage(75f, 100f, 10f));
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        public void CapDamage_NonPositiveDamage_ReturnsZero(float damage)
        {
            Rig r = new Rig(2, true);

            Assert.AreEqual(0f, r.Gimmick.CapDamage(100f, 100f, damage));
        }

        [Test]
        public void CapDamage_DuringTransitionAndActive_DiscardsEverything()
        {
            Rig r = new Rig(2, true);
            r.Enter();
            Assert.AreEqual(0f, r.Gimmick.CapDamage(75f, 100f, 50f));
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);

            r.PlaceAll();
            r.Tick(3.0);

            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(75f, 100f, 50f));
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
        }

        [Test]
        public void CapDamage_CounterWindow_StopsAtNextBoundary()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(8.0);
            Assert.AreEqual(EWolbaekTalismanState.Counter, r.Gimmick.State);

            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.AreEqual(25f, r.Gimmick.CapDamage(75f, 100f, 40f), 0.0001f);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(50f, 100f, 5f));

            r.Vitals.HealthRate = NEXT_RATE;
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
        }

        [Test]
        public void CapDamage_AfterCounterWindow_PassesDamageThrough()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(8.0);
            r.Tick(14.0);

            Assert.AreEqual(EWolbaekTalismanState.Completed, r.Gimmick.State);
            Assert.AreEqual(40f, r.Gimmick.CapDamage(50f, 100f, 40f), 0.0001f);
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
        }

        /****************************************
        *          Seat Assignment
        ****************************************/

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void Enter_AssignsOneDistinctStructurePerParticipantWithDistinctColorAndSymbol(int count)
        {
            Rig r = new Rig(count, true);

            r.Enter();

            Assert.AreEqual(count, r.Gimmick.RequiredCount);
            Assert.AreEqual(count, r.Gimmick.Structures.Count);

            HashSet<int> directions = new HashSet<int>();
            HashSet<EWolbaekTalismanColor> colors = new HashSet<EWolbaekTalismanColor>();
            HashSet<int> symbols = new HashSet<int>();
            HashSet<int> assigned = new HashSet<int>();
            for (int i = 0; i < r.Gimmick.Structures.Count; ++i)
            {
                WolbaekTalismanStructure structure = r.Gimmick.Structures[i];
                directions.Add(structure.DirectionIndex);
                colors.Add(structure.Color);
                symbols.Add(structure.SymbolId);
                assigned.Add(structure.AssignedCombatId);
                Assert.AreEqual(10f, Vector3.Distance(Vector3.zero, structure.Center), 0.0001f);
                Assert.AreEqual(3f, structure.Radius);
            }

            Assert.AreEqual(count, directions.Count);
            Assert.AreEqual(count, colors.Count);
            Assert.AreEqual(count, symbols.Count);
            Assert.AreEqual(count, assigned.Count);
            for (int i = 0; i < count; ++i)
            {
                Assert.IsTrue(assigned.Contains(FIRST_PLAYER + i));
            }
        }

        [Test]
        public void Enter_TwoParticipants_UseOppositeStructures()
        {
            for (int pick = 0; pick < 2; ++pick)
            {
                Rig r = new Rig(2, true);
                r.Random.Ints.Enqueue(1);
                r.Random.Ints.Enqueue(pick);

                r.Enter();

                int first  = r.Gimmick.Structures[0].DirectionIndex;
                int second = r.Gimmick.Structures[1].DirectionIndex;
                Assert.AreEqual(2, Math.Abs(first - second));
            }
        }

        [Test]
        public void Enter_OneParticipant_UsesTheChosenPointOfTheFour()
        {
            for (int pick = 0; pick < 4; ++pick)
            {
                Rig r = new Rig(1, true);
                r.Random.Ints.Enqueue(pick);

                r.Enter();

                Assert.AreEqual(1, r.Gimmick.Structures.Count);
                Assert.AreEqual(pick, r.Gimmick.Structures[0].DirectionIndex);
            }
        }

        [Test]
        public void Enter_DeadParticipantAtStart_StillCountsAsRequired()
        {
            Rig r = new Rig(3, true);
            r.Member(FIRST_PLAYER + 2).Target.LifeState = EParticipantLifeState.Dead;

            r.Enter();

            Assert.AreEqual(3, r.Gimmick.RequiredCount);
            Assert.AreEqual(3, r.Gimmick.Structures.Count);
        }

        [Test]
        public void Enter_MoreThanFourParticipants_KeepsFirstFourAndLogsError()
        {
            Rig r = new Rig(5, true);
            LogAssert.Expect(LogType.Error, new Regex("상한"));

            r.Enter();

            Assert.AreEqual(4, r.Gimmick.RequiredCount);
        }

        [Test]
        public void Assignment_AfterDeathReviveAndReconnect_StaysTheSame()
        {
            Rig r = new Rig(3, true);
            r.Enter();
            r.PlaceAll();
            List<int> before = Snapshot(r);

            r.Tick(3.0);
            r.Occupant(0).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(3.25);
            r.Occupant(0).Target.LifeState = EParticipantLifeState.Alive;
            r.Occupant(1).Connected = false;
            r.Tick(3.5);
            r.Occupant(1).Connected = true;
            r.Tick(3.75);

            CollectionAssert.AreEqual(before, Snapshot(r));
        }

        private static List<int> Snapshot(Rig r)
        {
            List<int> values = new List<int>();
            for (int i = 0; i < r.Gimmick.Structures.Count; ++i)
            {
                values.Add(r.Gimmick.Structures[i].AssignedCombatId);
                values.Add((int)r.Gimmick.Structures[i].Color);
                values.Add(r.Gimmick.Structures[i].DirectionIndex);
            }

            return values;
        }

        [Test]
        public void Enter_StartNoticeCarriesPhaseTimes()
        {
            Rig r = new Rig(2, true);

            r.Enter(10.0);

            Assert.AreEqual(10.0, r.Listener.Start.StartServerTime);
            Assert.AreEqual(13.0, r.Listener.Start.TransitionEndServerTime);
            Assert.AreEqual(5f, r.Listener.Start.HoldSeconds);
            Assert.AreEqual(BossHarness.SELF_ID, r.Listener.Start.BossCombatId);
            Assert.AreEqual(2, r.Listener.Start.Structures.Count);
        }

        /****************************************
        *               Hold
        ****************************************/

        [Test]
        public void Tick_DuringTransition_DoesNotMeasureHoldEvenWhenEveryoneIsInPlace()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();

            r.Tick(1.0);
            r.Tick(2.75);

            Assert.IsFalse(r.Gimmick.HasHold);
            Assert.AreEqual(EWolbaekTalismanState.Transition, r.Gimmick.State);
        }

        [Test]
        public void Tick_EveryoneInPlaceForFiveSeconds_SucceedsAfterTransition()
        {
            Rig r = new Rig(3, false);
            r.Enter();
            r.PlaceAll();

            r.Tick(3.0);
            Assert.IsTrue(r.Gimmick.HasHold);
            Assert.AreEqual(3.0, r.Gimmick.HoldStartServerTime);

            r.Tick(7.75);
            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);

            r.Tick(8.0);
            Assert.AreEqual(EWolbaekTalismanState.Counter, r.Gimmick.State);
            Assert.AreEqual(14.0, r.Gimmick.CounterEndServerTime);
        }

        [Test]
        public void Tick_LateArrival_StartsMeasuringOnlyWhenEveryoneIsInPlace()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();
            Vector3 lateSpot = r.Occupant(1).Target.Position;
            r.Occupant(1).Target.Position = Vector3.zero;

            r.Tick(3.0);
            r.Tick(6.0);
            Assert.IsFalse(r.Gimmick.HasHold);

            r.Occupant(1).Target.Position = lateSpot;
            r.Tick(6.25);
            Assert.AreEqual(6.25, r.Gimmick.HoldStartServerTime);

            r.Tick(11.0);
            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
            r.Tick(11.25);
            Assert.AreEqual(EWolbaekTalismanState.Counter, r.Gimmick.State);
        }

        [Test]
        public void Tick_StandingInAnotherStructure_DoesNotCount()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();
            r.Occupant(0).Target.Position = r.Center(1);

            r.Tick(3.0);

            Assert.IsFalse(r.Gimmick.HasHold);
        }

        [Test]
        public void Tick_OnStructureEdge_StillCounts()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Occupant(0).Target.Position = r.Center(0) + (Vector3.up * 5f) + (Vector3.forward * 3f);

            r.Tick(3.0);

            Assert.IsTrue(r.Gimmick.HasHold);
        }

        [Test]
        public void Tick_LeavingTheStructure_ResetsHoldAndRestartsFromZero()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            Vector3 spot = r.Occupant(0).Target.Position;
            int leaver = r.Occupant(0).Target.CombatId;

            r.Occupant(0).Target.Position = spot + (Vector3.right * 4f);
            r.Tick(6.0);
            Assert.IsFalse(r.Gimmick.HasHold);
            CollectionAssert.Contains(r.Listener.Events, "reset:Left:" + leaver);

            r.Occupant(0).Target.Position = spot;
            r.Tick(6.25);
            Assert.AreEqual(6.25, r.Gimmick.HoldStartServerTime);

            r.Tick(8.0);
            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
            r.Tick(11.25);
            Assert.AreEqual(EWolbaekTalismanState.Counter, r.Gimmick.State);
        }

        [Test]
        public void Tick_ParticipantDies_ResetsHoldWithDiedReason()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            int victim = r.Occupant(1).Target.CombatId;

            r.Occupant(1).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(4.0);

            Assert.IsFalse(r.Gimmick.HasHold);
            CollectionAssert.Contains(r.Listener.Events, "reset:Died:" + victim);
            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
        }

        [Test]
        public void Tick_ParticipantDisconnects_ResetsHoldAndReconnectJoinsSameSeat()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            int absent = r.Occupant(0).Target.CombatId;

            r.Occupant(0).Connected = false;
            r.Tick(4.0);
            CollectionAssert.Contains(r.Listener.Events, "reset:Disconnected:" + absent);
            Assert.IsFalse(r.Gimmick.HasHold);

            r.Occupant(0).Connected = true;
            r.Tick(5.0);

            Assert.IsTrue(r.Gimmick.HasHold);
            Assert.AreEqual(5.0, r.Gimmick.HoldStartServerTime);
            Assert.AreEqual(absent, r.Gimmick.Structures[0].AssignedCombatId);
        }

        [Test]
        public void Tick_RevivedParticipant_MustBeBackInPlaceBeforeHoldResumes()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            Vector3 spot = r.Occupant(0).Target.Position;

            r.Occupant(0).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(4.0);
            r.Occupant(0).Target.LifeState = EParticipantLifeState.Alive;
            r.Occupant(0).Target.Position = Vector3.zero;
            r.Tick(5.0);
            Assert.IsFalse(r.Gimmick.HasHold);

            r.Occupant(0).Target.Position = spot;
            r.Tick(6.0);
            Assert.IsTrue(r.Gimmick.HasHold);
        }

        [Test]
        public void Tick_FiveSecondsCompleteInSameTickAsDeath_DeathWinsAndNoSuccess()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);

            r.Occupant(1).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(8.0);

            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.HasHold);
            Assert.IsFalse(r.Listener.Events.Exists(e => e.StartsWith("success")));
        }

        [Test]
        public void Tick_FiveSecondsCompleteInSameTickAsDisconnect_ResetsInsteadOfSucceeding()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);

            r.Occupant(0).Connected = false;
            r.Tick(8.0);

            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
        }

        /****************************************
        *               Failure
        ****************************************/

        [Test]
        public void Tick_RequiredParticipantCannotRevive_FailsAsUnrecoverable()
        {
            Rig r = new Rig(2, true);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);

            r.Occupant(0).Target.LifeState = EParticipantLifeState.Spectating;
            r.Occupant(0).Revivable = false;
            r.Tick(3.25);

            Assert.AreEqual(EWolbaekTalismanState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekFormationFail.Unrecoverable, r.Gimmick.FailReason);
            CollectionAssert.Contains(r.Listener.Events, "fail:Unrecoverable");
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
        }

        [Test]
        public void Tick_RequiredParticipantLeavesRoster_FailsAsUnrecoverable()
        {
            Rig r = new Rig(2, true);
            r.Enter();

            r.Occupant(1).Present = false;
            r.Tick(1.0);

            Assert.AreEqual(EWolbaekFormationFail.Unrecoverable, r.Gimmick.FailReason);
        }

        [Test]
        public void Tick_DeadButRevivableParticipant_DoesNotFail()
        {
            Rig r = new Rig(2, true);
            r.Enter();

            r.Occupant(0).Target.LifeState = EParticipantLifeState.ReviveWaiting;
            r.Tick(1.0);

            Assert.AreEqual(EWolbaekTalismanState.Transition, r.Gimmick.State);
        }

        [Test]
        public void Tick_EveryRequiredParticipantDead_FailsAsWipeEvenDuringTransition()
        {
            Rig r = new Rig(2, true);
            r.Enter();

            r.Member(FIRST_PLAYER).Target.LifeState = EParticipantLifeState.Dead;
            r.Member(FIRST_PLAYER + 1).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(1.0);

            Assert.AreEqual(EWolbaekFormationFail.Wipe, r.Gimmick.FailReason);
        }

        [Test]
        public void Tick_AfterFailure_NoFurtherEventsOrDamage()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Occupant(0).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(3.25);
            int events = r.Listener.Events.Count;
            int damage = r.DamageCount;

            r.Tick(9.0);
            r.Tick(20.0);

            Assert.AreEqual(events, r.Listener.Events.Count);
            Assert.AreEqual(damage, r.DamageCount);
        }

        [Test]
        public void Tick_FailureWithPendingAttack_CancelsItAndNeverStrikes()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            Assert.AreEqual(1, r.Listener.Attacks.Count);

            r.Occupant(0).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(3.5);
            r.Tick(5.0);

            CollectionAssert.Contains(r.Listener.Events, "cancel:1");
            Assert.AreEqual(0, r.Listener.Hits.Count);
            Assert.AreEqual(0, r.DamageCount);
        }

        /****************************************
        *            Hard Attacks
        ****************************************/

        [Test]
        public void Tick_Hard_NoAttackBeforeTransitionEndsEvenIfNobodyArrived()
        {
            Rig r = new Rig(2, true);
            r.Enter();

            r.Tick(1.0);
            r.Tick(2.75);

            Assert.AreEqual(0, r.Listener.Attacks.Count);
            Assert.AreEqual(0, r.DamageCount);
        }

        [Test]
        public void Tick_Hard_AttackStartsAtTransitionEndWithoutWaitingForArrival()
        {
            Rig r = new Rig(2, true);
            r.Enter();

            r.Tick(3.0);

            Assert.AreEqual(1, r.Listener.Attacks.Count);
            Assert.AreEqual(EWolbaekTalismanAttackKind.Ring, r.Listener.Attacks[0].Kind);
            Assert.AreEqual(3.0, r.Listener.Attacks[0].TelegraphServerTime);
            Assert.AreEqual(3.0 + 1.2f, r.Listener.Attacks[0].StrikeServerTime, 0.0001);
        }

        [Test]
        public void Tick_Hard_FollowsRingLineRingScheduleWithFixedGaps()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();

            r.Tick(3.0);
            r.Tick(4.0);
            Assert.AreEqual(0, r.Listener.Hits.Count);
            r.Tick(4.25);
            Assert.AreEqual(1, r.Listener.Hits.Count);
            Assert.AreEqual(1, r.Listener.Attacks.Count);

            r.Tick(5.0);
            Assert.AreEqual(2, r.Listener.Attacks.Count);
            Assert.AreEqual(EWolbaekTalismanAttackKind.Line, r.Listener.Attacks[1].Kind);
            Assert.AreEqual(5.0, r.Listener.Attacks[1].TelegraphServerTime);
            r.Tick(6.0);
            Assert.AreEqual(1, r.Listener.Hits.Count);
            r.Tick(6.25);
            Assert.AreEqual(2, r.Listener.Hits.Count);

            r.Tick(7.0);
            Assert.AreEqual(3, r.Listener.Attacks.Count);
            Assert.AreEqual(EWolbaekTalismanAttackKind.Ring, r.Listener.Attacks[2].Kind);
            Assert.AreEqual(7.0, r.Listener.Attacks[2].TelegraphServerTime);
        }

        [Test]
        public void Tick_Hard_CoarseTickProcessesEveryDueAttackInOrder()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();

            r.Tick(10.0);

            Assert.AreEqual(4, r.Listener.Attacks.Count);
            Assert.AreEqual(3, r.Listener.Hits.Count);
            List<string> order = r.Listener.Events.FindAll(e => e.StartsWith("telegraph") || e.StartsWith("strike"));
            CollectionAssert.AreEqual(new[]
            {
                "telegraph:Ring", "strike:1", "telegraph:Line", "strike:2", "telegraph:Ring", "strike:3", "telegraph:Line"
            }, order);
        }

        [Test]
        public void Tick_Hard_ResumesAmbientHazardAtTransitionEndTime()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();

            r.Tick(2.75);
            Assert.AreEqual(0, r.Hazard.Resumes.Count);

            r.Tick(3.25);
            CollectionAssert.AreEqual(new[] { 3.0 }, r.Hazard.Resumes);

            r.Tick(4.0);
            Assert.AreEqual(1, r.Hazard.Resumes.Count);
        }

        [Test]
        public void Tick_Hard_WithoutHazardConnection_StillRuns()
        {
            Rig r = new Rig(1, true, false);
            r.Enter();
            r.PlaceAll();

            r.Tick(3.0);

            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Attacks.Count);
        }

        [Test]
        public void Ring_Inner_HitsOnlyThePlayerInTheInnerBand()
        {
            Rig r = new Rig(2, true);
            r.Enter();
            r.PlaceAll();
            r.Occupant(0).Target.Position = r.Center(0) - (r.Outward(0) * INNER_HALF_OFFSET);
            r.Occupant(1).Target.Position = r.Center(1) + (r.Outward(1) * INNER_HALF_OFFSET);

            r.Random.Ints.Enqueue(0);
            r.Tick(3.0);
            r.Tick(4.25);

            Assert.AreEqual(1, r.DamageCount);
            Assert.AreEqual(r.Occupant(0).Target.CombatId, r.Damage.Requests[0].HitTargetId);
            Assert.AreEqual(7f, r.Listener.Attacks[0].InnerRadius);
            Assert.AreEqual(10f, r.Listener.Attacks[0].OuterRadius);
        }

        [Test]
        public void Ring_Outer_HitsOnlyThePlayerInTheOuterBand()
        {
            Rig r = new Rig(2, true);
            r.Enter();
            r.PlaceAll();
            r.Occupant(0).Target.Position = r.Center(0) - (r.Outward(0) * INNER_HALF_OFFSET);
            r.Occupant(1).Target.Position = r.Center(1) + (r.Outward(1) * INNER_HALF_OFFSET);

            r.Random.Ints.Enqueue(1);
            r.Tick(3.0);
            r.Tick(4.25);

            Assert.AreEqual(1, r.DamageCount);
            Assert.AreEqual(r.Occupant(1).Target.CombatId, r.Damage.Requests[0].HitTargetId);
            Assert.AreEqual(10f, r.Listener.Attacks[0].InnerRadius);
            Assert.AreEqual(13f, r.Listener.Attacks[0].OuterRadius);
        }

        [Test]
        public void Ring_Hit_CarriesDamageFlagsAndPushesOutwardOneMeter()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();
            r.Occupant(0).Target.Position = r.Center(0) - (r.Outward(0) * INNER_HALF_OFFSET);

            r.Random.Ints.Enqueue(0);
            r.Tick(3.0);
            r.Tick(4.25);

            Assert.AreEqual(1, r.DamageCount);
            DamageRequest request = r.Damage.Requests[0];
            Assert.AreEqual(BossHarness.SELF_ID, request.AttackerId);
            Assert.AreEqual(FIRST_PLAYER, request.HitTargetId);
            Assert.AreEqual(DAMAGE_REF * HREF, request.RawDamage, 0.001f);
            Assert.IsTrue(request.IsUnblockable);
            Assert.AreEqual(r.Gimmick.CastInstanceId, request.CastInstanceId);
            Assert.AreEqual(1, r.Displacer.Pushes.Count);
            Assert.AreEqual(PUSH_DISTANCE, r.Displacer.Pushes[0].Distance);
            Assert.AreEqual(1f, Vector3.Dot(r.Outward(0), r.Displacer.Pushes[0].Direction), 0.0001f);
        }

        [Test]
        public void Ring_SharedActionNumber_HitsEachTargetOnce()
        {
            Rig r = new Rig(2, true);
            r.Enter();
            r.PlaceAll();
            r.Occupant(0).Target.Position = r.Center(0) - (r.Outward(0) * INNER_HALF_OFFSET);
            r.Occupant(1).Target.Position = r.Center(1) - (r.Outward(1) * INNER_HALF_OFFSET);

            r.Random.Ints.Enqueue(0);
            r.Tick(3.0);
            r.Tick(4.25);

            Assert.AreEqual(2, r.DamageCount);
            Assert.AreEqual(r.Damage.Requests[0].ActionInstanceId, r.Damage.Requests[1].ActionInstanceId);
            Assert.AreEqual(2, r.Listener.Hits[0]);
        }

        [Test]
        public void Line_AimsAtTheParticipantStructureAndHitsInsideTheWidth()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(4.25);
            int before = r.DamageCount;

            Vector3 side = Vector3.Cross(Vector3.up, r.Outward(0));
            r.Occupant(0).Target.Position = r.Center(0) + (side * 1.4f);
            r.Tick(5.0);
            r.Tick(6.25);

            WolbaekTalismanAttack line = r.Listener.Attacks[1];
            Assert.AreEqual(EWolbaekTalismanAttackKind.Line, line.Kind);
            Assert.AreEqual(1f, Vector3.Dot(r.Outward(0), line.Direction), 0.0001f);
            Assert.AreEqual(3f, line.Width);
            Assert.AreEqual(ARENA_RADIUS, line.Length);
            Assert.AreEqual(FIRST_PLAYER, line.TargetCombatId);
            Assert.AreEqual(before + 1, r.DamageCount);
            Assert.AreEqual("wolbaek_talisman_line", r.Damage.Requests[r.DamageCount - 1].SourceDefinitionId);
        }

        [Test]
        public void Line_MissesPlayersOutsideTheWidthAndBehindTheBoss()
        {
            Rig r = new Rig(2, true);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(4.25);
            r.Tick(5.0);
            int before = r.DamageCount;

            WolbaekTalismanAttack line = r.Listener.Attacks[1];
            Participant target = r.Member(line.TargetCombatId);
            Participant other  = (target == r.Occupant(0)) ? r.Occupant(1) : r.Occupant(0);
            int targetStructure = (target == r.Occupant(0)) ? 0 : 1;
            Vector3 side = Vector3.Cross(Vector3.up, line.Direction);
            target.Target.Position = r.Center(targetStructure) + (side * 1.6f);
            other.Target.Position  = -line.Direction * INNER_HALF_OFFSET;

            r.Tick(6.25);

            Assert.AreEqual(before, r.DamageCount);
            Assert.AreEqual(0, r.Listener.Hits[1]);
        }

        [Test]
        public void Line_SkipsDeadParticipantsWhenChoosingTheTarget()
        {
            Rig r = new Rig(2, true);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(4.25);

            r.Occupant(0).Target.LifeState = EParticipantLifeState.ReviveWaiting;
            r.Tick(5.0);

            Assert.AreEqual(2, r.Listener.Attacks.Count);
            Assert.AreEqual(r.Occupant(1).Target.CombatId, r.Listener.Attacks[1].TargetCombatId);
        }

        [Test]
        public void Line_NoLivingParticipant_SkipsThatAttackAndTheGimmickFailsAsWipe()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(4.25);

            r.Occupant(0).Target.LifeState = EParticipantLifeState.ReviveWaiting;
            r.Tick(5.0);

            Assert.AreEqual(1, r.Listener.Attacks.Count);
            Assert.AreEqual(EWolbaekTalismanState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekFormationFail.Wipe, r.Gimmick.FailReason);
        }

        [Test]
        public void Strike_PushedOutOfStructure_ResetsHoldButHitAloneDoesNot()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();
            Vector3 spot = r.Occupant(0).Target.Position;
            r.Random.Ints.Enqueue(1);
            r.Tick(3.0);
            Assert.IsTrue(r.Gimmick.HasHold);

            r.Tick(4.25);
            Assert.AreEqual(1, r.Listener.Hits[0]);
            Assert.IsTrue(r.Gimmick.HasHold);

            r.Occupant(0).Target.Position = spot + (spot.normalized * 4f);
            r.Tick(4.5);

            Assert.IsFalse(r.Gimmick.HasHold);
        }

        [Test]
        public void Easy_HasNoBossAttacksAndDoesNotResumeHazard()
        {
            Rig r = new Rig(2, false);
            r.Enter();
            r.PlaceAll();

            for (double t = 3.0; t < 8.0; t += 0.25)
            {
                r.Tick(t);
            }

            Assert.AreEqual(0, r.Listener.Attacks.Count);
            Assert.AreEqual(0, r.DamageCount);
            Assert.AreEqual(0, r.Hazard.Resumes.Count);
            Assert.AreEqual(1, r.Hazard.SuspendCount);
        }

        /****************************************
        *               Success
        ****************************************/

        [Test]
        public void Success_CancelsPendingAttackAndDropsLaterDamage()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(4.25);
            r.Tick(5.0);
            r.Tick(6.25);
            r.Tick(7.0);
            int damageBefore = r.DamageCount;
            Assert.AreEqual(3, r.Listener.Attacks.Count);

            r.Tick(8.0);

            Assert.AreEqual(EWolbaekTalismanState.Counter, r.Gimmick.State);
            CollectionAssert.Contains(r.Listener.Events, "cancel:3");

            r.Tick(9.0);
            r.Tick(12.0);
            Assert.AreEqual(3, r.Listener.Attacks.Count);
            Assert.AreEqual(damageBefore, r.DamageCount);
        }

        [Test]
        public void Success_ReleasesInvulnerabilityAndKeepsSelectionBlockedForSixSeconds()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);

            r.Tick(8.0);

            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);

            r.Tick(13.75);
            Assert.IsTrue(r.Gimmick.BlocksSelection);

            r.Tick(14.0);
            Assert.AreEqual(EWolbaekTalismanState.Completed, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.BlocksSelection);
            Assert.IsFalse(r.Gimmick.DefersPhaseTransition);
            CollectionAssert.Contains(r.Listener.Events, "counterEnd@14");
        }

        [Test]
        public void Success_NoticeCarriesCounterEnd()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);

            r.Tick(8.5);

            CollectionAssert.Contains(r.Listener.Events, "success@8.5/14.5");
        }

        /****************************************
        *               Stop
        ****************************************/

        [Test]
        public void Stop_DuringActive_CancelsAttackClearsDisplayAndSuspendsHazard()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            int suspendBefore = r.Hazard.SuspendCount;

            r.Gimmick.Stop(EBossInterruptReason.Death);

            Assert.AreEqual(EWolbaekTalismanState.Stopped, r.Gimmick.State);
            CollectionAssert.Contains(r.Listener.Events, "cancel:1");
            CollectionAssert.Contains(r.Listener.Events, "cleared:Death");
            Assert.AreEqual(suspendBefore + 1, r.Hazard.SuspendCount);
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);

            r.Tick(10.0);
            Assert.AreEqual(0, r.DamageCount);
        }

        [Test]
        public void Stop_BeforeEntry_JustStopsWithoutNotice()
        {
            Rig r = new Rig(1, true);

            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);

            Assert.AreEqual(EWolbaekTalismanState.Stopped, r.Gimmick.State);
            Assert.AreEqual(0, r.Listener.Events.Count);
            Assert.AreEqual(0, r.Hazard.SuspendCount);
        }

        [Test]
        public void Stop_DuringCounter_DoesNotRepeatClearNotice()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(8.0);

            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);

            Assert.AreEqual(EWolbaekTalismanState.Stopped, r.Gimmick.State);
            Assert.IsFalse(r.Listener.Events.Exists(e => e.StartsWith("cleared")));
        }

        [Test]
        public void Stop_AfterCompletion_DoesNothing()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(8.0);
            r.Tick(14.0);

            r.Gimmick.Stop(EBossInterruptReason.Death);

            Assert.AreEqual(EWolbaekTalismanState.Completed, r.Gimmick.State);
        }

        /****************************************
        *             Snapshot
        ****************************************/

        [Test]
        public void TryGetSnapshot_ReflectsPhaseHoldAndPendingAttack()
        {
            Rig r = new Rig(1, true);
            Assert.IsFalse(r.Gimmick.TryGetSnapshot(out WolbaekTalismanSnapshot _));

            r.Enter();
            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekTalismanSnapshot early));
            Assert.AreEqual(EWolbaekTalismanState.Transition, early.State);
            Assert.IsFalse(early.HasHold);
            Assert.IsFalse(early.HasAttack);

            r.PlaceAll();
            r.Tick(3.0);
            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekTalismanSnapshot active));
            Assert.AreEqual(EWolbaekTalismanState.Active, active.State);
            Assert.IsTrue(active.HasHold);
            Assert.AreEqual(3.0, active.HoldStartServerTime);
            Assert.IsTrue(active.HasAttack);
            Assert.AreEqual(1, active.Attack.AttackId);
            Assert.AreEqual(1, active.Start.Structures.Count);
        }

        [Test]
        public void TryGetSnapshot_AfterSuccess_ReportsCounterEnd()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(8.0);

            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekTalismanSnapshot counter));
            Assert.AreEqual(EWolbaekTalismanState.Counter, counter.State);
            Assert.AreEqual(14.0, counter.CounterEndServerTime);
            Assert.IsFalse(counter.HasAttack);
        }

        [Test]
        public void TryGetSnapshot_AfterCompletion_IsFalse()
        {
            Rig r = new Rig(1, false);
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            r.Tick(8.0);
            r.Tick(14.0);

            Assert.IsFalse(r.Gimmick.TryGetSnapshot(out WolbaekTalismanSnapshot _));
        }

        /****************************************
        *          Construction
        ****************************************/

        [Test]
        public void Constructor_HardSpecWithoutDisplacer_Throws()
        {
            Rig r = new Rig(1, true);

            Assert.Throws<ArgumentNullException>(() => new WolbaekTalismanFormation(MakeSpec(true), r.Boss, HREF, r.Vitals,
                r.Roster, r.Arena, r.Boss, r.Damage, r.Query, r.Random, null));
        }

        [Test]
        public void Constructor_EasySpecWithoutDisplacer_IsAllowed()
        {
            Rig r = new Rig(1, true);

            Assert.DoesNotThrow(() => new WolbaekTalismanFormation(MakeSpec(false), r.Boss, HREF, r.Vitals,
                r.Roster, r.Arena, r.Boss, r.Damage, r.Query, r.Random, null));
        }

        [Test]
        public void Constructor_WithoutMovementChannel_Throws()
        {
            Rig r = new Rig(1, true);

            Assert.Throws<ArgumentNullException>(() => new WolbaekTalismanFormation(MakeSpec(false), r.Boss, HREF, r.Vitals,
                r.Roster, r.Arena, null, r.Damage, r.Query, r.Random, null));
        }

        [Test]
        public void Spec_InvalidValues_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekTalismanSpec(0f, 0f, 3f, 5f, 6f, 10f, 3f, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekTalismanSpec(0.75f, 0.75f, 3f, 5f, 6f, 10f, 3f, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekTalismanSpec(0.75f, 0.5f, 3f, 0f, 6f, 10f, 3f, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekTalismanSpec(0.75f, 0.5f, 0f, 5f, 6f, 10f, 3f, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekTalismanSpec(0.75f, 0.5f, 3f, 5f, 6f, 3f, 3f, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekTalismanAttackSpec(7f, 10f, 13f, 3f, 1.2f, 1.2f, 0.12f, 1f, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekTalismanAttackSpec(10f, 10f, 13f, 3f, 1.2f, 2f, 0.12f, 1f, true));
        }

        /****************************************
        *          Move To Center
        ****************************************/

        // 전장 반경 18m에서 몸체 반경 2m를 뺀 16m 안쪽의 서로 다른 방향과 거리다
        private static readonly Vector3[] OffCenterStarts =
        {
            new Vector3(10f, 0f, 0f), new Vector3(-12f, 0f, 5f), new Vector3(0f, 0f, 15f), new Vector3(8f, 0f, -8f),
            new Vector3(15f, 0f, -6f)
        };

        private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 0.01f)
        {
            Assert.Less(Vector3.Distance(expected, actual), tolerance, $"expected {expected} but was {actual}");
        }

        [Test]
        public void Move_StartedAtCenter_NeverMovesAndKeepsTheThreeSecondTransition()
        {
            Rig r = new Rig(1, true);
            r.Enter();
            r.PlaceAll();

            r.Tick(1.0);
            r.Tick(2.75);
            Assert.AreEqual(EWolbaekTalismanState.Transition, r.Gimmick.State);
            Assert.AreEqual(0, r.Listener.Attacks.Count);

            r.Tick(3.0);
            Assert.AreEqual(0, r.Boss.MoveCalls);
            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
            Assert.AreEqual(3.0, r.Listener.Attacks[0].TelegraphServerTime);
        }

        [TestCaseSource(nameof(OffCenterStarts))]
        public void Move_FromOffCenter_MovesLinearlyAndArrivesWhenTheTransitionEnds(Vector3 start)
        {
            Rig r = new Rig(1, false, true, start);
            r.Enter();
            r.PlaceAll();

            r.Tick(1.5);
            AssertNear(Vector3.Lerp(start, Vector3.zero, 0.5f), r.Boss.Position);

            r.Tick(2.75);
            AssertNear(Vector3.Lerp(start, Vector3.zero, 2.75f / 3f), r.Boss.Position);
            Assert.AreEqual(EWolbaekTalismanState.Transition, r.Gimmick.State);

            r.Tick(3.0);
            AssertNear(Vector3.zero, r.Boss.Position, 0.05f);
            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
        }

        [TestCaseSource(nameof(OffCenterStarts))]
        public void Move_FromOffCenter_NeverMovesAwayFromCenterOrOutsideTheArena(Vector3 start)
        {
            Rig r = new Rig(1, false, true, start);
            r.Enter();
            r.PlaceAll();
            float previous = Vector3.Distance(start, Vector3.zero);

            for (double t = 0.25; t <= 3.0; t += 0.25)
            {
                r.Tick(t);
                float distance = Vector3.Distance(r.Boss.Position, Vector3.zero);
                Assert.LessOrEqual(distance, previous + 0.0001f);
                Assert.LessOrEqual(distance, ARENA_RADIUS);
                Assert.AreEqual(start.y, r.Boss.Position.y);
                previous = distance;
            }
        }

        [TestCaseSource(nameof(OffCenterStarts))]
        public void Move_Arrival_StructuresAndAttackCentersMatchTheFinalBossPosition(Vector3 start)
        {
            Rig r = new Rig(3, true, true, start);
            r.Enter();
            r.PlaceAll();

            r.Tick(3.0);

            AssertNear(r.Boss.Position, r.Listener.Start.Center, 0.05f);
            for (int i = 0; i < r.Gimmick.Structures.Count; ++i)
            {
                Assert.AreEqual(10f, Vector3.Distance(r.Boss.Position, r.Gimmick.Structures[i].Center), 0.05f);
            }

            AssertNear(r.Boss.Position, r.Listener.Attacks[0].Origin, 0.05f);
        }

        [TestCaseSource(nameof(OffCenterStarts))]
        public void Move_FromOffCenter_DoesNotBringForwardHoldOrAttackTimes(Vector3 start)
        {
            Rig r = new Rig(1, true, true, start);
            r.Enter();
            r.PlaceAll();

            r.Tick(2.75);
            Assert.IsFalse(r.Gimmick.HasHold);
            Assert.AreEqual(0, r.Listener.Attacks.Count);

            r.Tick(3.0);
            Assert.AreEqual(3.0, r.Gimmick.HoldStartServerTime);
            Assert.AreEqual(3.0, r.Listener.Attacks[0].TelegraphServerTime);
            Assert.AreEqual(3.0 + 1.2f, r.Listener.Attacks[0].StrikeServerTime, 0.0001);

            r.Tick(4.0);
            Assert.AreEqual(0, r.Listener.Hits.Count);
            r.Tick(4.25);
            Assert.AreEqual(1, r.Listener.Hits.Count);

            r.Tick(7.75);
            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
            r.Tick(8.0);
            Assert.AreEqual(EWolbaekTalismanState.Counter, r.Gimmick.State);
        }

        [Test]
        public void Move_SameTimeTickedRepeatedly_DoesNotMoveAgain()
        {
            Rig r = new Rig(1, false, true, new Vector3(10f, 0f, 0f));
            r.Enter();
            r.PlaceAll();

            r.Tick(1.0);
            int calls = r.Boss.MoveCalls;
            Vector3 position = r.Boss.Position;
            r.Tick(1.0);
            r.Tick(1.0);

            Assert.AreEqual(1, calls);
            Assert.AreEqual(calls, r.Boss.MoveCalls);
            AssertNear(position, r.Boss.Position, 0.0001f);
        }

        [Test]
        public void Move_BigTick_ReachesCenterInOneMoveWithoutOvershoot()
        {
            Rig r = new Rig(1, true, true, new Vector3(15f, 0f, -6f));
            r.Enter();
            r.PlaceAll();

            r.Tick(10.0);

            Assert.AreEqual(1, r.Boss.MoveCalls);
            AssertNear(Vector3.zero, r.Boss.Position, 0.05f);
            Assert.AreEqual(EWolbaekTalismanState.Active, r.Gimmick.State);
            Assert.AreEqual(4, r.Listener.Attacks.Count);
        }

        [Test]
        public void Move_BlockedByWall_HaltsAtTransitionEndWithoutStartingAttacks()
        {
            Rig r = new Rig(1, true, true, new Vector3(10f, 0f, 0f));
            r.Boss.WallX = 5f;
            r.Enter();
            r.PlaceAll();

            r.Tick(1.0);
            r.Tick(2.75);
            Assert.AreEqual(EWolbaekTalismanState.Transition, r.Gimmick.State);

            r.Tick(3.0);

            Assert.AreEqual(EWolbaekTalismanState.Halted, r.Gimmick.State);
            Assert.AreEqual(EWolbaekHaltReason.MovementBlocked, r.Gimmick.HaltReason);
            Assert.AreEqual(5f, r.Boss.Position.x, 0.01f);
            CollectionAssert.Contains(r.Listener.Events, "halt:MovementBlocked");
            Assert.IsFalse(r.Listener.Events.Exists(e => e.StartsWith("holdStart")));
            Assert.AreEqual(0, r.Listener.Attacks.Count);
            Assert.AreEqual(0, r.Hazard.Resumes.Count);
            Assert.IsFalse(r.Gimmick.HasHold);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(75f, 100f, 10f));
            Assert.IsFalse(r.Gimmick.TryGetSnapshot(out WolbaekTalismanSnapshot _));
        }

        [Test]
        public void Move_Halted_StaysQuietAndNeverSucceedsEvenWhenEveryoneIsInPlace()
        {
            Rig r = new Rig(1, true, true, new Vector3(10f, 0f, 0f));
            r.Boss.WallX = 5f;
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);
            int events = r.Listener.Events.Count;

            r.Tick(4.25);
            r.Tick(8.0);
            r.Tick(20.0);

            Assert.AreEqual(EWolbaekTalismanState.Halted, r.Gimmick.State);
            Assert.AreEqual(events, r.Listener.Events.Count);
            Assert.AreEqual(0, r.DamageCount);
        }

        [Test]
        public void Move_BossRefusesToMove_HaltsAtTransitionEnd()
        {
            Rig r = new Rig(1, false, true, new Vector3(6f, 0f, 0f));
            r.Boss.CanMove = false;
            r.Enter();
            r.PlaceAll();

            r.Tick(1.0);
            r.Tick(3.0);

            Assert.Greater(r.Boss.MoveCalls, 0);
            Assert.AreEqual(EWolbaekHaltReason.MovementBlocked, r.Gimmick.HaltReason);
        }

        [Test]
        public void Move_StopDuringTransition_NeverMovesAgain()
        {
            Rig r = new Rig(1, true, true, new Vector3(10f, 0f, 0f));
            r.Enter();
            r.PlaceAll();
            r.Tick(1.0);
            int calls = r.Boss.MoveCalls;
            Vector3 position = r.Boss.Position;

            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);
            r.Tick(2.0);
            r.Tick(3.0);

            Assert.AreEqual(calls, r.Boss.MoveCalls);
            AssertNear(position, r.Boss.Position, 0.0001f);
            Assert.AreEqual(EWolbaekTalismanState.Stopped, r.Gimmick.State);
            CollectionAssert.Contains(r.Listener.Events, "cleared:CombatEnd");
        }

        [Test]
        public void Move_HaltedThenStopped_DoesNotRepeatClearNotice()
        {
            Rig r = new Rig(1, true, true, new Vector3(10f, 0f, 0f));
            r.Boss.WallX = 5f;
            r.Enter();
            r.PlaceAll();
            r.Tick(3.0);

            r.Gimmick.Stop(EBossInterruptReason.Death);

            Assert.AreEqual(EWolbaekTalismanState.Stopped, r.Gimmick.State);
            Assert.IsFalse(r.Listener.Events.Exists(e => e.StartsWith("cleared")));
        }

        [Test]
        public void Move_ParticipantsAllDeadWhileMoving_FailsAsWipeAndStopsMoving()
        {
            Rig r = new Rig(1, true, true, new Vector3(10f, 0f, 0f));
            r.Enter();
            r.Tick(1.0);
            int calls = r.Boss.MoveCalls;

            r.Occupant(0).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(1.5);
            r.Tick(2.0);

            Assert.AreEqual(EWolbaekFormationFail.Wipe, r.Gimmick.FailReason);
            Assert.GreaterOrEqual(r.Boss.MoveCalls, calls);
            int afterFail = r.Boss.MoveCalls;
            r.Tick(3.0);
            Assert.AreEqual(afterFail, r.Boss.MoveCalls);
        }

        /****************************************
        *         Brain Integration
        ****************************************/

        // 두뇌에 실제 기믹을 얹고 진입 정리와 선택 차단과 전환 보류를 한 흐름으로 본다
        private sealed class BrainRig
        {
            public BossHarness              Harness  { get; }
            public WolbaekTalismanFormation Gimmick  { get; }
            public MovingBoss               Boss     { get; } = new MovingBoss();
            public Recorder                 Listener { get; } = new Recorder();

            public BrainRig(bool isHard, Vector3 bossStart = default)
            {
                Harness = new BossHarness(BossTestFixtures.MakeBoss(), null, BossTestFixtures.MakePattern("p1"));
                Harness.Factory.HitOffsets = new[] { 0.0 };

                FakeRoster roster = new FakeRoster();
                roster.Members.Add(new Participant { Target = Harness.Player });
                PartyQuery query = new PartyQuery();
                query.Players.Add(Harness.Player);

                // 두뇌가 보는 보스 몸체(Harness.Self)와 기믹이 움직이는 몸체(Boss)는 따로지만 전투 번호가 같다
                Boss.Position = bossStart;
                Gimmick = new WolbaekTalismanFormation(MakeSpec(isHard), Boss, HREF, Harness.Vitals, roster, new FakeArena(), Boss,
                    Harness.Damage, query, new ScriptedRandom(), new FakeDisplacer(), new FakeHazard(), Listener);
                Assert.IsTrue(Harness.Brain.AddGimmick(Gimmick));
                Assert.IsTrue(Harness.Brain.Begin(0.0));
            }

            public void Tick(double serverTime)
            {
                Harness.Brain.Tick(serverTime);
            }

            public void SitInSeat()
            {
                Harness.Player.Position = Gimmick.Structures[0].Center;
            }
        }

        [Test]
        public void Brain_Entry_CancelsRunningPatternAndStopsNewSelection()
        {
            BrainRig b = new BrainRig(false);
            b.Tick(0.0);
            Assert.AreEqual(EBossAttackState.Telegraph, b.Harness.Brain.AttackState);

            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);

            Assert.AreEqual(EWolbaekTalismanState.Transition, b.Gimmick.State);
            Assert.AreEqual(EBossAttackState.Ready, b.Harness.Brain.AttackState);
            Assert.IsTrue(b.Harness.Factory.Last.IsCancelled);
            Assert.AreEqual(EBossInterruptReason.GimmickEntry, b.Harness.Factory.Last.CancelReason);
            Assert.AreEqual(EBossInterruptReason.GimmickEntry, b.Harness.Effects.Cancels[0].Key);

            b.Tick(1.0);
            b.Tick(2.0);
            b.Tick(4.0);
            Assert.AreEqual(1, b.Harness.Factory.CreateCount);
        }

        [Test]
        public void Brain_Success_ResumesNormalPatternsOnlyAfterTheCounterWindow()
        {
            BrainRig b = new BrainRig(false);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);
            b.SitInSeat();

            b.Tick(3.25);
            b.Tick(8.25);
            Assert.AreEqual(EWolbaekTalismanState.Counter, b.Gimmick.State);

            b.Tick(10.0);
            b.Tick(14.0);
            Assert.AreEqual(1, b.Harness.Factory.CreateCount);

            b.Tick(14.25);
            b.Tick(14.5);
            Assert.AreEqual(EWolbaekTalismanState.Completed, b.Gimmick.State);
            Assert.AreEqual(2, b.Harness.Factory.CreateCount);
        }

        [Test]
        public void Brain_HealthReachesNextBoundaryInCounter_TransitionWaitsForCounterEnd()
        {
            BrainRig b = new BrainRig(false);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);
            b.SitInSeat();
            b.Tick(3.25);
            b.Tick(8.25);
            Assert.AreEqual(EWolbaekTalismanState.Counter, b.Gimmick.State);

            b.Harness.Vitals.HealthRate = NEXT_RATE;
            b.Tick(10.0);
            Assert.AreEqual(1, b.Harness.Brain.Phase);
            Assert.AreEqual(EBossPhaseState.Active, b.Harness.Brain.PhaseState);

            b.Tick(14.25);
            Assert.AreEqual(2, b.Harness.Brain.Phase);
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, b.Harness.Brain.PhaseState);
        }

        [Test]
        public void Brain_HealthCrossesBothBoundariesInOneHit_GimmickRunsBeforeTransition()
        {
            BrainRig b = new BrainRig(false);
            b.Tick(0.0);

            b.Harness.Vitals.HealthRate = 0.4f;
            b.Tick(0.25);

            Assert.AreEqual(EWolbaekTalismanState.Transition, b.Gimmick.State);
            Assert.AreEqual(1, b.Harness.Brain.Phase);
        }

        [Test]
        public void Brain_BossDeathDuringGimmick_StopsItAndClearsDisplay()
        {
            BrainRig b = new BrainRig(true);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);

            b.Harness.Vitals.IsHealthZero = true;
            b.Tick(0.5);

            Assert.AreEqual(EBossFlowState.Defeated, b.Harness.Brain.FlowState);
            Assert.AreEqual(EWolbaekTalismanState.Stopped, b.Gimmick.State);
            CollectionAssert.Contains(b.Listener.Events, "cleared:Death");
        }

        [Test]
        public void Brain_CombatEndDuringGimmick_StopsIt()
        {
            BrainRig b = new BrainRig(true);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);

            b.Harness.Brain.RequestCombatEnd();
            b.Tick(0.5);

            Assert.AreEqual(EBossFlowState.Ended, b.Harness.Brain.FlowState);
            CollectionAssert.Contains(b.Listener.Events, "cleared:CombatEnd");
        }

        [Test]
        public void Brain_OffCenterEntry_BossWalksToCenterWhileSelectionStaysBlocked()
        {
            BrainRig b = new BrainRig(false, new Vector3(12f, 0f, 0f));
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);
            Assert.AreEqual(0, b.Boss.MoveCalls);

            b.Tick(1.75);
            AssertNear(new Vector3(6f, 0f, 0f), b.Boss.Position);
            Assert.AreEqual(1, b.Harness.Factory.CreateCount);

            b.SitInSeat();
            b.Tick(3.25);
            AssertNear(Vector3.zero, b.Boss.Position, 0.05f);
            Assert.AreEqual(EWolbaekTalismanState.Active, b.Gimmick.State);
            Assert.AreEqual(1, b.Harness.Factory.CreateCount);
        }

        [Test]
        public void Brain_CombatEndWhileWalking_NoMoveRequestsAfterTheEnd()
        {
            BrainRig b = new BrainRig(true, new Vector3(12f, 0f, 0f));
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);
            b.Tick(1.0);
            Assert.Greater(b.Boss.MoveCalls, 0);
            int before = b.Boss.MoveCalls;

            b.Harness.Brain.RequestCombatEnd();
            b.Tick(1.25);
            b.Tick(2.0);
            b.Tick(3.5);

            Assert.AreEqual(EBossFlowState.Ended, b.Harness.Brain.FlowState);
            Assert.AreEqual(EWolbaekTalismanState.Stopped, b.Gimmick.State);
            Assert.AreEqual(before, b.Boss.MoveCalls);
            CollectionAssert.Contains(b.Listener.Events, "cleared:CombatEnd");
        }

        [Test]
        public void Brain_HaltedByBlockedMove_HoldsPhaseTransitionAndSelectionWithoutDeciding()
        {
            BrainRig b = new BrainRig(true, new Vector3(12f, 0f, 0f));
            b.Boss.WallX = 6f;
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);
            b.Tick(3.25);
            Assert.AreEqual(EWolbaekTalismanState.Halted, b.Gimmick.State);

            b.Harness.Vitals.HealthRate = 0.4f;
            b.Tick(4.0);
            b.Tick(20.0);

            Assert.AreEqual(1, b.Harness.Brain.Phase);
            Assert.AreEqual(1, b.Harness.Factory.CreateCount);
            Assert.AreEqual(EBossFlowState.Combat, b.Harness.Brain.FlowState);
            Assert.IsTrue(b.Gimmick.IsDamageBlocked);
        }
    }
}
