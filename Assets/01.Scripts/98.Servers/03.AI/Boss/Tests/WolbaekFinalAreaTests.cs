// System
using System;
using System.Collections.Generic;

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
    // 시각은 이진 분수로 넘겨 경계 판정에 부동소수 오차가 끼지 않게 한다.
    // 기본 시간표: 진입 t=0, 바닥 제거와 노란 여우불 생성과 첫 빨간 묶음 시작 t=4, 위치 고정 t=8, 폭발 t=10이고 묶음 사이 간격 0이라
    // 다음 묶음이 t=10에 시작해 t=14에 고정되고 t=16에 터진다. 참가자는 중앙 바닥(반경 8m) 안 서로 8m 이상 떨어진 네 곳에 서 있어
    // 빨간 폭발(반경 2m)이 서로 겹치지 않는다. 최대 체력 1000이라 단일 적중 피해는 600이다
    public class WolbaekFinalAreaTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  MAX_HEALTH   = 1000f;
        private const int    FIRST_PLAYER = 10;
        private const int    FIRST_YELLOW = 100;
        private const float  CENTRAL      = 8f;
        private const string BLAST_ID     = "wolbaek_final_blast";

        /****************************************
        *               Doubles
        ****************************************/

        private sealed class Participant
        {
            public FakeCombatTarget Target    { get; set; }
            public bool             Connected { get; set; } = true;
            public bool             Revivable { get; set; } = true;
        }

        private sealed class FakeRoster : IBossParticipantRoster, IBossParticipantHealth
        {
            public List<Participant> Members { get; } = new List<Participant>();

            public int CollectParticipants(List<int> combatIds)
            {
                for (int i = 0; i < Members.Count; ++i)
                {
                    combatIds.Add(Members[i].Target.CombatId);
                }

                return combatIds.Count;
            }

            public bool TryGetParticipant(int combatId, out BossParticipantInfo info)
            {
                for (int i = 0; i < Members.Count; ++i)
                {
                    Participant member = Members[i];
                    if (member.Target.CombatId == combatId)
                    {
                        info = new BossParticipantInfo(member.Target.LifeState, member.Connected, member.Revivable, member.Target.Position);
                        return true;
                    }
                }

                info = default;
                return false;
            }

            public bool TryGetMaxHealth(int combatId, out float maxHealth)
            {
                maxHealth = MAX_HEALTH;
                return true;
            }
        }

        private sealed class FakeArena : IBossArenaSource
        {
            public bool Available { get; set; } = true;

            public bool TryGetArena(out Vector3 center, out float radius)
            {
                center = Vector3.zero;
                radius = 18f;
                return Available;
            }
        }

        private sealed class FakeSites : IWolbaekFinalSiteSource
        {
            public List<Vector3> Centers { get; } = new List<Vector3>
            {
                new Vector3(3f, 0f, 3f), new Vector3(-3f, 0f, 3f), new Vector3(3f, 0f, -3f), new Vector3(-3f, 0f, -3f)
            };

            public int CollectYellowSites(List<Vector3> centers)
            {
                centers.AddRange(Centers);
                return centers.Count;
            }
        }

        private sealed class StillBoss : ICombatTarget
        {
            public int                   CombatId      => BossHarness.SELF_ID;
            public ECombatFaction        Faction       => ECombatFaction.Enemy;
            public EParticipantLifeState LifeState     => EParticipantLifeState.Alive;
            public bool                  IsSummon      => false;
            public int                   OwnerCombatId => CombatIds.NONE;
            public Vector3               Position      => Vector3.zero;
            public Vector3               Forward       => Vector3.forward;
            public float                 BodyRadius    => 2f;
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
                return GetLivingPlayers(results);
            }

            public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
            {
                return 0;
            }

            public bool TryGet(int combatId, out ICombatTarget target)
            {
                target = null;
                return false;
            }
        }

        private sealed class Log
        {
            public List<string> Lines { get; } = new List<string>();
        }

        private sealed class FakeFloor : IWolbaekFinalFloor
        {
            private readonly List<FakeCombatTarget> _players;

            public bool Accept   { get; set; } = true;
            public bool Removed  { get; private set; }
            public int  Requests { get; private set; }

            public FakeFloor(List<FakeCombatTarget> players)
            {
                _players = players;
            }

            public bool TryRemoveOuterFloor(int castInstanceId, double serverTime)
            {
                ++Requests;
                if (Accept)
                {
                    Removed = true;
                }

                return Accept;
            }

            public bool IsOnRemovedFloor(int combatId)
            {
                if (!Removed)
                {
                    return false;
                }

                for (int i = 0; i < _players.Count; ++i)
                {
                    if (_players[i].CombatId == combatId)
                    {
                        Vector3 p = _players[i].Position;
                        return Mathf.Sqrt((p.x * p.x) + (p.z * p.z)) > CENTRAL;
                    }
                }

                return false;
            }

            public bool TryGetSafeRevivePoint(int combatId, out Vector3 point)
            {
                point = new Vector3(1f, 0f, 1f);
                return true;
            }
        }

        private sealed class Kill
        {
            public int                CombatId;
            public EWolbaekFinalDeath Cause;
        }

        private sealed class FakeKiller : IWolbaekFinalKiller
        {
            private readonly List<FakeCombatTarget> _players;

            public List<Kill> Kills { get; } = new List<Kill>();

            public FakeKiller(List<FakeCombatTarget> players)
            {
                _players = players;
            }

            public bool TryKill(int combatId, int castInstanceId, EWolbaekFinalDeath cause)
            {
                for (int i = 0; i < _players.Count; ++i)
                {
                    if ((_players[i].CombatId == combatId) && (_players[i].LifeState == EParticipantLifeState.Alive))
                    {
                        _players[i].LifeState = EParticipantLifeState.Dead;
                        Kills.Add(new Kill { CombatId = combatId, Cause = cause });
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class FakeGuard : IWolbaekFinalGuard
        {
            private readonly Log _log;

            public int Calls { get; private set; }

            public FakeGuard(Log log)
            {
                _log = log;
            }

            public void GrantVictoryProtection(int castInstanceId, double serverTime)
            {
                ++Calls;
                _log.Lines.Add("guard");
            }
        }

        private sealed class FakeRegistry : IWolbaekYellowRegistry
        {
            private int _next = FIRST_YELLOW;

            public bool                 IssueFails    { get; set; }
            public bool                 RegisterFails { get; set; }
            public List<IDamageable>    Registered    { get; } = new List<IDamageable>();
            public List<int>            Unregistered  { get; } = new List<int>();

            public int IssueCombatId()
            {
                if (IssueFails)
                {
                    return CombatIds.NONE;
                }

                int issued = _next;
                ++_next;
                return issued;
            }

            public bool Register(IDamageable target)
            {
                if (RegisterFails)
                {
                    return false;
                }

                Registered.Add(target);
                return true;
            }

            public bool Unregister(int combatId)
            {
                Unregistered.Add(combatId);
                return true;
            }
        }

        private sealed class FakeRevive : IBossReviveSource
        {
            public Dictionary<int, double> Stamps { get; } = new Dictionary<int, double>();

            public bool TryGetLastReviveServerTime(int combatId, out double serverTime)
            {
                return Stamps.TryGetValue(combatId, out serverTime);
            }
        }

        private sealed class RecordingResolver : IDamageResolver
        {
            private int _nextId = 1;

            public List<DamageRequest> Requests { get; } = new List<DamageRequest>();

            public DamageResult Apply(in DamageRequest request)
            {
                Requests.Add(request);
                return DamageResult.Applied(1f, false, false);
            }

            public int IssueInstanceId()
            {
                int issued = _nextId;
                ++_nextId;
                return issued;
            }
        }

        // 25% 봉인 대역, 인계 대기와 반격 중을 따로 켠다
        private sealed class FakeHandoff : IBossGimmick, IBossBoundaryHandoff
        {
            public bool Pending   { get; set; }
            public bool Defers    { get; set; }
            public int  TakeCalls { get; private set; }

            public bool HasPendingHandoff       => Pending;
            public bool DefersPhaseTransition   => Defers || Pending;
            public bool BlocksSelection         => DefersPhaseTransition;

            public bool TryTakeHandoff()
            {
                if (!Pending)
                {
                    return false;
                }

                Pending = false;
                Defers  = false;
                ++TakeCalls;
                return true;
            }

            public void Tick(double serverTime, int phase)
            {
            }

            public bool TrySelect(BossBlackboard blackboard, out Shinshi.Data.BossPatternCatalogSO.PatternDefinition pattern)
            {
                pattern = null;
                return false;
            }

            public void OnCastExecuted(int castInstanceId, double serverTime)
            {
            }

            public void OnCastFinished(int castInstanceId, double serverTime)
            {
            }
        }

        private sealed class Recorder : IWolbaekFinalAreaListener
        {
            private readonly Log _log;

            public List<string>               Events     { get; } = new List<string>();
            public List<Vector3[]>            Locks      { get; } = new List<Vector3[]>();
            public List<WolbaekFinalBundle>   Bundles    { get; } = new List<WolbaekFinalBundle>();
            public List<int>                  Fallen     { get; } = new List<int>();
            public List<int[]>                Resolved   { get; } = new List<int[]>();
            public WolbaekFinalStart          Start      { get; private set; }

            public Recorder(Log log)
            {
                _log = log;
            }

            public int Count(string prefix)
            {
                int total = 0;
                for (int i = 0; i < Events.Count; ++i)
                {
                    if (Events[i].StartsWith(prefix))
                    {
                        ++total;
                    }
                }

                return total;
            }

            public void OnFinalStarted(in WolbaekFinalStart start)
            {
                Start = start;
                Events.Add("start");
            }

            public void OnOuterFloorRemoved(int castInstanceId, double serverTime, IReadOnlyList<int> fallen)
            {
                Fallen.AddRange(fallen);
                Events.Add("floor@" + serverTime);
            }

            public void OnYellowsSpawned(int castInstanceId, double serverTime, IReadOnlyList<WolbaekFinalYellow> yellows)
            {
                Events.Add("yellows:" + yellows.Count + "@" + serverTime);
            }

            public void OnYellowDestroyed(int castInstanceId, int yellowIndex, double serverTime, int remaining)
            {
                Events.Add("yellowDown:" + yellowIndex + ":" + remaining);
            }

            public void OnBundleStarted(int castInstanceId, in WolbaekFinalBundle bundle)
            {
                Bundles.Add(bundle);
                Events.Add("bundle:" + bundle.Index + ":" + bundle.Reds.Count + "@" + bundle.StartServerTime);
            }

            public void OnRedDetached(int castInstanceId, int bundleIndex, int redIndex, double serverTime, Vector3 position)
            {
                Events.Add("detached:" + bundleIndex + "/" + redIndex);
            }

            public void OnRedsLocked(int castInstanceId, int bundleIndex, double serverTime, IReadOnlyList<Vector3> centers)
            {
                Locks.Add(new List<Vector3>(centers).ToArray());
                Events.Add("lock:" + bundleIndex + "@" + serverTime);
            }

            public void OnBundleResolved(int castInstanceId, int bundleIndex, double serverTime, int singleHits, int doubleKills)
            {
                Resolved.Add(new[] { singleHits, doubleKills });
                Events.Add("blast:" + bundleIndex + "@" + serverTime);
            }

            public void OnFinalSucceeded(int castInstanceId, double serverTime)
            {
                _log.Lines.Add("success");
                Events.Add("success@" + serverTime);
            }

            public void OnFinalFailed(int castInstanceId, double serverTime, EWolbaekFinalFail reason)
            {
                Events.Add("fail:" + reason);
            }

            public void OnFinalHalted(int castInstanceId, double serverTime, EWolbaekFinalHaltReason reason)
            {
                Events.Add("halt:" + reason);
            }

            public void OnFinalCleared(int castInstanceId, EBossInterruptReason reason)
            {
                Events.Add("cleared:" + reason);
            }
        }

        private static WolbaekFinalAreaSpec MakeSpec(float gap = 0f)
        {
            return new WolbaekFinalAreaSpec(4f, CENTRAL, new[] { 2, 3, 4, 4 }, 8000f, 0f, 1f, 4f, 2f, 2f, 0.6f, gap, 3f);
        }

        private static readonly Vector3[] Spread =
        {
            new Vector3(-6f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(0f, 0f, 6f), new Vector3(0f, 0f, -6f)
        };

        private sealed class Rig
        {
            public Log                Log      { get; } = new Log();
            public StillBoss          Boss     { get; } = new StillBoss();
            public FakeRoster         Roster   { get; } = new FakeRoster();
            public FakeArena          Arena    { get; } = new FakeArena();
            public FakeSites          Sites    { get; } = new FakeSites();
            public PartyQuery         Query    { get; } = new PartyQuery();
            public RecordingResolver  Damage   { get; } = new RecordingResolver();
            public FakeBossVitals     Vitals   { get; } = new FakeBossVitals();
            public FakeFloor          Floor    { get; }
            public FakeKiller         Killer   { get; }
            public FakeGuard          Guard    { get; }
            public FakeRegistry       Registry { get; } = new FakeRegistry();
            public FakeRevive         Revive   { get; } = new FakeRevive();
            public Recorder           Listener { get; }
            public WolbaekFinalArea   Gimmick  { get; }

            public Rig(int partyCount, FakeHandoff preceding = null)
            {
                for (int i = 0; i < partyCount; ++i)
                {
                    FakeCombatTarget target = new FakeCombatTarget
                    {
                        CombatId = FIRST_PLAYER + i, Faction = ECombatFaction.Player, Position = Spread[i]
                    };
                    Roster.Members.Add(new Participant { Target = target });
                    Query.Players.Add(target);
                }

                Floor    = new FakeFloor(Query.Players);
                Killer   = new FakeKiller(Query.Players);
                Guard    = new FakeGuard(Log);
                Listener = new Recorder(Log);
                Gimmick  = new WolbaekFinalArea(MakeSpec(), Boss, Vitals, Roster, Roster, Arena, Sites, Floor, Killer, Guard, Registry,
                    Revive, Damage, Query, preceding, Listener);
            }

            public FakeCombatTarget Player(int index)
            {
                return Query.Players[index];
            }

            public void ZeroHealth()
            {
                Vitals.HealthRate   = 0f;
                Vitals.IsHealthZero = true;
            }

            public void Enter(double serverTime = 0.0)
            {
                ZeroHealth();
                Gimmick.Tick(serverTime, 2);
            }

            public void Tick(double serverTime)
            {
                Gimmick.Tick(serverTime, 2);
            }

            // 진입하고 바닥 제거 틱까지 진행한다
            public void ReachHunt()
            {
                Enter();
                Tick(4.0);
            }

            public void DestroyAllYellows(double serverTime)
            {
                for (int i = 0; i < Gimmick.YellowBodies.Count; ++i)
                {
                    Gimmick.YellowBodies[i].ApplyDamage(8000f, true, serverTime);
                }
            }

            public List<DamageRequest> Blasts()
            {
                List<DamageRequest> found = new List<DamageRequest>();
                for (int i = 0; i < Damage.Requests.Count; ++i)
                {
                    if (Damage.Requests[i].SourceDefinitionId == BLAST_ID)
                    {
                        found.Add(Damage.Requests[i]);
                    }
                }

                return found;
            }

            public int BlastsOn(int combatId)
            {
                int total = 0;
                List<DamageRequest> blasts = Blasts();
                for (int i = 0; i < blasts.Count; ++i)
                {
                    if (blasts[i].HitTargetId == combatId)
                    {
                        ++total;
                    }
                }

                return total;
            }
        }

        /****************************************
        *                Entry
        ****************************************/

        [Test]
        public void Entry_AtZeroHealth_StartsOnceEvenOnRepeatedTicksAtTheSameTime()
        {
            Rig r = new Rig(2);
            r.Enter();
            int cast = r.Gimmick.CastInstanceId;
            r.Tick(0.0);
            r.Tick(0.0);

            Assert.AreEqual(EWolbaekFinalState.Active, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Count("start"));
            Assert.AreEqual(cast, r.Gimmick.CastInstanceId);
            Assert.IsTrue(r.Gimmick.ConsumeCleanupRequest());
            Assert.IsFalse(r.Gimmick.ConsumeCleanupRequest());
        }

        [Test]
        public void Entry_AboveZeroHealth_StaysDormantAndDoesNotHoldDeath()
        {
            Rig r = new Rig(2);
            r.Vitals.HealthRate = 0.2f;
            r.Tick(0.0);

            Assert.AreEqual(EWolbaekFinalState.Dormant, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.IsDeathHeld);
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.IsFalse(r.Gimmick.BlocksSelection);
        }

        [Test]
        public void DeathHold_HoldsFromZeroHealthThroughActiveAndReleasesOnlyOnFinalSuccess()
        {
            Rig r = new Rig(2);
            BossGimmickDeathPolicy policy = new BossGimmickDeathPolicy(new ImmediateBossDeathPolicy());
            policy.AddHold(r.Gimmick);

            r.ZeroHealth();
            Assert.IsTrue(r.Gimmick.IsDeathHeld);
            Assert.IsFalse(policy.IsDeathConfirmed(r.Vitals, 0.0));

            r.Tick(0.0);
            Assert.IsFalse(policy.IsDeathConfirmed(r.Vitals, 0.0));

            r.Tick(4.0);
            r.DestroyAllYellows(5.0);
            r.Tick(5.0);

            Assert.AreEqual(EWolbaekFinalState.Succeeded, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.IsDeathHeld);
            Assert.IsTrue(policy.IsDeathConfirmed(r.Vitals, 5.0));
        }

        [Test]
        public void DeathHold_StaysHeldAfterWipeAndHalt()
        {
            Rig wiped = new Rig(1);
            wiped.Player(0).LifeState = EParticipantLifeState.Dead;
            wiped.Enter();
            Assert.AreEqual(EWolbaekFinalState.Failed, wiped.Gimmick.State);
            Assert.IsTrue(wiped.Gimmick.IsDeathHeld);

            LogAssert.ignoreFailingMessages = true;
            Rig halted = new Rig(1);
            halted.Arena.Available = false;
            halted.Enter();
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(EWolbaekFinalState.Halted, halted.Gimmick.State);
            Assert.IsTrue(halted.Gimmick.IsDeathHeld);
        }

        [Test]
        public void Handoff_TakenExactlyOnce_AndOnlyAfterTheSealReportsPending()
        {
            FakeHandoff seal = new FakeHandoff { Defers = true };
            Rig r = new Rig(2, seal);
            r.ZeroHealth();

            r.Tick(0.0);
            r.Tick(3.0);
            Assert.AreEqual(EWolbaekFinalState.Dormant, r.Gimmick.State);
            Assert.AreEqual(0, seal.TakeCalls);

            seal.Pending = true;
            r.Tick(6.0);
            Assert.AreEqual(EWolbaekFinalState.Active, r.Gimmick.State);
            Assert.AreEqual(1, seal.TakeCalls);

            r.Tick(6.0);
            r.Tick(7.0);
            Assert.AreEqual(1, seal.TakeCalls);
            Assert.AreEqual(1, r.Listener.Count("start"));
            Assert.AreEqual(6.0, r.Listener.Start.StartServerTime, 0.0001);
        }

        [Test]
        public void Handoff_WithoutPendingAndNotBusy_StartsAtZeroHealth()
        {
            FakeHandoff seal = new FakeHandoff();
            Rig r = new Rig(2, seal);
            r.Enter();

            Assert.AreEqual(EWolbaekFinalState.Active, r.Gimmick.State);
            Assert.AreEqual(0, seal.TakeCalls);
        }

        /****************************************
        *               Collapse
        ****************************************/

        [Test]
        public void Collapse_NothingSpawnsOrFallsBeforeTheWarningEnds()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(14f, 0f, 0f);
            r.Enter();
            r.Tick(3.75);

            Assert.AreEqual(EWolbaekFinalStep.Collapse, r.Gimmick.Step);
            Assert.AreEqual(0, r.Floor.Requests);
            Assert.AreEqual(0, r.Registry.Registered.Count);
            Assert.AreEqual(0, r.Killer.Kills.Count);
            Assert.AreEqual(0, r.Gimmick.YellowBodies.Count);
            Assert.AreEqual(4.0, r.Listener.Start.CollapseServerTime, 0.0001);
        }

        [Test]
        public void Collapse_KillsOnlyLivingPlayersStandingOnTheRemovedFloor()
        {
            Rig r = new Rig(4);
            r.Player(0).Position = new Vector3(14f, 0f, 0f);
            r.Player(1).Position = new Vector3(0f, 0f, 8.0f);
            r.Player(2).Position = new Vector3(0f, 0f, 20f);
            r.Player(2).LifeState = EParticipantLifeState.Dead;
            r.ReachHunt();

            Assert.AreEqual(1, r.Killer.Kills.Count);
            Assert.AreEqual(FIRST_PLAYER, r.Killer.Kills[0].CombatId);
            Assert.AreEqual(EWolbaekFinalDeath.Fall, r.Killer.Kills[0].Cause);
            CollectionAssert.AreEqual(new[] { FIRST_PLAYER }, r.Listener.Fallen);
            Assert.AreEqual(EParticipantLifeState.Alive, r.Player(1).LifeState);
        }

        [Test]
        public void Collapse_NoDamageOverTimeOrGraceExists_OnlyOneFallJudgment()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(14f, 0f, 0f);
            r.ReachHunt();
            r.Tick(5.0);
            r.Tick(6.0);

            Assert.AreEqual(1, r.Killer.Kills.Count);
            Assert.AreEqual(1, r.Listener.Count("floor@"));
        }

        [Test]
        public void Collapse_Rejected_HaltsWithoutKillingAnyone()
        {
            LogAssert.ignoreFailingMessages = true;
            Rig r = new Rig(2);
            r.Floor.Accept       = false;
            r.Player(0).Position = new Vector3(14f, 0f, 0f);
            r.ReachHunt();
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(EWolbaekFinalState.Halted, r.Gimmick.State);
            Assert.AreEqual(EWolbaekFinalHaltReason.FloorUnavailable, r.Gimmick.HaltReason);
            Assert.AreEqual(0, r.Killer.Kills.Count);
            Assert.AreEqual(0, r.Registry.Registered.Count);
            Assert.IsTrue(r.Gimmick.IsDeathHeld);
        }

        [Test]
        public void Collapse_EveryoneFalls_FailsWithoutSpawningYellows()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(14f, 0f, 0f);
            r.Player(1).Position = new Vector3(-14f, 0f, 0f);
            r.ReachHunt();

            Assert.AreEqual(EWolbaekFinalState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekFinalFail.Wipe, r.Gimmick.FailReason);
            Assert.AreEqual(0, r.Registry.Registered.Count);
            Assert.AreEqual(0, r.Listener.Count("bundle"));
        }

        [Test]
        public void Collapse_WipeWithRevivablePlayers_StillFailsImmediately()
        {
            Rig r = new Rig(2);
            r.Player(0).LifeState = EParticipantLifeState.ReviveWaiting;
            r.Player(1).LifeState = EParticipantLifeState.Dead;
            r.Roster.Members[0].Revivable = true;
            r.Enter();

            Assert.AreEqual(EWolbaekFinalState.Failed, r.Gimmick.State);
        }

        /****************************************
        *                Yellows
        ****************************************/

        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(3, 4)]
        [TestCase(4, 4)]
        public void Yellows_CountIsFixedByLivingPlayersAtEntry(int living, int expected)
        {
            Rig r = new Rig(4);
            for (int i = living; i < 4; ++i)
            {
                r.Player(i).LifeState = EParticipantLifeState.Dead;
            }

            r.ReachHunt();

            Assert.AreEqual(expected, r.Gimmick.YellowCount);
            Assert.AreEqual(expected, r.Gimmick.YellowBodies.Count);
            Assert.AreEqual(expected, r.Registry.Registered.Count);
            Assert.AreEqual(expected, r.Listener.Start.YellowCount);
        }

        [Test]
        public void Yellows_AreCreatedOnlyAtTheFloorRemovalMomentOnVerifiedSitesInOrder()
        {
            Rig r = new Rig(3);
            r.Enter();
            r.Tick(3.5);
            Assert.AreEqual(0, r.Registry.Registered.Count);

            r.Tick(4.0);

            Assert.AreEqual(4, r.Gimmick.YellowBodies.Count);
            for (int i = 0; i < 4; ++i)
            {
                Assert.AreEqual(r.Sites.Centers[i], r.Gimmick.Yellows[i].Position);
                Assert.AreEqual(FIRST_YELLOW + i, r.Gimmick.Yellows[i].CombatId);
                Assert.AreEqual(8000f, r.Gimmick.YellowBodies[i].MaxHealth);
                Assert.AreEqual(0f, r.Gimmick.YellowBodies[i].Defense);
                Assert.AreEqual(ECombatFaction.Enemy, r.Gimmick.YellowBodies[i].Faction);
            }
        }

        [Test]
        public void Yellows_CountDoesNotShrinkWhenPlayersDieOrDisconnect()
        {
            Rig r = new Rig(4);
            r.ReachHunt();
            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Roster.Members[1].Connected = false;
            r.Tick(10.0);
            r.Tick(20.0);

            Assert.AreEqual(4, r.Gimmick.YellowCount);
            Assert.AreEqual(4, r.Gimmick.YellowRemaining);
        }

        [Test]
        public void Yellows_SitesOutsideTheCentralFloorOrTooFewHaltBeforeTheFloorIsTouched()
        {
            LogAssert.ignoreFailingMessages = true;
            Rig outside = new Rig(1);
            // 한 명이면 두 개가 필요한데 중앙 바닥 안의 위치는 하나만 남긴다
            outside.Sites.Centers[1] = new Vector3(9f, 0f, 0f);
            outside.Sites.Centers[2] = new Vector3(0f, 0f, 9f);
            outside.Sites.Centers[3] = new Vector3(-9f, 0f, 0f);
            outside.Enter();
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(EWolbaekFinalState.Halted, outside.Gimmick.State);
            Assert.AreEqual(EWolbaekFinalHaltReason.SitesInsufficient, outside.Gimmick.HaltReason);
            Assert.AreEqual(0, outside.Floor.Requests);
        }

        [Test]
        public void Yellows_RegistryFailures_Halt()
        {
            LogAssert.ignoreFailingMessages = true;
            Rig noId = new Rig(1);
            noId.Registry.IssueFails = true;
            noId.Enter();
            Assert.AreEqual(EWolbaekFinalHaltReason.RegistryUnavailable, noId.Gimmick.HaltReason);
            Assert.AreEqual(0, noId.Floor.Requests);

            Rig noRegister = new Rig(1);
            noRegister.Registry.RegisterFails = true;
            noRegister.ReachHunt();
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(EWolbaekFinalState.Halted, noRegister.Gimmick.State);
            Assert.AreEqual(EWolbaekFinalHaltReason.RegistryUnavailable, noRegister.Gimmick.HaltReason);
            Assert.AreEqual(0, noRegister.Gimmick.YellowBodies.Count);
        }

        [Test]
        public void Yellows_TakeDamagePerBodyAndRedBlastsNeverTargetThem()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.Tick(10.0);

            Assert.AreEqual(3, r.Gimmick.YellowBodies.Count);
            r.Gimmick.YellowBodies[0].ApplyDamage(3000f, false, 11.0);
            r.Gimmick.YellowBodies[1].ApplyDamage(3000f, false, 11.0);

            Assert.AreEqual(5000f, r.Gimmick.YellowBodies[0].Health);
            Assert.AreEqual(5000f, r.Gimmick.YellowBodies[1].Health);
            Assert.AreEqual(8000f, r.Gimmick.YellowBodies[2].Health);

            List<DamageRequest> blasts = r.Blasts();
            for (int i = 0; i < blasts.Count; ++i)
            {
                Assert.GreaterOrEqual(blasts[i].HitTargetId, FIRST_PLAYER);
                Assert.Less(blasts[i].HitTargetId, FIRST_YELLOW);
            }
        }

        /****************************************
        *                 Reds
        ****************************************/

        [Test]
        public void Reds_FirstBundleStartsAtTheFloorRemovalAndRepeatsWhileYellowsRemain()
        {
            Rig r = new Rig(2);
            r.ReachHunt();

            Assert.AreEqual(1, r.Gimmick.BundleCount);
            Assert.AreEqual(4.0, r.Gimmick.Bundle.StartServerTime, 0.0001);
            Assert.AreEqual(8.0, r.Gimmick.Bundle.LockServerTime, 0.0001);
            Assert.AreEqual(10.0, r.Gimmick.Bundle.BlastServerTime, 0.0001);

            r.Tick(10.0);
            Assert.AreEqual(2, r.Gimmick.BundleCount);
            r.Tick(16.0);
            Assert.AreEqual(3, r.Gimmick.BundleCount);
        }

        [Test]
        public void Reds_OnePerLivingPlayerAtBundleStartAndRecountedEachBundle()
        {
            Rig r = new Rig(4);
            r.ReachHunt();
            Assert.AreEqual(4, r.Gimmick.Bundle.Reds.Count);

            r.Player(3).LifeState = EParticipantLifeState.Dead;
            r.Tick(10.0);

            Assert.AreEqual(3, r.Gimmick.Bundle.Reds.Count);
        }

        [Test]
        public void Reds_ReviveduringABundleJoinsOnlyTheNextBundle()
        {
            Rig r = new Rig(3);
            r.Player(2).LifeState = EParticipantLifeState.Dead;
            r.ReachHunt();
            Assert.AreEqual(2, r.Gimmick.Bundle.Reds.Count);

            r.Player(2).LifeState = EParticipantLifeState.Alive;
            r.Revive.Stamps[FIRST_PLAYER + 2] = 6.0;
            r.Tick(9.0);
            Assert.AreEqual(2, r.Gimmick.Bundle.Reds.Count);

            r.Tick(10.0);
            Assert.AreEqual(3, r.Gimmick.Bundle.Reds.Count);
        }

        [Test]
        public void Reds_TrackUntilLockThenStayEvenIfTheTargetMoves()
        {
            Rig r = new Rig(1);
            r.ReachHunt();
            r.Player(0).Position = new Vector3(5f, 0f, 0f);
            r.Tick(7.0);
            r.Player(0).Position = new Vector3(4f, 0f, 3f);
            r.Tick(8.0);
            Vector3 locked = r.Listener.Locks[0][0];
            r.Player(0).Position = new Vector3(-6f, 0f, 0f);
            r.Tick(9.0);

            Assert.AreEqual(new Vector3(4f, 0f, 3f), locked);
            Assert.AreEqual(0, r.BlastsOn(FIRST_PLAYER));

            r.Tick(10.0);
            Assert.AreEqual(0, r.BlastsOn(FIRST_PLAYER));
        }

        [Test]
        public void Reds_DeadOrDisconnectedTarget_KeepsTheLastServerPositionAndStillExplodes()
        {
            Rig r = new Rig(3);
            r.ReachHunt();
            Vector3 lastSpot = r.Player(0).Position;

            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Roster.Members[1].Connected = false;
            r.Tick(5.0);
            r.Player(0).Position = new Vector3(0f, 0f, 3f);
            r.Tick(8.0);

            Assert.GreaterOrEqual(r.Listener.Count("detached"), 2);

            // 다른 사람이 쓰러진 대상의 마지막 자리에 들어가면 그 폭발에 맞는다
            r.Player(2).Position = lastSpot;
            r.Tick(10.0);

            Assert.AreEqual(1, r.BlastsOn(FIRST_PLAYER + 2));
            Assert.AreEqual(600f, r.Blasts()[0].RawDamage, 0.001f);
        }

        /****************************************
        *               Blast Damage
        ****************************************/

        [Test]
        public void Blast_SingleHit_IsSixtyPercentOfMaxHealthUnblockableAndNotScaled()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.Tick(10.0);

            List<DamageRequest> blasts = r.Blasts();
            Assert.AreEqual(2, blasts.Count);
            for (int i = 0; i < blasts.Count; ++i)
            {
                Assert.AreEqual(600f, blasts[i].RawDamage, 0.001f);
                Assert.IsTrue(blasts[i].IsUnblockable);
            }

            Assert.AreEqual(0, r.Killer.Kills.Count);
            Assert.AreEqual(2, r.Listener.Resolved[0][0]);
            Assert.AreEqual(0, r.Listener.Resolved[0][1]);
        }

        [Test]
        public void Blast_OverlappingDisplayOnly_IsNotInstantDeath()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(-1.5f, 0f, 0f);
            r.Player(1).Position = new Vector3(1.5f, 0f, 0f);
            r.ReachHunt();
            r.Tick(8.0);
            r.Tick(10.0);

            Assert.AreEqual(0, r.Killer.Kills.Count);
            Assert.AreEqual(1, r.BlastsOn(FIRST_PLAYER));
            Assert.AreEqual(1, r.BlastsOn(FIRST_PLAYER + 1));
        }

        [Test]
        public void Blast_TwoActuallyHit_IsInstantDeathWithNoDamageRequest()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(-1.5f, 0f, 0f);
            r.Player(1).Position = new Vector3(1.5f, 0f, 0f);
            r.ReachHunt();
            r.Tick(8.0);
            r.Player(0).Position = Vector3.zero;
            r.Tick(10.0);

            Assert.AreEqual(1, r.Killer.Kills.Count);
            Assert.AreEqual(FIRST_PLAYER, r.Killer.Kills[0].CombatId);
            Assert.AreEqual(EWolbaekFinalDeath.DoubleBlast, r.Killer.Kills[0].Cause);
            Assert.AreEqual(0, r.BlastsOn(FIRST_PLAYER));
            Assert.AreEqual(1, r.Listener.Resolved[0][1]);
        }

        [Test]
        public void Blast_BoundaryOfTheRadiusCounts()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.Tick(8.0);
            r.Player(0).Position = Spread[0] + new Vector3(2f, 0f, 0f);
            r.Tick(10.0);

            Assert.AreEqual(1, r.BlastsOn(FIRST_PLAYER));
        }

        /****************************************
        *            Revive Protection
        ****************************************/

        [Test]
        public void Protection_BlocksSingleBlastDuringTheWindowButNotAtItsEnd()
        {
            Rig inside = new Rig(2);
            inside.ReachHunt();
            inside.Revive.Stamps[FIRST_PLAYER] = 8.5;
            inside.Tick(10.0);
            Assert.AreEqual(0, inside.BlastsOn(FIRST_PLAYER));
            Assert.AreEqual(1, inside.BlastsOn(FIRST_PLAYER + 1));

            Rig edge = new Rig(2);
            edge.ReachHunt();
            edge.Revive.Stamps[FIRST_PLAYER] = 7.0;
            edge.Tick(10.0);
            Assert.AreEqual(1, edge.BlastsOn(FIRST_PLAYER));
        }

        [Test]
        public void Protection_BlocksDoubleBlastDeath()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(-1.5f, 0f, 0f);
            r.Player(1).Position = new Vector3(1.5f, 0f, 0f);
            r.ReachHunt();
            r.Tick(8.0);
            r.Player(0).Position = Vector3.zero;
            r.Revive.Stamps[FIRST_PLAYER] = 8.5;
            r.Tick(10.0);

            Assert.AreEqual(0, r.Killer.Kills.Count);
            Assert.AreEqual(0, r.BlastsOn(FIRST_PLAYER));
        }

        [Test]
        public void Protection_DoesNotPreventTheFall()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(14f, 0f, 0f);
            r.Revive.Stamps[FIRST_PLAYER] = 3.5;
            r.ReachHunt();

            Assert.AreEqual(1, r.Killer.Kills.Count);
            Assert.AreEqual(EWolbaekFinalDeath.Fall, r.Killer.Kills[0].Cause);
        }

        [Test]
        public void Protection_ReviveBeforeTheGimmickStartedDoesNotCount()
        {
            Rig r = new Rig(2);
            r.Revive.Stamps[FIRST_PLAYER] = -1.0;
            r.Enter(0.0);
            r.Tick(4.0);
            r.Tick(10.0);

            Assert.AreEqual(1, r.BlastsOn(FIRST_PLAYER));
        }

        [Test]
        public void BigTick_ReviveAfterTheBlastTimeMeansTheyWereDownDuringTheBlast()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.Tick(8.0);
            r.Revive.Stamps[FIRST_PLAYER] = 10.5;
            r.Tick(11.0);

            Assert.AreEqual(0, r.BlastsOn(FIRST_PLAYER));
            Assert.AreEqual(1, r.BlastsOn(FIRST_PLAYER + 1));
        }

        /****************************************
        *               Big Ticks
        ****************************************/

        [Test]
        public void BigTick_ProcessesEveryScheduledEventInTimeOrderExactlyOnce()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.Tick(100.0);

            // 바닥 제거 t=4 뒤 묶음이 6초마다 시작해 t=100에도 시작한다
            Assert.AreEqual(17, r.Gimmick.BundleCount);
            Assert.AreEqual(1, r.Listener.Count("floor@"));
            Assert.AreEqual(1, r.Listener.Count("yellows"));
            Assert.AreEqual(16, r.Listener.Count("blast"));
            Assert.AreEqual(16, r.BlastsOn(FIRST_PLAYER));

            int floorAt = r.Listener.Events.IndexOf("floor@4");
            int bundleAt = r.Listener.Events.IndexOf("bundle:0:1@4");
            int lockAt = r.Listener.Events.IndexOf("lock:0@8");
            int blastAt = r.Listener.Events.IndexOf("blast:0@10");
            int nextAt = r.Listener.Events.IndexOf("bundle:1:1@10");
            Assert.Less(floorAt, bundleAt);
            Assert.Less(bundleAt, lockAt);
            Assert.Less(lockAt, blastAt);
            Assert.Less(blastAt, nextAt);

            r.Tick(100.0);
            Assert.AreEqual(17, r.Gimmick.BundleCount);
            Assert.AreEqual(16, r.BlastsOn(FIRST_PLAYER));
        }

        [Test]
        public void BigTick_WipeInTheMiddleStopsTheLaterEvents()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.Player(0).Position = Spread[0];

            // 한 번의 큰 틱 안에서 첫 폭발이 단일 피해만 줘서는 죽지 않으므로 사망 대역을 켠다
            r.Tick(4.0);
            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Tick(50.0);

            Assert.AreEqual(EWolbaekFinalState.Failed, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Count("fail"));
        }

        /****************************************
        *               Wipe
        ****************************************/

        [Test]
        public void Wipe_DuringHunt_FailsAndNothingMoreHurts()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Player(1).LifeState = EParticipantLifeState.ReviveWaiting;
            int before = r.Damage.Requests.Count;
            r.Tick(10.0);
            r.Tick(20.0);

            Assert.AreEqual(EWolbaekFinalState.Failed, r.Gimmick.State);
            Assert.AreEqual(before, r.Damage.Requests.Count);
            Assert.AreEqual(1, r.Listener.Count("fail"));
        }

        [Test]
        public void Wipe_ByTheDoubleBlastThatKillsTheLastPlayer_Fails()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(-1.5f, 0f, 0f);
            r.Player(1).Position = new Vector3(1.5f, 0f, 0f);
            r.ReachHunt();
            r.Tick(8.0);
            r.Player(0).Position = Vector3.zero;
            r.Player(1).Position = Vector3.zero;
            r.Tick(10.0);

            Assert.AreEqual(2, r.Killer.Kills.Count);
            Assert.AreEqual(EWolbaekFinalState.Failed, r.Gimmick.State);
            Assert.AreEqual(1, r.Gimmick.BundleCount);
        }

        /****************************************
        *             Final Success
        ****************************************/

        [Test]
        public void Success_LastYellowDestroyed_ConfirmsSuccessAndGuardBeforeAnythingElse()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.DestroyAllYellows(9.0);
            r.Tick(9.0);

            Assert.AreEqual(EWolbaekFinalState.Succeeded, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.IsFinalSucceeded);
            Assert.AreEqual(1, r.Guard.Calls);
            CollectionAssert.AreEqual(new[] { "guard", "success" }, r.Log.Lines);
            Assert.IsFalse(r.Gimmick.IsDeathHeld);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
        }

        [Test]
        public void Success_BeatsTheBlastDueOnTheSameTick()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.DestroyAllYellows(10.0);
            r.Tick(10.0);

            Assert.AreEqual(EWolbaekFinalState.Succeeded, r.Gimmick.State);
            Assert.AreEqual(0, r.Blasts().Count);
            Assert.AreEqual(0, r.Killer.Kills.Count);
        }

        [Test]
        public void Success_BeatsWipeOnTheSameTick()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.DestroyAllYellows(9.0);
            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Player(1).LifeState = EParticipantLifeState.Dead;
            r.Tick(9.0);

            Assert.AreEqual(EWolbaekFinalState.Succeeded, r.Gimmick.State);
            Assert.AreEqual(0, r.Listener.Count("fail"));
        }

        [Test]
        public void Success_CancelsTheRemainingRedBlastsForGood()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.Tick(8.0);
            r.DestroyAllYellows(9.0);
            r.Tick(9.0);
            int before = r.Damage.Requests.Count;

            r.Tick(10.0);
            r.Tick(60.0);

            Assert.AreEqual(before, r.Damage.Requests.Count);
            Assert.AreEqual(1, r.Gimmick.BundleCount);
            Assert.AreEqual(1, r.Guard.Calls);
        }

        [Test]
        public void Success_NeedsEveryYellowDestroyed()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.Gimmick.YellowBodies[0].ApplyDamage(8000f, true, 9.0);
            r.Gimmick.YellowBodies[1].ApplyDamage(7999f, false, 9.0);
            r.Tick(9.0);

            Assert.AreEqual(EWolbaekFinalState.Active, r.Gimmick.State);
            Assert.AreEqual(2, r.Gimmick.YellowRemaining);
            Assert.Contains("yellowDown:0:2", r.Listener.Events);
            Assert.AreEqual(0, r.Guard.Calls);
        }

        [Test]
        public void Success_SignalIsConsumedExactlyOnceWithTheBattleKey()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            Assert.IsFalse(r.Gimmick.HasFinalSuccess);
            Assert.IsFalse(r.Gimmick.TryConsumeFinalSuccess(out BossFinalSuccess early));

            r.DestroyAllYellows(9.0);
            r.Tick(9.0);
            r.Tick(9.0);

            Assert.IsTrue(r.Gimmick.HasFinalSuccess);
            Assert.IsTrue(r.Gimmick.TryConsumeFinalSuccess(out BossFinalSuccess success));
            Assert.AreEqual(BossHarness.SELF_ID, success.BossCombatId);
            Assert.AreEqual(r.Gimmick.CastInstanceId, success.CastInstanceId);
            Assert.AreEqual(9.0, success.ServerTime, 0.0001);
            Assert.IsFalse(r.Gimmick.TryConsumeFinalSuccess(out BossFinalSuccess again));
            Assert.IsFalse(r.Gimmick.HasFinalSuccess);
            Assert.IsTrue(r.Gimmick.IsFinalSucceeded);
        }

        [Test]
        public void Success_ReleasesYellowsAndSurvivesTheBossDeathStop()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.DestroyAllYellows(9.0);
            r.Tick(9.0);

            Assert.AreEqual(3, r.Registry.Unregistered.Count);
            Assert.IsTrue(r.Gimmick.YellowBodies[0].IsInvulnerable);

            r.Gimmick.Stop(EBossInterruptReason.Death);
            Assert.AreEqual(EWolbaekFinalState.Succeeded, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.HasFinalSuccess);
        }

        /****************************************
        *          Stop, Snapshot, Revive Point
        ****************************************/

        [Test]
        public void Stop_WhileActive_ClearsDisplayAndReleasesYellows()
        {
            Rig r = new Rig(2);
            r.ReachHunt();
            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);
            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);
            r.Tick(10.0);

            Assert.AreEqual(EWolbaekFinalState.Stopped, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Count("cleared"));
            Assert.AreEqual(3, r.Registry.Unregistered.Count);
            Assert.IsFalse(r.Gimmick.IsDeathHeld);
        }

        [Test]
        public void Snapshot_RestoresYellowsAndTheCurrentBundle()
        {
            Rig r = new Rig(2);
            Assert.IsFalse(r.Gimmick.TryGetSnapshot(out WolbaekFinalSnapshot none));

            r.ReachHunt();
            r.Gimmick.YellowBodies[0].ApplyDamage(2000f, false, 5.0);
            r.Tick(8.0);

            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekFinalSnapshot snapshot));
            Assert.AreEqual(EWolbaekFinalStep.Hunt, snapshot.Step);
            Assert.IsTrue(snapshot.OuterFloorRemoved);
            Assert.AreEqual(3, snapshot.Yellows.Count);
            Assert.AreEqual(6000f, snapshot.YellowHealth[0]);
            Assert.IsTrue(snapshot.HasBundle);
            Assert.IsTrue(snapshot.RedsLocked);
            Assert.AreEqual(2, snapshot.RedPositions.Count);
        }

        [Test]
        public void SafeRevivePoint_IsOfferedOnlyWhileActive()
        {
            Rig r = new Rig(2);
            Assert.IsFalse(r.Gimmick.TryGetSafeRevivePoint(FIRST_PLAYER, out Vector3 before));

            r.Enter();
            Assert.IsTrue(r.Gimmick.TryGetSafeRevivePoint(FIRST_PLAYER, out Vector3 point));
            Assert.AreEqual(new Vector3(1f, 0f, 1f), point);
        }

        [Test]
        public void Cap_BlocksEveryDamageWhileHeldAndPassesItBeforeZero()
        {
            Rig r = new Rig(1);
            Assert.AreEqual(50f, r.Gimmick.CapDamage(500f, 1000f, 50f), 0.001f);

            r.Enter();
            Assert.AreEqual(0f, r.Gimmick.CapDamage(0f, 1000f, 50f), 0.001f);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(500f, 1000f, 0f), 0.001f);
        }

        /****************************************
        *                 Spec
        ****************************************/

        [Test]
        public void Spec_RejectsInvalidValuesAndReadsCountsByLivingPlayers()
        {
            WolbaekFinalAreaSpec spec = MakeSpec();
            Assert.AreEqual(0, spec.GetYellowCount(0));
            Assert.AreEqual(2, spec.GetYellowCount(1));
            Assert.AreEqual(4, spec.GetYellowCount(9));

            Assert.Throws<ArgumentException>(() => new WolbaekFinalAreaSpec(4f, 8f, new[] { 2, 3, 4 }, 8000f, 0f, 1f, 4f, 2f, 2f, 0.6f, 0f, 3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekFinalAreaSpec(4f, 8f, new[] { 2, 3, 2, 4 }, 8000f, 0f, 1f, 4f, 2f, 2f, 0.6f, 0f, 3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekFinalAreaSpec(0f, 8f, new[] { 2, 3, 4, 4 }, 8000f, 0f, 1f, 4f, 2f, 2f, 0.6f, 0f, 3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WolbaekFinalAreaSpec(4f, 8f, new[] { 2, 3, 4, 4 }, 8000f, -1f, 1f, 4f, 2f, 2f, 0.6f, 0f, 3f));
        }
    }
}
