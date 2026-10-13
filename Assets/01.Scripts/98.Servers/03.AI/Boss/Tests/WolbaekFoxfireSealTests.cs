// System
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
    // 시각은 이진 분수로 넘겨 경계 판정에 부동소수 오차가 끼지 않게 한다. 휩쓸기 예고 시각(판정 0.8초 전)만 이진 분수가 아니라 허용 오차로 비교한다.
    // 기본 시간표: 진입 t=0이 첫 차례의 시작이다. 준비 3초, 추적 t=3~7, 낙하 위치 고정 t=7, 판정 t=9, 정리 1초 뒤 t=10에 다음 차례가 시작된다.
    // 그래서 성공 세 번의 마지막 판정은 t=29이고 제한 60초 안에는 판정이 t=9, 19, 29, 39, 49, 59에 최대 여섯 번 있다. 반격은 6초다.
    // 후보는 전장 중심에서 11m 떨어진 8방위이고 서로 8.4m 이상 떨어져 있다. 하드 휩쓸기의 시험값은 범위 16m, 좌우 60도다.
    // 첫 휩쓸기 방향은 난수로 정해지는데 고정 난수가 0을 주므로 +Z 방향이고, 두 번째는 -Z 방향이다
    public class WolbaekFoxfireSealTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TRIGGER_RATE   = 0.25f;
        private const float  ARENA_RADIUS   = 18f;
        private const float  MAX_HEALTH     = 1000f;
        private const int    FIRST_PLAYER   = 10;
        private const float  SITE_RING      = 11f;
        private const int    SITE_COUNT     = 8;
        private const float  TIME_LIMIT     = 60f;
        private const float  SWEEP_RANGE    = 16f;
        private const float  SWEEP_ANGLE    = 60f;
        private const string BLAST_ID       = "wolbaek_seal_blast";
        private const string SWEEP_ID       = "wolbaek_seal_sweep";

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

        private sealed class FakeRoster : IBossParticipantRoster, IBossParticipantHealth
        {
            public List<Participant> Members   { get; } = new List<Participant>();
            public float             MaxHealth { get; set; } = MAX_HEALTH;

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

            public bool TryGetMaxHealth(int combatId, out float maxHealth)
            {
                maxHealth = MaxHealth;
                return true;
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

        // 색 결계와 봉인 핵이 같은 후보 목록을 나눠 쓴다
        private sealed class FakeSites : IWolbaekSealSiteSource, IWolbaekWardSiteSource
        {
            public List<Vector3> Centers { get; } = new List<Vector3>();

            public FakeSites(int count)
            {
                for (int i = 0; i < count; ++i)
                {
                    float angle = i * (Mathf.PI * 2f / SITE_COUNT);
                    Centers.Add(new Vector3(Mathf.Cos(angle) * SITE_RING, 0f, Mathf.Sin(angle) * SITE_RING));
                }
            }

            public int CollectCandidates(List<Vector3> centers)
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
            public Vector3               Position      { get; set; }
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

        private sealed class ScriptedRandom : IBossRandom
        {
            public float NextFloat01()
            {
                return 0f;
            }

            public int NextInt(int maxExclusive)
            {
                return 0;
            }
        }

        private sealed class SeededRandom : IBossRandom
        {
            private readonly System.Random _random;

            public SeededRandom(int seed)
            {
                _random = new System.Random(seed);
            }

            public float NextFloat01()
            {
                return (float)_random.NextDouble();
            }

            public int NextInt(int maxExclusive)
            {
                return _random.Next(maxExclusive);
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

        // 요청을 기록하고, 켜 두면 맞은 대상을 쓰러뜨려 폭발이 마지막 생존자를 죽이는 상황을 만든다
        private sealed class RecordingResolver : IDamageResolver
        {
            private int _nextId = 1;

            public List<DamageRequest>                Requests { get; } = new List<DamageRequest>();
            public Dictionary<int, FakeCombatTarget>  Targets  { get; } = new Dictionary<int, FakeCombatTarget>();
            public bool                               Lethal   { get; set; }

            public DamageResult Apply(in DamageRequest request)
            {
                Requests.Add(request);

                if (Lethal && Targets.TryGetValue(request.HitTargetId, out FakeCombatTarget target))
                {
                    target.LifeState = EParticipantLifeState.Dead;
                }

                return DamageResult.Applied(1f, Lethal, false);
            }

            public int IssueInstanceId()
            {
                int issued = _nextId;
                ++_nextId;
                return issued;
            }
        }

        private sealed class FakePreceding : IBossGimmick
        {
            public bool Defers { get; set; }

            public bool DefersPhaseTransition => Defers;
            public bool BlocksSelection       => Defers;

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

        private sealed class FixedCap : IBossDamageCap
        {
            public float Boundary { get; set; }

            public bool IsDamageBlocked => false;

            public float CapDamage(float currentHealth, float maxHealth, float damage)
            {
                float allowed = currentHealth - (Boundary * maxHealth);
                return (allowed > 0f) ? Mathf.Min(damage, allowed) : 0f;
            }
        }

        private sealed class Recorder : IWolbaekFoxfireSealListener
        {
            public List<string>          Events     { get; } = new List<string>();
            public List<Vector3[]>       Locks      { get; } = new List<Vector3[]>();
            public List<WolbaekSealSweep> Telegraphs { get; } = new List<WolbaekSealSweep>();
            public List<WolbaekSealTurn> Turns      { get; } = new List<WolbaekSealTurn>();
            public List<int>             SweepHits  { get; } = new List<int>();
            public WolbaekSealStart      Start      { get; private set; }

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

            public void OnSealStarted(in WolbaekSealStart start)
            {
                Start = start;
                Events.Add("start");
            }

            public void OnTurnStarted(int castInstanceId, in WolbaekSealTurn turn)
            {
                Turns.Add(turn);
                Events.Add("turn:" + turn.Index + ":" + turn.Cores.Count + "/" + turn.Foxfires.Count);
            }

            public void OnTrackingStarted(int castInstanceId, int turnIndex, double serverTime)
            {
                Events.Add("tracking:" + turnIndex + "@" + serverTime);
            }

            public void OnFoxfireDetached(int castInstanceId, int turnIndex, int foxfireIndex, double serverTime, Vector3 position)
            {
                Events.Add("detached:" + turnIndex + "/" + foxfireIndex + "@" + serverTime);
            }

            public void OnSweepTelegraphed(int castInstanceId, in WolbaekSealSweep sweep)
            {
                Telegraphs.Add(sweep);
                Events.Add("telegraph:" + sweep.SweepIndex);
            }

            public void OnSweepResolved(int castInstanceId, int turnIndex, int sweepIndex, double serverTime, int hitCount)
            {
                SweepHits.Add(hitCount);
                Events.Add("strike:" + sweepIndex + "@" + serverTime);
            }

            public void OnFoxfiresLocked(int castInstanceId, int turnIndex, double serverTime, IReadOnlyList<Vector3> centers)
            {
                Locks.Add(new List<Vector3>(centers).ToArray());
                Events.Add("lock:" + turnIndex + "@" + serverTime);
            }

            public void OnTurnResolved(int castInstanceId, int turnIndex, double serverTime, int destroyedCores, int totalCores,
                int playerHits, bool succeeded, int successCount)
            {
                Events.Add("resolve:" + turnIndex + ":" + destroyedCores + "/" + totalCores + ":" + succeeded + ":" + successCount
                    + "@" + serverTime);
            }

            public void OnSealSucceeded(int castInstanceId, double serverTime, double counterEndServerTime)
            {
                Events.Add("success@" + serverTime + "/" + counterEndServerTime);
            }

            public void OnCounterEnded(int castInstanceId, double serverTime, bool reachedNextBoundary)
            {
                Events.Add("counterEnd:" + reachedNextBoundary);
            }

            public void OnSealFailed(int castInstanceId, double serverTime, EWolbaekSealFail reason)
            {
                Events.Add("fail:" + reason);
            }

            public void OnSealHalted(int castInstanceId, double serverTime, EWolbaekSealHaltReason reason)
            {
                Events.Add("halt:" + reason);
            }

            public void OnSealCleared(int castInstanceId, EBossInterruptReason reason)
            {
                Events.Add("cleared:" + reason);
            }
        }

        private static WolbaekSealSweepSpec MakeSweep()
        {
            return new WolbaekSealSweepSpec(new[] { 1.5f, 3f }, 0.8f, 0.25f, 1f, SWEEP_RANGE, SWEEP_ANGLE);
        }

        private static WolbaekFoxfireSealSpec MakeSpec(bool hard, float limit = TIME_LIMIT)
        {
            return new WolbaekFoxfireSealSpec(TRIGGER_RATE, hard ? 0f : (float?)null, 3f, 4f, 2f, 1f, 3, limit, 1.5f, 1.5f, 0.25f, 6f,
                6f, hard ? MakeSweep() : null);
        }

        private static WolbaekColorWardSpec MakeWardSpec()
        {
            return new WolbaekColorWardSpec(0.5f, 5f, 8f, 3f, 3, 7f, 1.5f, 3, 1.5f, 0.3f, 1f, 15f, 8f, 3f, 4f, 1f,
                new[] { 5f, 6f }, new[] { 0.05f, 0.1f, 0.2f }, 8f);
        }

        private sealed class Rig
        {
            public StillBoss          Boss      { get; } = new StillBoss();
            public FakeRoster         Roster    { get; } = new FakeRoster();
            public FakeArena          Arena     { get; } = new FakeArena();
            public FakeSites          Sites     { get; }
            public PartyQuery         Query     { get; } = new PartyQuery();
            public RecordingResolver  Damage    { get; } = new RecordingResolver();
            public FakeBossVitals     Vitals    { get; } = new FakeBossVitals();
            public FakeDisplacer      Displacer { get; } = new FakeDisplacer();
            public Recorder           Listener  { get; } = new Recorder();
            public WolbaekColorWard   Ward      { get; }
            public WolbaekFoxfireSeal Gimmick   { get; }
            public int                Phase     { get; set; } = 2;

            public Rig(int partyCount, bool hard = false, bool withWard = false, IBossGimmick preceding = null, int siteCount = SITE_COUNT,
                float limit = TIME_LIMIT, IBossRandom random = null)
            {
                Sites = new FakeSites(siteCount);
                for (int i = 0; i < partyCount; ++i)
                {
                    FakeCombatTarget target = new FakeCombatTarget { CombatId = FIRST_PLAYER + i, Faction = ECombatFaction.Player };
                    Roster.Members.Add(new Participant { Target = target });
                    Query.Players.Add(target);
                    Damage.Targets[target.CombatId] = target;
                }

                IBossGimmick before = preceding;
                if (withWard)
                {
                    Ward   = new WolbaekColorWard(MakeWardSpec(), Boss, Vitals, Roster, Roster, Arena, Sites, Damage, Query,
                        new ScriptedRandom(), Displacer);
                    before = Ward;
                }

                Gimmick = new WolbaekFoxfireSeal(MakeSpec(hard, limit), Boss, Vitals, Roster, Roster, Arena, Sites, Damage, Query,
                    random ?? new ScriptedRandom(), Displacer, before, Listener);
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

            public FakeCombatTarget Player(int index)
            {
                return Roster.Members[index].Target;
            }

            public void Enter(double serverTime = 0.0)
            {
                Vitals.HealthRate = TRIGGER_RATE;
                Gimmick.Tick(serverTime, Phase);
            }

            public void Tick(double serverTime)
            {
                Gimmick.Tick(serverTime, Phase);
            }

            // 선행 기믹과 이 기믹을 등록 순서대로 틱한다
            public void TickAll(double serverTime, int phase)
            {
                Ward.Tick(serverTime, phase);
                Gimmick.Tick(serverTime, phase);
            }

            public void PlaceAll(Vector3 position)
            {
                for (int i = 0; i < Roster.Members.Count; ++i)
                {
                    Roster.Members[i].Target.Position = position;
                }
            }

            // 참가자 번호와 같은 번호의 핵 위에 세운다, 차례의 여우불은 명단 순서의 생존자를 따라가므로 여우불 번호와 핵 번호가 맞는다
            public void StandOnCores()
            {
                int cores = Gimmick.Cores.Count;
                for (int i = 0; (i < cores) && (i < Roster.Members.Count); ++i)
                {
                    Roster.Members[i].Target.Position = Gimmick.Cores[i].Center;
                }
            }

            // 이미 시작한 차례를 끝까지 진행한다. succeed가 참이면 모두 핵 위에서 버틴다. 다음 차례 시작 틱까지 부르려면 next를 참으로 둔다
            public void RunTurn(double start, bool succeed, bool next = true)
            {
                if (succeed)
                {
                    StandOnCores();
                }
                else
                {
                    PlaceAll(Vector3.zero);
                }

                Tick(start + 3.0);
                Tick(start + 7.0);
                Tick(start + 9.0);

                if (next)
                {
                    Tick(start + 10.0);
                }
            }

            // 성공 세 번째 판정(t=29)까지 모두 성공한 차례로 달려 반격에 들어간다
            public void ReachCounter()
            {
                Enter();
                RunTurn(0.0, true);
                RunTurn(10.0, true);
                RunTurn(20.0, true, false);
            }

            public List<DamageRequest> Requests(string definitionId)
            {
                List<DamageRequest> found = new List<DamageRequest>();
                for (int i = 0; i < Damage.Requests.Count; ++i)
                {
                    if (Damage.Requests[i].SourceDefinitionId == definitionId)
                    {
                        found.Add(Damage.Requests[i]);
                    }
                }

                return found;
            }

            // 색 결계를 t=39에 성공시킨다, 이때까지 seal은 앞선 기믹이 끝나기를 기다린다. t=39 틱은 호출하는 쪽이 한다
            public void DriveWardUntilBeforeSuccess()
            {
                Vitals.HealthRate = 0.5f;
                TickAll(0.0, 1);
                PlaceAll(Vector3.zero);
                TickAll(1.0, 1);
                TickAll(9.0, 1);

                for (double time = 9.5; time < 39.0; time += 0.5)
                {
                    PlaceAtOwnWards();
                    TickAll(time, 1);
                }

                PlaceAtOwnWards();
            }

            public void PlaceAtOwnWards()
            {
                for (int i = 0; i < Roster.Members.Count; ++i)
                {
                    FakeCombatTarget target = Roster.Members[i].Target;
                    Ward.TryGetColor(target.CombatId, out EWolbaekWardColor color);
                    target.Position = Ward.Stage.GetCenter(color);
                }
            }
        }

        /****************************************
        *       Entry And Boundary
        ****************************************/

        [Test]
        public void Tick_AboveTrigger_StaysDormantAndOpen()
        {
            Rig r = new Rig(2);
            r.Vitals.HealthRate = 0.8f;

            r.Tick(0.0);

            Assert.AreEqual(EWolbaekSealState.Dormant, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.IsFalse(r.Gimmick.ConsumeCleanupRequest());
            Assert.AreEqual(100f, r.Gimmick.CapDamage(800f, 1000f, 100f), 0.0001f);
        }

        [Test]
        public void CapDamage_BeforeTrigger_StopsExactlyAtBoundaryAndDiscardsExcess()
        {
            Rig r = new Rig(1);

            Assert.AreEqual(50f, r.Gimmick.CapDamage(300f, 1000f, 100f), 0.0001f);
            Assert.AreEqual(50f, r.Gimmick.CapDamage(300f, 1000f, 5000f), 0.0001f);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(250f, 1000f, 50f), 0.0001f);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(300f, 1000f, 0f), 0.0001f);
        }

        [Test]
        public void Tick_AtTrigger_EntersActiveAndRequestsCleanupOnce()
        {
            Rig r = new Rig(2);
            r.Enter();

            Assert.AreEqual(EWolbaekSealState.Active, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.ConsumeCleanupRequest());
            Assert.IsFalse(r.Gimmick.ConsumeCleanupRequest());
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(250f, 1000f, 100f), 0.0001f);
            Assert.AreEqual(1, r.Listener.Count("start"));
            Assert.AreEqual(60.0, r.Listener.Start.LimitEndServerTime - r.Listener.Start.StartServerTime, 0.0001);
        }

        [Test]
        public void Tick_AfterCompletion_NeverStartsTwice()
        {
            Rig r = new Rig(1);
            r.ReachCounter();
            r.Tick(35.0);
            Assert.AreEqual(EWolbaekSealState.Completed, r.Gimmick.State);

            r.Vitals.HealthRate = 0.1f;
            r.Tick(36.0);
            r.Tick(50.0);

            Assert.AreEqual(1, r.Listener.Count("start"));
            Assert.AreEqual(EWolbaekSealState.Completed, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.IsFalse(r.Gimmick.BlocksSelection);
        }

        [Test]
        public void Preceding_StillDeferring_WaitsInvulnerableThenStartsOnTheReleaseTick()
        {
            FakePreceding preceding = new FakePreceding { Defers = true };
            Rig r = new Rig(2, preceding: preceding);

            r.Enter(0.0);
            r.Tick(5.0);

            Assert.AreEqual(EWolbaekSealState.Dormant, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.AreEqual(0, r.Listener.Count("start"));

            preceding.Defers = false;
            r.Tick(6.0);

            Assert.AreEqual(EWolbaekSealState.Active, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Count("start"));
            Assert.AreEqual(6.0, r.Gimmick.Turn.StartServerTime, 0.0001);

            r.Tick(7.0);
            Assert.AreEqual(1, r.Listener.Count("start"));
        }

        [Test]
        public void Preceding_FirstPhase_HoldsTransitionWhileWaiting()
        {
            // 앞선 기믹을 기다리는 동안 체력이 경계에 닿았다면 1페이즈 전환을 막는다
            FakePreceding preceding = new FakePreceding { Defers = true };
            Rig waiting = new Rig(1, preceding: preceding);
            waiting.Phase = 1;
            waiting.Vitals.HealthRate = 0.2f;
            waiting.Tick(0.0);
            Assert.AreEqual(EWolbaekSealState.Dormant, waiting.Gimmick.State);
            Assert.IsTrue(waiting.Gimmick.DefersPhaseTransition);

            // 체력이 경계 위면 막지 않는다
            Rig above = new Rig(1, preceding: preceding);
            above.Phase = 1;
            above.Vitals.HealthRate = 0.8f;
            above.Tick(0.0);
            Assert.IsFalse(above.Gimmick.DefersPhaseTransition);
        }

        /****************************************
        *       Chain With The Ward
        ****************************************/

        [Test]
        public void Chain_WardSuccessReleasesSealOnTheSameTickWhenWardIsTickedFirst()
        {
            Rig r = new Rig(2, withWard: true);
            r.DriveWardUntilBeforeSuccess();

            // 경계 도달이 앞선 기믹 진행 중에 일어난 상황
            r.Vitals.HealthRate = TRIGGER_RATE;
            r.TickAll(38.5, 1);
            Assert.AreEqual(EWolbaekWardState.Strike, r.Ward.State);
            Assert.AreEqual(EWolbaekSealState.Dormant, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);

            r.TickAll(39.0, 1);

            Assert.AreEqual(EWolbaekWardState.Completed, r.Ward.State);
            Assert.AreEqual(EWolbaekSealState.Active, r.Gimmick.State);
            Assert.AreEqual(39.0, r.Gimmick.Turn.StartServerTime, 0.0001);
            Assert.AreEqual(1, r.Listener.Count("start"));

            r.TickAll(40.0, 1);
            Assert.AreEqual(1, r.Listener.Count("start"));
        }

        [Test]
        public void Chain_WardTickedAfterSeal_StartsExactlyOneTickLater()
        {
            Rig r = new Rig(2, withWard: true);
            r.DriveWardUntilBeforeSuccess();
            r.Vitals.HealthRate = TRIGGER_RATE;
            r.TickAll(38.5, 1);

            r.Gimmick.Tick(39.0, 1);
            r.Ward.Tick(39.0, 1);

            Assert.AreEqual(EWolbaekWardState.Completed, r.Ward.State);
            Assert.AreEqual(EWolbaekSealState.Dormant, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);

            r.Gimmick.Tick(39.5, 1);
            r.Ward.Tick(39.5, 1);

            Assert.AreEqual(EWolbaekSealState.Active, r.Gimmick.State);
            Assert.AreEqual(39.5, r.Gimmick.Turn.StartServerTime, 0.0001);
            Assert.AreEqual(1, r.Listener.Count("start"));
        }

        [Test]
        public void Chain_RealDamageThroughTheWardThenSeal_StopsAtEachBoundary()
        {
            Rig r = new Rig(1, withWard: true);
            BossDamageCapComposite composite = new BossDamageCapComposite(r.Ward, r.Gimmick);

            // 50% 기믹 전에는 50%에서 멈춘다
            Assert.IsFalse(composite.IsDamageBlocked);
            Assert.AreEqual(100f, composite.CapDamage(600f, 1000f, 500f), 0.0001f);

            r.DriveWardUntilBeforeSuccess();
            r.TickAll(39.0, 1);
            Assert.AreEqual(EWolbaekWardState.Completed, r.Ward.State);
            Assert.AreEqual(EWolbaekSealState.Dormant, r.Gimmick.State);

            // 50% 기믹이 끝난 뒤에는 25%에서 멈추고 초과분을 버린다
            Assert.IsFalse(composite.IsDamageBlocked);
            Assert.AreEqual(250f, composite.CapDamage(500f, 1000f, 400f), 0.0001f);
            Assert.AreEqual(250f, composite.CapDamage(500f, 1000f, 5000f), 0.0001f);

            r.Vitals.HealthRate = TRIGGER_RATE;
            Assert.IsTrue(composite.IsDamageBlocked);
            Assert.AreEqual(0f, composite.CapDamage(250f, 1000f, 10f), 0.0001f);
        }

        [Test]
        public void Composite_ThreeBoundaries_CapsAtTheLowestAndBlocksIfAnyBlocks()
        {
            Rig r = new Rig(1);
            BossDamageCapComposite composite = new BossDamageCapComposite(new FixedCap { Boundary = 0.75f },
                new FixedCap { Boundary = 0.5f }, r.Gimmick);

            Assert.AreEqual(50f, composite.CapDamage(800f, 1000f, 500f), 0.0001f);
            Assert.AreEqual(0f, composite.CapDamage(500f, 1000f, 50f), 0.0001f);
            Assert.IsFalse(composite.IsDamageBlocked);

            r.Enter();
            Assert.IsTrue(composite.IsDamageBlocked);
        }

        /****************************************
        *            Turn Structure
        ****************************************/

        [Test]
        public void Turn_Timeline_PrepareTrackEscapeResolveCleanup()
        {
            Rig r = new Rig(1);
            r.Enter();

            WolbaekSealTurn turn = r.Gimmick.Turn;
            Assert.AreEqual(0.0, turn.StartServerTime, 0.0001);
            Assert.AreEqual(3.0, turn.TrackStartServerTime, 0.0001);
            Assert.AreEqual(7.0, turn.LockServerTime, 0.0001);
            Assert.AreEqual(9.0, turn.ResolveServerTime, 0.0001);
            Assert.AreEqual(10.0, turn.EndServerTime, 0.0001);
            Assert.AreEqual(EWolbaekSealStep.Prepare, r.Gimmick.Step);

            r.Tick(2.5);
            Assert.AreEqual(EWolbaekSealStep.Prepare, r.Gimmick.Step);

            r.Tick(3.0);
            Assert.AreEqual(EWolbaekSealStep.Track, r.Gimmick.Step);

            r.Tick(6.5);
            Assert.AreEqual(EWolbaekSealStep.Track, r.Gimmick.Step);

            r.Tick(7.0);
            Assert.AreEqual(EWolbaekSealStep.Escape, r.Gimmick.Step);

            r.Tick(8.5);
            Assert.AreEqual(EWolbaekSealStep.Escape, r.Gimmick.Step);

            r.Tick(9.0);
            Assert.AreEqual(EWolbaekSealStep.Cleanup, r.Gimmick.Step);

            r.Tick(9.5);
            Assert.AreEqual(EWolbaekSealStep.Cleanup, r.Gimmick.Step);
            Assert.AreEqual(0, r.Gimmick.TurnIndex);

            r.Tick(10.0);
            Assert.AreEqual(1, r.Gimmick.TurnIndex);
            Assert.AreEqual(EWolbaekSealStep.Prepare, r.Gimmick.Step);
            Assert.AreEqual(10.0, r.Gimmick.Turn.StartServerTime, 0.0001);
            Assert.AreEqual(19.0, r.Gimmick.Turn.ResolveServerTime, 0.0001);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void Turn_CoresAndFoxfiresEqualLivingCount(int party)
        {
            Rig r = new Rig(party);
            r.Enter();

            Assert.AreEqual(party, r.Gimmick.Turn.Cores.Count);
            Assert.AreEqual(party, r.Gimmick.Turn.Foxfires.Count);

            // 핵 사이 간격은 설정의 최소 간격 이상이다
            for (int a = 0; a < party; ++a)
            {
                for (int b = a + 1; b < party; ++b)
                {
                    Vector3 delta = r.Gimmick.Turn.Cores[a].Center - r.Gimmick.Turn.Cores[b].Center;
                    Assert.GreaterOrEqual(delta.magnitude, 6f - 0.001f);
                }
            }

            // 여우불은 생존자 한 명당 하나이고 서로 다른 사람을 따라간다
            HashSet<int> targets = new HashSet<int>();
            for (int i = 0; i < party; ++i)
            {
                targets.Add(r.Gimmick.Turn.Foxfires[i].TargetCombatId);
            }

            Assert.AreEqual(party, targets.Count);
        }

        [Test]
        public void Turn_MoreThanFourParticipants_UsesFour()
        {
            Rig r = new Rig(5);
            r.Enter();

            Assert.AreEqual(4, r.Gimmick.Turn.Cores.Count);
            Assert.AreEqual(4, r.Gimmick.Turn.Foxfires.Count);
        }

        [Test]
        public void Turn_DeadAtStart_IsNotCounted()
        {
            Rig r = new Rig(3);
            r.Player(1).LifeState = EParticipantLifeState.Dead;

            r.Enter();

            Assert.AreEqual(2, r.Gimmick.Turn.Cores.Count);
            Assert.AreEqual(2, r.Gimmick.Turn.Foxfires.Count);
            for (int i = 0; i < 2; ++i)
            {
                Assert.AreNotEqual(r.Player(1).CombatId, r.Gimmick.Turn.Foxfires[i].TargetCombatId);
            }
        }

        [Test]
        public void Turn_DeathDuringTurn_KeepsCountsAndNextTurnRecounts()
        {
            Rig r = new Rig(3);
            r.Enter();
            Assert.AreEqual(3, r.Gimmick.Turn.Foxfires.Count);

            r.Player(2).LifeState = EParticipantLifeState.Dead;
            r.Tick(2.0);
            r.Tick(5.0);

            Assert.AreEqual(3, r.Gimmick.Turn.Cores.Count);
            Assert.AreEqual(3, r.Gimmick.Turn.Foxfires.Count);
            Assert.AreEqual(EWolbaekSealState.Active, r.Gimmick.State);

            r.Tick(9.0);
            r.Tick(10.0);

            Assert.AreEqual(1, r.Gimmick.TurnIndex);
            Assert.AreEqual(2, r.Gimmick.Turn.Cores.Count);
            Assert.AreEqual(2, r.Gimmick.Turn.Foxfires.Count);
        }

        [Test]
        public void Turn_RevivedMidTurn_JoinsFromTheNextTurn()
        {
            Rig r = new Rig(3);
            r.Player(2).LifeState = EParticipantLifeState.Dead;
            r.Enter();
            Assert.AreEqual(2, r.Gimmick.Turn.Foxfires.Count);

            r.Player(2).LifeState = EParticipantLifeState.Alive;
            r.Tick(2.0);
            r.Tick(8.0);
            Assert.AreEqual(2, r.Gimmick.Turn.Cores.Count);
            Assert.AreEqual(2, r.Gimmick.Turn.Foxfires.Count);

            r.Tick(9.0);
            r.Tick(10.0);

            Assert.AreEqual(3, r.Gimmick.Turn.Cores.Count);
            Assert.AreEqual(3, r.Gimmick.Turn.Foxfires.Count);
        }

        [Test]
        public void Turn_DisconnectedSurvivorStillCountsAndIsDetachedAtOnce()
        {
            Rig r = new Rig(2);
            r.Member(FIRST_PLAYER + 1).Connected = false;
            r.Player(1).Position = new Vector3(4f, 0f, 4f);

            r.Enter();

            Assert.AreEqual(2, r.Gimmick.Turn.Cores.Count);
            Assert.AreEqual(2, r.Gimmick.Turn.Foxfires.Count);
            Assert.AreEqual(1, r.Listener.Count("detached:0/1"));
        }

        [Test]
        public void Turn_Events_ReportedInTimeOrderForACoarseTick()
        {
            Rig r = new Rig(1);
            r.Enter();

            r.Tick(10.0);

            List<string> kinds = new List<string>();
            for (int i = 0; i < r.Listener.Events.Count; ++i)
            {
                string name = r.Listener.Events[i];
                int colon = name.IndexOf(':');
                int at    = name.IndexOf('@');
                int cut   = (colon >= 0) ? colon : ((at >= 0) ? at : name.Length);
                kinds.Add(name.Substring(0, cut));
            }

            CollectionAssert.AreEqual(new[] { "start", "turn", "tracking", "lock", "resolve", "turn" }, kinds);
        }

        [Test]
        public void Tick_RepeatedAtTheSameTime_DoesNotRepeatEvents()
        {
            Rig r = new Rig(1);
            r.Enter();

            r.Tick(9.0);
            int events = r.Listener.Events.Count;
            int requests = r.Damage.Requests.Count;
            r.Tick(9.0);
            r.Tick(9.0);

            Assert.AreEqual(events, r.Listener.Events.Count);
            Assert.AreEqual(requests, r.Damage.Requests.Count);
            Assert.AreEqual(1, r.Listener.Count("resolve"));
        }

        /****************************************
        *            Foxfire Tracking
        ****************************************/

        [Test]
        public void Foxfire_FollowsTheTargetOnlyWhileTrackingAndFreezesAtLock()
        {
            Rig r = new Rig(1);
            r.Player(0).Position = new Vector3(1f, 0f, 1f);
            r.Enter();

            r.Player(0).Position = new Vector3(2f, 0f, 2f);
            r.Tick(2.0);
            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot prepare));
            Assert.AreEqual(1f, prepare.FoxfirePositions[0].x, 0.0001f);

            r.Tick(3.0);
            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot track));
            Assert.AreEqual(2f, track.FoxfirePositions[0].x, 0.0001f);
            Assert.IsFalse(track.FoxfiresLocked);

            r.Player(0).Position = new Vector3(4f, 0f, 5f);
            r.Tick(5.0);
            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot follow));
            Assert.AreEqual(4f, follow.FoxfirePositions[0].x, 0.0001f);

            r.Player(0).Position = new Vector3(6f, 0f, 6f);
            r.Tick(7.0);
            Assert.AreEqual(1, r.Listener.Locks.Count);
            Assert.AreEqual(6f, r.Listener.Locks[0][0].x, 0.0001f);

            r.Player(0).Position = new Vector3(9f, 0f, 9f);
            r.Tick(8.0);
            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot escape));
            Assert.IsTrue(escape.FoxfiresLocked);
            Assert.AreEqual(6f, escape.FoxfirePositions[0].x, 0.0001f);

            r.Tick(9.0);
            Assert.AreEqual(0, r.Requests(BLAST_ID).Count);
        }

        [Test]
        public void Foxfire_TargetDiesDuringTrack_StaysAtLastPositionAndIsNeverHandedOver()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(2f, 0f, 2f);
            r.Player(1).Position = new Vector3(-6f, 0f, -6f);
            r.Enter();
            r.Tick(3.0);

            r.Player(0).Position = new Vector3(3f, 0f, 3f);
            r.Tick(4.0);

            // 쓰러진 뒤 시신 위치가 어디로 바뀌든 마지막으로 읽은 서버 위치를 쓴다
            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Player(0).Position  = new Vector3(15f, 0f, 15f);
            r.Tick(5.0);
            Assert.AreEqual(1, r.Listener.Count("detached:0/0"));

            // 부활해도 다시 따라가지 않는다
            r.Player(0).LifeState = EParticipantLifeState.Alive;
            r.Player(0).Position  = new Vector3(-12f, 0f, 5f);
            r.Player(1).Position  = new Vector3(-5f, 0f, -5f);
            r.Tick(6.0);
            r.Tick(7.0);

            Assert.AreEqual(1, r.Listener.Locks.Count);
            Assert.AreEqual(3f, r.Listener.Locks[0][0].x, 0.0001f);
            Assert.AreEqual(3f, r.Listener.Locks[0][0].z, 0.0001f);
            Assert.AreEqual(-5f, r.Listener.Locks[0][1].x, 0.0001f);
            Assert.AreEqual(2, r.Gimmick.Turn.Foxfires.Count);
        }

        [Test]
        public void Foxfire_TargetDisconnectsDuringPrepare_ExplodesAtTheScheduledTimeAtItsLastPosition()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(4f, 0f, 4f);
            r.Player(1).Position = new Vector3(-8f, 0f, 8f);
            r.Enter();

            r.Member(FIRST_PLAYER).Connected = false;
            r.Player(0).Position = new Vector3(-9f, 0f, -9f);
            r.Tick(2.0);
            r.Member(FIRST_PLAYER).Connected = true;
            r.Tick(3.0);
            r.Tick(7.0);

            Assert.AreEqual(4f, r.Listener.Locks[0][0].x, 0.0001f);
            Assert.AreEqual(4f, r.Listener.Locks[0][0].z, 0.0001f);

            // 고정된 낙하 위치에 서 있던 사람은 예정 시각에 맞는다, 폭발은 대상의 생사와 상관없다
            r.Player(1).Position = new Vector3(4f, 0f, 4f);
            r.Tick(8.9);
            Assert.AreEqual(0, r.Requests(BLAST_ID).Count);

            r.Tick(9.0);
            Assert.AreEqual(1, r.Listener.Count("resolve"));
            Assert.IsTrue(r.Requests(BLAST_ID).Count >= 1);
            Assert.AreEqual(4f, r.Requests(BLAST_ID)[0].SourcePosition.x, 0.0001f);
        }

        /****************************************
        *                 Blast
        ****************************************/

        [Test]
        public void Blast_Hit_IsPlainTwentyFivePercentOfMaxHealthBlockableAndWithoutPush()
        {
            Rig r = new Rig(1);
            r.Player(0).Position = new Vector3(3f, 0f, 3f);
            r.Roster.MaxHealth = 4255f;

            r.Enter();
            r.Tick(3.0);
            r.Tick(7.0);
            r.Tick(9.0);

            List<DamageRequest> blasts = r.Requests(BLAST_ID);
            Assert.AreEqual(1, blasts.Count);
            Assert.AreEqual(4255f * 0.25f, blasts[0].RawDamage, 0.001f);
            Assert.AreEqual(EDamageFlags.None, blasts[0].Flags);
            Assert.IsFalse(blasts[0].IsUnblockable);
            Assert.AreEqual(FIRST_PLAYER, blasts[0].HitTargetId);
            Assert.AreEqual(0, r.Displacer.Pushes.Count);
        }

        [Test]
        public void Blast_ExactlyAtBlastRadius_StillHits()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(0f, 0f, 0f);
            r.Player(1).Position = new Vector3(10f, 0f, 10f);

            r.Enter();
            r.Tick(3.0);
            r.Tick(7.0);

            r.Player(1).Position = new Vector3(1.5f, 0f, 0f);
            r.Tick(9.0);

            int hitsOnSecond = 0;
            List<DamageRequest> blasts = r.Requests(BLAST_ID);
            for (int i = 0; i < blasts.Count; ++i)
            {
                if (blasts[i].HitTargetId == FIRST_PLAYER + 1)
                {
                    ++hitsOnSecond;
                }
            }

            Assert.AreEqual(1, hitsOnSecond);
        }

        [Test]
        public void Blast_JustOutsideBlastRadius_Misses()
        {
            Rig r = new Rig(2);
            r.Player(0).Position = new Vector3(0f, 0f, 0f);
            r.Player(1).Position = new Vector3(10f, 0f, 10f);

            r.Enter();
            r.Tick(3.0);
            r.Tick(7.0);

            r.Player(1).Position = new Vector3(1.51f, 0f, 0f);
            r.Tick(9.0);

            for (int i = 0; i < r.Requests(BLAST_ID).Count; ++i)
            {
                Assert.AreNotEqual(FIRST_PLAYER + 1, r.Requests(BLAST_ID)[i].HitTargetId);
            }
        }

        [Test]
        public void Blast_DeadPlayerIsNotHit_AndEachBlastGetsItsOwnActionNumber()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.Tick(3.0);
            r.Tick(7.0);

            // 두 사람이 같은 자리에 겹치면 두 발이 모두 맞는다, 한 발마다 동작 번호가 다르다
            r.Player(1).Position = r.Player(0).Position;
            r.Tick(9.0);

            List<DamageRequest> blasts = r.Requests(BLAST_ID);
            Assert.AreEqual(4, blasts.Count);
            HashSet<int> actions = new HashSet<int>();
            for (int i = 0; i < blasts.Count; ++i)
            {
                actions.Add(blasts[i].ActionInstanceId);
            }

            Assert.AreEqual(2, actions.Count);

            Rig dead = new Rig(2);
            dead.Enter();
            dead.Tick(3.0);
            dead.Tick(7.0);
            dead.Player(1).LifeState = EParticipantLifeState.Dead;
            dead.Tick(9.0);

            for (int i = 0; i < dead.Requests(BLAST_ID).Count; ++i)
            {
                Assert.AreNotEqual(FIRST_PLAYER + 1, dead.Requests(BLAST_ID)[i].HitTargetId);
            }
        }

        /****************************************
        *               Cores
        ****************************************/

        [Test]
        public void Core_CenterWithinHitRadius_IsDestroyedAndTurnSucceeds()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.Player(0).Position = r.Gimmick.Cores[0].Center + new Vector3(1.5f, 0f, 0f);

            r.Tick(3.0);
            r.Tick(7.0);
            r.Tick(9.0);

            Assert.IsTrue(r.Gimmick.IsCoreDestroyed(0));
            Assert.AreEqual(1, r.Gimmick.SuccessCount);
            CollectionAssert.Contains(r.Listener.Events, "resolve:0:1/1:True:1@9");
        }

        [Test]
        public void Core_BeyondHitRadius_IsNotDestroyed()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.Player(0).Position = r.Gimmick.Cores[0].Center + new Vector3(1.51f, 0f, 0f);

            r.Tick(3.0);
            r.Tick(7.0);
            r.Tick(9.0);

            Assert.IsFalse(r.Gimmick.IsCoreDestroyed(0));
            Assert.AreEqual(0, r.Gimmick.SuccessCount);
            CollectionAssert.Contains(r.Listener.Events, "resolve:0:0/1:False:0@9");
        }

        [Test]
        public void Core_OneFoxfireDestroysOnlyOneCoreAndTwoFoxfiresOnOneCoreDestroyItOnce()
        {
            Rig r = new Rig(2);
            r.Enter();

            // 두 발이 같은 핵에 떨어지면 핵 하나만 부서지고 다른 핵은 남는다
            Vector3 first = r.Gimmick.Cores[0].Center;
            r.Player(0).Position = first;
            r.Player(1).Position = first;

            r.Tick(3.0);
            r.Tick(7.0);
            r.Tick(9.0);

            Assert.IsTrue(r.Gimmick.IsCoreDestroyed(0));
            Assert.IsFalse(r.Gimmick.IsCoreDestroyed(1));
            Assert.AreEqual(0, r.Gimmick.SuccessCount);
            CollectionAssert.Contains(r.Listener.Events, "resolve:0:1/2:False:0@9");
        }

        [Test]
        public void Core_CoresArePositionsNotPersonalAssignments_AnyFoxfireCanBreakAnyCore()
        {
            Rig r = new Rig(2);
            r.Enter();

            // 여우불 0번이 핵 1번을, 1번이 핵 0번을 부숴도 성공한다
            r.Player(0).Position = r.Gimmick.Cores[1].Center;
            r.Player(1).Position = r.Gimmick.Cores[0].Center;

            r.Tick(3.0);
            r.Tick(7.0);
            r.Tick(9.0);

            Assert.AreEqual(1, r.Gimmick.SuccessCount);
        }

        /****************************************
        *           Success Accumulation
        ****************************************/

        [Test]
        public void Success_ThreeTurnsInARow_PassesAtTheThirdResolveAndOpensTheCounter()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.RunTurn(0.0, true);
            r.RunTurn(10.0, true);
            r.RunTurn(20.0, true, false);

            Assert.AreEqual(EWolbaekSealState.Counter, r.Gimmick.State);
            Assert.AreEqual(3, r.Gimmick.SuccessCount);
            Assert.AreEqual(35.0, r.Gimmick.CounterEndServerTime, 0.0001);
            CollectionAssert.Contains(r.Listener.Events, "success@29/35");
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
        }

        [Test]
        public void Success_AFailedTurnKeepsEarlierSuccesses()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.RunTurn(0.0, true);
            Assert.AreEqual(1, r.Gimmick.SuccessCount);

            r.RunTurn(10.0, false);
            Assert.AreEqual(1, r.Gimmick.SuccessCount);
            Assert.AreEqual(EWolbaekSealState.Active, r.Gimmick.State);

            r.RunTurn(20.0, true);
            Assert.AreEqual(2, r.Gimmick.SuccessCount);

            r.RunTurn(30.0, true, false);
            Assert.AreEqual(3, r.Gimmick.SuccessCount);
            Assert.AreEqual(EWolbaekSealState.Counter, r.Gimmick.State);
            CollectionAssert.Contains(r.Listener.Events, "success@39/45");
        }

        [Test]
        public void Success_ClearsAttacksGateAndNeverHurtsAfterwards()
        {
            Rig r = new Rig(1, hard: true);
            r.ReachCounter();
            int requests = r.Damage.Requests.Count;

            r.Tick(30.0);
            r.Tick(34.0);

            Assert.AreEqual(requests, r.Damage.Requests.Count);
            Assert.AreEqual(EWolbaekSealState.Counter, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot active) && (active.State == EWolbaekSealState.Active));
        }

        /****************************************
        *        Sixty Seconds And Wipe
        ****************************************/

        [Test]
        public void TimeLimit_NoSuccess_FailsExactlyAtSixtySecondsWithoutAnotherTurn()
        {
            Rig r = new Rig(1);
            r.Enter();
            for (int turn = 0; turn < 5; ++turn)
            {
                r.RunTurn(turn * 10.0, false);
            }

            r.Tick(55.0);
            r.Tick(59.0);
            Assert.AreEqual(EWolbaekSealState.Active, r.Gimmick.State);
            Assert.AreEqual(5, r.Gimmick.TurnIndex);

            r.Tick(59.9);
            Assert.AreEqual(EWolbaekSealState.Active, r.Gimmick.State);

            r.Tick(60.0);

            Assert.AreEqual(EWolbaekSealState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekSealFail.TimeExpired, r.Gimmick.FailReason);
            Assert.AreEqual(6, r.Listener.Count("turn:"));
            Assert.AreEqual(1, r.Listener.Count("fail:"));
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
        }

        [Test]
        public void TimeLimit_CoarseTick_ProcessesEveryDueTurnBeforeTheExpiry()
        {
            Rig r = new Rig(1);
            r.Enter();

            r.Tick(100.0);

            Assert.AreEqual(EWolbaekSealState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekSealFail.TimeExpired, r.Gimmick.FailReason);
            Assert.AreEqual(6, r.Listener.Count("resolve"));
            Assert.AreEqual(6, r.Listener.Count("turn:"));
        }

        [Test]
        public void TimeLimit_ThirdSuccessOnTheLimitTick_SuccessWins()
        {
            Rig r = new Rig(1, limit: 29f);
            r.Enter();
            r.RunTurn(0.0, true);
            r.RunTurn(10.0, true);
            r.RunTurn(20.0, true, false);

            Assert.AreEqual(EWolbaekSealState.Counter, r.Gimmick.State);
            Assert.AreEqual(0, r.Listener.Count("fail:"));
            Assert.AreEqual(3, r.Gimmick.SuccessCount);
        }

        [Test]
        public void TimeLimit_OneTickTooLate_FailsEvenWithTwoSuccesses()
        {
            Rig r = new Rig(1, limit: 28.5f);
            r.Enter();
            r.RunTurn(0.0, true);
            r.RunTurn(10.0, true);

            r.StandOnCores();
            r.Tick(23.0);
            r.Tick(27.0);
            r.Tick(29.0);

            Assert.AreEqual(EWolbaekSealState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekSealFail.TimeExpired, r.Gimmick.FailReason);
            Assert.AreEqual(2, r.Gimmick.SuccessCount);
        }

        [Test]
        public void TimeLimit_ThirdSuccessInsideTheLimitButTickedLate_SuccessWins()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.RunTurn(0.0, true);
            r.RunTurn(10.0, true);

            // 세 번째 판정은 t=29로 제한 안인데 서버 틱이 제한 시각을 넘겨서 불린 경우
            r.StandOnCores();
            r.Tick(75.0);

            Assert.AreEqual(EWolbaekSealState.Counter, r.Gimmick.State);
            Assert.AreEqual(3, r.Gimmick.SuccessCount);
            Assert.AreEqual(0, r.Listener.Count("fail:"));
        }

        [Test]
        public void Wipe_AtTickStart_FailsAndNothingMoreHurts()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Player(1).LifeState = EParticipantLifeState.Dead;

            r.Tick(5.0);

            Assert.AreEqual(EWolbaekSealState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekSealFail.Wipe, r.Gimmick.FailReason);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);

            r.Player(0).LifeState = EParticipantLifeState.Alive;
            r.Tick(9.0);
            r.Tick(20.0);

            Assert.AreEqual(0, r.Damage.Requests.Count);
            Assert.AreEqual(1, r.Listener.Count("fail:"));
        }

        [Test]
        public void Wipe_OnTheThirdSuccessTick_FailureWins()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.RunTurn(0.0, true);
            r.RunTurn(10.0, true);

            r.StandOnCores();
            r.Tick(23.0);
            r.Tick(27.0);

            // 마지막 판정 시각의 틱에 마지막 생존자가 먼저 쓰러져 있다
            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Tick(29.0);

            Assert.AreEqual(EWolbaekSealState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekSealFail.Wipe, r.Gimmick.FailReason);
            Assert.AreEqual(2, r.Gimmick.SuccessCount);
            Assert.AreEqual(0, r.Listener.Count("success"));
        }

        [Test]
        public void Wipe_CausedByTheThirdBlast_FailureWinsOverSuccess()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.RunTurn(0.0, true);
            r.RunTurn(10.0, true);

            r.StandOnCores();
            r.Tick(23.0);
            r.Tick(27.0);

            // 같은 틱의 폭발이 마지막 생존자를 쓰러뜨린다
            r.Damage.Lethal = true;
            r.Tick(29.0);

            Assert.AreEqual(EWolbaekSealState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekSealFail.Wipe, r.Gimmick.FailReason);
            Assert.AreEqual(0, r.Listener.Count("success"));
            Assert.AreEqual(1, r.Listener.Count("fail:"));
        }

        [Test]
        public void Wipe_EveryoneDeadWhileSomeoneIsRevivable_StillFails()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Player(1).LifeState = EParticipantLifeState.Dead;
            r.Member(FIRST_PLAYER).Revivable = true;

            r.Tick(1.0);

            Assert.AreEqual(EWolbaekSealFail.Wipe, r.Gimmick.FailReason);
        }

        /****************************************
        *         Hard Sweeps
        ****************************************/

        [Test]
        public void Sweep_Timeline_TelegraphsEightTenthsBeforeAndStrikesAtOnePointFiveAndThree()
        {
            Rig r = new Rig(1, hard: true);
            r.Enter();

            r.Tick(3.0);
            r.Tick(3.6);
            Assert.AreEqual(0, r.Listener.Telegraphs.Count);

            r.Tick(3.75);
            Assert.AreEqual(1, r.Listener.Telegraphs.Count);
            Assert.AreEqual(3.7, r.Listener.Telegraphs[0].TelegraphServerTime, 0.001);
            Assert.AreEqual(4.5, r.Listener.Telegraphs[0].StrikeServerTime, 0.0001);

            r.Tick(4.4);
            Assert.AreEqual(0, r.Listener.SweepHits.Count);

            r.Tick(4.5);
            Assert.AreEqual(1, r.Listener.SweepHits.Count);

            r.Tick(5.1);
            Assert.AreEqual(1, r.Listener.Telegraphs.Count);

            r.Tick(5.25);
            Assert.AreEqual(2, r.Listener.Telegraphs.Count);
            Assert.AreEqual(5.2, r.Listener.Telegraphs[1].TelegraphServerTime, 0.001);
            Assert.AreEqual(6.0, r.Listener.Telegraphs[1].StrikeServerTime, 0.0001);

            r.Tick(5.9);
            Assert.AreEqual(1, r.Listener.SweepHits.Count);

            r.Tick(6.0);
            Assert.AreEqual(2, r.Listener.SweepHits.Count);
        }

        [Test]
        public void Sweep_SecondDirectionIsTheOppositeOfTheFirst()
        {
            Rig r = new Rig(1, hard: true);
            r.Enter();
            r.Tick(3.0);
            r.Tick(6.0);

            Assert.AreEqual(2, r.Listener.Telegraphs.Count);
            Vector3 first  = r.Listener.Telegraphs[0].Direction;
            Vector3 second = r.Listener.Telegraphs[1].Direction;
            Assert.AreEqual(-1f, Vector3.Dot(first, second), 0.0001f);
            Assert.AreEqual(1f, first.magnitude, 0.0001f);
            Assert.AreEqual(SWEEP_RANGE, r.Listener.Telegraphs[0].Range, 0.0001f);
            Assert.AreEqual(SWEEP_ANGLE, r.Listener.Telegraphs[0].HalfAngleDegrees, 0.0001f);
        }

        [Test]
        public void Sweep_Hit_IsTwentyFivePercentUnblockableAndPushesOneMeterOutward()
        {
            Rig r = new Rig(1, hard: true);
            r.Enter();
            r.Tick(3.0);

            r.Player(0).Position = new Vector3(0f, 0f, 5f);
            r.Tick(3.75);
            r.Tick(4.5);

            List<DamageRequest> sweeps = r.Requests(SWEEP_ID);
            Assert.AreEqual(1, sweeps.Count);
            Assert.AreEqual(250f, sweeps[0].RawDamage, 0.001f);
            Assert.IsTrue(sweeps[0].IsUnblockable);
            Assert.AreEqual(1, r.Displacer.Pushes.Count);
            Assert.AreEqual(1f, r.Displacer.Pushes[0].Distance, 0.0001f);
            Assert.AreEqual(1f, r.Displacer.Pushes[0].Direction.z, 0.0001f);

            // 반대 방향에 서 있던 사람은 두 번째 휩쓸기에 맞는다
            r.Player(0).Position = new Vector3(0f, 0f, -5f);
            r.Tick(5.25);
            r.Tick(6.0);

            Assert.AreEqual(2, r.Requests(SWEEP_ID).Count);
            Assert.AreEqual(-1f, r.Displacer.Pushes[1].Direction.z, 0.0001f);
        }

        [Test]
        public void Sweep_OutsideAngleOrRange_MissesAndBoundariesAreInclusive()
        {
            Rig r = new Rig(4, hard: true);
            r.Enter();
            r.Tick(3.0);

            r.Player(0).Position = new Vector3(0f, 0f, 16f);
            r.Player(1).Position = new Vector3(0f, 0f, 17f);
            r.Player(2).Position = new Vector3(Mathf.Sin(60f * Mathf.Deg2Rad) * 5f, 0f, Mathf.Cos(60f * Mathf.Deg2Rad) * 5f);
            r.Player(3).Position = new Vector3(Mathf.Sin(61f * Mathf.Deg2Rad) * 5f, 0f, Mathf.Cos(61f * Mathf.Deg2Rad) * 5f);
            r.Tick(3.75);
            r.Tick(4.5);

            List<DamageRequest> sweeps = r.Requests(SWEEP_ID);
            HashSet<int> hit = new HashSet<int>();
            for (int i = 0; i < sweeps.Count; ++i)
            {
                hit.Add(sweeps[i].HitTargetId);
            }

            Assert.IsTrue(hit.Contains(FIRST_PLAYER));
            Assert.IsFalse(hit.Contains(FIRST_PLAYER + 1));
            Assert.IsTrue(hit.Contains(FIRST_PLAYER + 2));
            Assert.IsFalse(hit.Contains(FIRST_PLAYER + 3));
        }

        [Test]
        public void Sweep_DoesNotInterruptFoxfireTrackingAndSkipsTheDead()
        {
            Rig r = new Rig(2, hard: true);
            r.Enter();
            r.Tick(3.0);

            r.Player(0).Position = new Vector3(0f, 0f, 5f);
            r.Player(1).Position = new Vector3(0f, 0f, 6f);
            r.Player(1).LifeState = EParticipantLifeState.Dead;
            r.Tick(3.75);
            r.Tick(4.5);

            List<DamageRequest> sweeps = r.Requests(SWEEP_ID);
            Assert.AreEqual(1, sweeps.Count);
            Assert.AreEqual(FIRST_PLAYER, sweeps[0].HitTargetId);

            // 맞은 뒤에도 여우불은 계속 따라가 고정 위치가 마지막 위치와 같다
            r.Player(0).Position = new Vector3(5f, 0f, 5f);
            r.Tick(7.0);
            Assert.AreEqual(5f, r.Listener.Locks[0][0].x, 0.0001f);
            Assert.AreEqual(5f, r.Listener.Locks[0][0].z, 0.0001f);
        }

        [Test]
        public void Sweep_EasyHasNoSweepsAndHardRequiresADisplacer()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.Tick(3.0);
            r.Tick(7.0);
            r.Tick(9.0);

            Assert.AreEqual(0, r.Listener.Telegraphs.Count);
            Assert.AreEqual(0, r.Requests(SWEEP_ID).Count);

            Assert.Throws<System.ArgumentNullException>(() => new WolbaekFoxfireSeal(MakeSpec(true), new StillBoss(), new FakeBossVitals(),
                new FakeRoster(), new FakeRoster(), new FakeArena(), new FakeSites(SITE_COUNT), new RecordingResolver(), new PartyQuery(),
                new ScriptedRandom()));
        }

        [Test]
        public void Sweep_RepeatsEveryTurn()
        {
            Rig r = new Rig(1, hard: true);
            r.Enter();
            r.RunTurn(0.0, false);
            Assert.AreEqual(2, r.Listener.Telegraphs.Count);

            r.Tick(13.75);
            r.Tick(14.5);

            Assert.AreEqual(3, r.Listener.Telegraphs.Count);
            Assert.AreEqual(3, r.Listener.SweepHits.Count);
            Assert.AreEqual(1, r.Listener.Telegraphs[2].TurnIndex);
            Assert.AreEqual(0, r.Listener.Telegraphs[2].SweepIndex);
            Assert.AreEqual(1, r.Gimmick.TurnIndex);
        }

        /****************************************
        *               Counter
        ****************************************/

        [Test]
        public void Counter_Hard_StopsExactlyAtZeroDiscardsExcessAndHoldsDeath()
        {
            Rig r = new Rig(1, hard: true);
            r.ReachCounter();

            r.Vitals.HealthRate = 0.25f;
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.AreEqual(250f, r.Gimmick.CapDamage(250f, 1000f, 300f), 0.0001f);
            Assert.AreEqual(100f, r.Gimmick.CapDamage(250f, 1000f, 100f), 0.0001f);
            Assert.IsTrue(r.Gimmick.IsDeathHeld);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);

            r.Vitals.HealthRate = 0f;
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(0f, 1000f, 10f), 0.0001f);
            Assert.IsTrue(r.Gimmick.IsDeathHeld);
        }

        [Test]
        public void Counter_Easy_HasNoBoundaryAndNeverHoldsDeath()
        {
            Rig r = new Rig(1);
            r.ReachCounter();

            r.Vitals.HealthRate = 0.25f;
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.AreEqual(300f, r.Gimmick.CapDamage(250f, 1000f, 300f), 0.0001f);
            Assert.IsFalse(r.Gimmick.IsDeathHeld);

            r.Vitals.HealthRate = 0f;
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.IsFalse(r.Gimmick.IsDeathHeld);

            r.Tick(35.0);
            Assert.AreEqual(EWolbaekSealState.Completed, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.HasPendingHandoff);
        }

        [Test]
        public void Counter_EndsAfterSixSecondsAndResumesNormalFightWhenNotAtTheBoundary()
        {
            Rig r = new Rig(1, hard: true);
            r.ReachCounter();
            r.Vitals.HealthRate = 0.1f;

            r.Tick(34.0);
            Assert.AreEqual(EWolbaekSealState.Counter, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.BlocksSelection);

            r.Tick(35.0);

            Assert.AreEqual(EWolbaekSealState.Completed, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.BlocksSelection);
            Assert.IsFalse(r.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(r.Gimmick.IsDeathHeld);
            CollectionAssert.Contains(r.Listener.Events, "counterEnd:False");
        }

        [Test]
        public void Counter_Hard_AtZeroWhenItEnds_WaitsForTheNextGimmickAndHandsOffOnce()
        {
            Rig r = new Rig(1, hard: true);
            r.ReachCounter();
            r.Vitals.HealthRate = 0f;

            r.Tick(34.0);
            Assert.IsFalse(r.Gimmick.HasPendingHandoff);

            r.Tick(35.0);

            Assert.AreEqual(EWolbaekSealState.NextBoundaryPending, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.HasPendingHandoff);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.IsDeathHeld);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(0f, 1000f, 10f), 0.0001f);
            CollectionAssert.Contains(r.Listener.Events, "counterEnd:True");

            r.Tick(50.0);
            Assert.AreEqual(EWolbaekSealState.NextBoundaryPending, r.Gimmick.State);

            Assert.IsTrue(r.Gimmick.TryTakeHandoff());
            Assert.IsFalse(r.Gimmick.TryTakeHandoff());
            Assert.AreEqual(EWolbaekSealState.Completed, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.HasPendingHandoff);
            Assert.IsFalse(r.Gimmick.IsDeathHeld);
        }

        [Test]
        public void DeathPolicy_HoldsOnlyWhileTheHardCounterAndHandoffHoldIt()
        {
            Rig hard = new Rig(1, hard: true);
            BossGimmickDeathPolicy policy = new BossGimmickDeathPolicy(new ImmediateBossDeathPolicy());
            policy.AddHold(hard.Gimmick);
            FakeBossVitals zero = new FakeBossVitals { HealthRate = 0f, IsHealthZero = true };

            Assert.IsTrue(policy.IsDeathConfirmed(zero, 0.0));

            hard.ReachCounter();
            Assert.IsFalse(policy.IsDeathConfirmed(zero, 30.0));

            hard.Vitals.HealthRate = 0f;
            hard.Tick(35.0);
            Assert.IsFalse(policy.IsDeathConfirmed(zero, 36.0));

            hard.Gimmick.TryTakeHandoff();
            Assert.IsTrue(policy.IsDeathConfirmed(zero, 37.0));

            Rig easy = new Rig(1);
            BossGimmickDeathPolicy easyPolicy = new BossGimmickDeathPolicy(new ImmediateBossDeathPolicy());
            easyPolicy.AddHold(easy.Gimmick);
            easy.ReachCounter();
            Assert.IsTrue(easyPolicy.IsDeathConfirmed(zero, 30.0));
        }

        /****************************************
        *          Halt And Stop
        ****************************************/

        private static void AssertHaltedSafely(Rig r, EWolbaekSealHaltReason reason)
        {
            Assert.AreEqual(EWolbaekSealState.Halted, r.Gimmick.State);
            Assert.AreEqual(reason, r.Gimmick.HaltReason);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(250f, 1000f, 100f), 0.0001f);
            Assert.AreEqual(0, r.Listener.Count("start"));
            Assert.AreEqual(0, r.Listener.Count("fail:"));
            Assert.AreEqual(1, r.Listener.Count("halt:" + reason));

            r.Tick(20.0);
            r.Tick(80.0);
            Assert.AreEqual(EWolbaekSealState.Halted, r.Gimmick.State);
            Assert.AreEqual(0, r.Damage.Requests.Count);
        }

        [Test]
        public void Halt_ArenaUnavailable_HaltsAndKeepsBossInvulnerable()
        {
            Rig r = new Rig(2);
            r.Arena.Available = false;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("전장 정보"));
            r.Enter();

            AssertHaltedSafely(r, EWolbaekSealHaltReason.ArenaUnavailable);
        }

        [Test]
        public void Halt_NoCandidates_HaltsWithoutStartingOrFailing()
        {
            Rig r = new Rig(2, siteCount: 0);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("핵 후보"));
            r.Enter();

            AssertHaltedSafely(r, EWolbaekSealHaltReason.SitesInsufficient);
        }

        [Test]
        public void Halt_FewerCandidatesThanTheRoster_Halts()
        {
            Rig r = new Rig(3, siteCount: 2);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("핵 후보"));
            r.Enter();

            AssertHaltedSafely(r, EWolbaekSealHaltReason.SitesInsufficient);
        }

        [Test]
        public void Halt_CandidatesTooCloseToEachOther_Halts()
        {
            Rig r = new Rig(2);
            r.Sites.Centers.Clear();
            for (int i = 0; i < 6; ++i)
            {
                r.Sites.Centers.Add(new Vector3(8f + (i * 0.5f), 0f, 0f));
            }

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("핵 후보"));
            r.Enter();

            AssertHaltedSafely(r, EWolbaekSealHaltReason.SitesInsufficient);
        }

        [Test]
        public void Halt_CandidatesOutsideTheArenaAreIgnored()
        {
            Rig r = new Rig(2);
            r.Sites.Centers.Clear();
            r.Sites.Centers.Add(new Vector3(30f, 0f, 0f));
            r.Sites.Centers.Add(new Vector3(-30f, 0f, 0f));
            r.Sites.Centers.Add(new Vector3(0f, 0f, 30f));

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("핵 후보"));
            r.Enter();

            AssertHaltedSafely(r, EWolbaekSealHaltReason.SitesInsufficient);
        }

        [Test]
        public void Candidates_ManySeededPicks_AlwaysKeepTheMinimumSeparationAndStayInsideTheArena()
        {
            for (int seed = 0; seed < 40; ++seed)
            {
                Rig r = new Rig(4, random: new SeededRandom(seed));
                r.Enter();
                IReadOnlyList<WolbaekSealCore> cores = r.Gimmick.Turn.Cores;
                Assert.AreEqual(4, cores.Count);

                for (int a = 0; a < cores.Count; ++a)
                {
                    Assert.LessOrEqual(cores[a].Center.magnitude, ARENA_RADIUS + 0.001f);
                    for (int b = a + 1; b < cores.Count; ++b)
                    {
                        Assert.GreaterOrEqual((cores[a].Center - cores[b].Center).magnitude, 6f - 0.001f, "seed " + seed);
                    }
                }
            }
        }

        [Test]
        public void Stop_DuringActive_ClearsOnceAndClosesDamage()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.Tick(3.0);

            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);
            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);

            Assert.AreEqual(EWolbaekSealState.Stopped, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Count("cleared:CombatEnd"));

            r.Tick(9.0);
            Assert.AreEqual(0, r.Damage.Requests.Count);
            Assert.IsFalse(r.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot _));
        }

        [Test]
        public void Stop_BeforeEntry_StopsWithoutNotice()
        {
            Rig r = new Rig(1);
            r.Gimmick.Stop(EBossInterruptReason.Death);

            Assert.AreEqual(EWolbaekSealState.Stopped, r.Gimmick.State);
            Assert.AreEqual(0, r.Listener.Events.Count);

            r.Enter();
            Assert.AreEqual(EWolbaekSealState.Stopped, r.Gimmick.State);
        }

        /****************************************
        *               Snapshot
        ****************************************/

        [Test]
        public void Snapshot_OnlyWhileActiveOrCounter_CarriesStepSweepAndDetachedFoxfires()
        {
            Rig r = new Rig(2, hard: true);
            Assert.IsFalse(r.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot none));

            r.Enter();
            r.Tick(3.0);
            r.Tick(3.75);
            r.Player(0).LifeState = EParticipantLifeState.Dead;
            r.Tick(4.0);

            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot snapshot));
            Assert.AreEqual(EWolbaekSealState.Active, snapshot.State);
            Assert.AreEqual(EWolbaekSealStep.Track, snapshot.Step);
            Assert.IsTrue(snapshot.HasPendingSweep);
            Assert.AreEqual(0, snapshot.PendingSweep.SweepIndex);
            Assert.AreEqual(2, snapshot.FoxfirePositions.Count);
            Assert.IsTrue(snapshot.FoxfireDetached[0]);
            Assert.IsFalse(snapshot.FoxfireDetached[1]);
            Assert.AreEqual(0, snapshot.SuccessCount);

            r.Player(0).LifeState = EParticipantLifeState.Alive;
            Rig counter = new Rig(1);
            counter.ReachCounter();
            Assert.IsTrue(counter.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot inCounter));
            Assert.AreEqual(EWolbaekSealState.Counter, inCounter.State);
            Assert.AreEqual(35.0, inCounter.CounterEndServerTime, 0.0001);
            Assert.AreEqual(3, inCounter.SuccessCount);

            counter.Tick(35.0);
            Assert.IsFalse(counter.Gimmick.TryGetSnapshot(out WolbaekSealSnapshot _));
        }

        /****************************************
        *                 Spec
        ****************************************/

        [Test]
        public void Spec_InvalidValues_Throw()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekFoxfireSealSpec(0f, null, 3f, 4f, 2f, 1f, 3, 60f, 1.5f,
                1.5f, 0.25f, 6f, 6f, null));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekFoxfireSealSpec(0.25f, 0.25f, 3f, 4f, 2f, 1f, 3, 60f,
                1.5f, 1.5f, 0.25f, 6f, 6f, null));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekFoxfireSealSpec(0.25f, null, 3f, 4f, 2f, 1f, 0, 60f,
                1.5f, 1.5f, 0.25f, 6f, 6f, null));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekFoxfireSealSpec(0.25f, null, 3f, 4f, 2f, 1f, 3, 60f,
                1.5f, 1.5f, 0.25f, 3f, 6f, null));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekFoxfireSealSpec(0.25f, null, 3f, 4f, 2f, 1f, 3, 60f,
                1.5f, 1.5f, 0.25f, 6f, -1f, null));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekFoxfireSealSpec(0.25f, null, 3f, 1.5f, 2f, 1f, 3, 60f,
                1.5f, 1.5f, 0.25f, 6f, 6f, MakeSweep()));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekSealSweepSpec(new[] { 3f, 1.5f }, 0.8f, 0.25f, 1f, 16f, 60f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekSealSweepSpec(new[] { 0.5f }, 0.8f, 0.25f, 1f, 16f, 60f));
            Assert.Throws<System.ArgumentException>(() => new WolbaekSealSweepSpec(new float[0], 0.8f, 0.25f, 1f, 16f, 60f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekSealSweepSpec(new[] { 1.5f }, 0.8f, 0.25f, 1f, 0f, 60f));
        }

        [Test]
        public void DevCatalog_Easy_HasNoSweepAndNoNextBoundaryAndHardHasBoth()
        {
            WolbaekFoxfireSealSpec easy = WolbaekFoxfireSealDevCatalog.CreateSpec(false);
            WolbaekFoxfireSealSpec hard = WolbaekFoxfireSealDevCatalog.CreateSpec(true);

            Assert.IsNull(easy.Sweep);
            Assert.IsFalse(easy.HasNextBoundary);
            Assert.IsNotNull(hard.Sweep);
            Assert.IsTrue(hard.HasNextBoundary);
            Assert.AreEqual(0f, hard.NextBoundaryHealthRate);
            Assert.AreEqual(3, hard.RequiredSuccesses);
            Assert.AreEqual(60f, hard.TimeLimitSeconds);
            Assert.AreEqual(6f, hard.CounterSeconds);
            Assert.AreEqual(1.5f, hard.BlastRadius);
            Assert.AreEqual(0.25f, hard.BlastDamageRate);
        }

        /****************************************
        *         Brain Integration
        ****************************************/

        private sealed class BrainRig
        {
            public BossHarness        Harness  { get; }
            public WolbaekFoxfireSeal Seal     { get; }
            public WolbaekColorWard   Ward     { get; }
            public Recorder           Listener { get; } = new Recorder();
            public StillBoss          Boss     { get; } = new StillBoss();
            public BossGimmickDeathPolicy Policy { get; } = new BossGimmickDeathPolicy(new ImmediateBossDeathPolicy());

            public BrainRig(bool hard, bool withWard, float phaseRate)
            {
                Harness = new BossHarness(BossTestFixtures.MakeBoss(phaseRate), Policy, BossTestFixtures.MakePattern("p1"));
                Harness.Factory.HitOffsets = new[] { 0.0 };

                FakeRoster roster = new FakeRoster();
                roster.Members.Add(new Participant { Target = Harness.Player });
                PartyQuery query = new PartyQuery();
                query.Players.Add(Harness.Player);
                FakeSites sites = new FakeSites(SITE_COUNT);

                IBossGimmick preceding = null;
                if (withWard)
                {
                    Ward = new WolbaekColorWard(MakeWardSpec(), Boss, Harness.Vitals, roster, roster, new FakeArena(), sites,
                        Harness.Damage, query, new ScriptedRandom(), new FakeDisplacer());
                    Assert.IsTrue(Harness.Brain.AddGimmick(Ward));
                    preceding = Ward;
                }

                Seal = new WolbaekFoxfireSeal(MakeSpec(hard), Boss, Harness.Vitals, roster, roster, new FakeArena(), sites,
                    Harness.Damage, query, new ScriptedRandom(), new FakeDisplacer(), preceding, Listener);
                Assert.IsTrue(Harness.Brain.AddGimmick(Seal));
                Policy.AddHold(Seal);
                Assert.IsTrue(Harness.Brain.Begin(0.0));
            }

            public void Tick(double serverTime)
            {
                Harness.Brain.Tick(serverTime);
            }

            // 진입한 차례를 시작 시각 기준으로 끝까지 진행한다, 플레이어는 핵 위에 선다
            public void RunTurn(double start, bool next)
            {
                Harness.Player.Position = Seal.Cores[0].Center;
                Tick(start + 3.0);
                Tick(start + 7.0);
                Tick(start + 9.0);

                if (next)
                {
                    Tick(start + 10.0);
                }
            }
        }

        [Test]
        public void Brain_Entry_CancelsRunningPatternBlocksSelectionThroughTheCounterThenResumes()
        {
            BrainRig b = new BrainRig(false, false, 0f);
            b.Tick(0.0);
            Assert.AreEqual(EBossAttackState.Telegraph, b.Harness.Brain.AttackState);
            int created = b.Harness.Factory.CreateCount;

            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);

            Assert.AreEqual(EWolbaekSealState.Active, b.Seal.State);
            Assert.AreEqual(EBossAttackState.Ready, b.Harness.Brain.AttackState);
            Assert.AreEqual(EBossInterruptReason.GimmickEntry, b.Harness.Factory.Last.CancelReason);

            b.RunTurn(0.25, true);
            b.RunTurn(10.25, true);
            b.RunTurn(20.25, false);

            Assert.AreEqual(EWolbaekSealState.Counter, b.Seal.State);
            Assert.AreEqual(created, b.Harness.Factory.CreateCount);

            b.Tick(33.0);
            Assert.AreEqual(created, b.Harness.Factory.CreateCount);

            b.Tick(35.25);
            Assert.AreEqual(EWolbaekSealState.Completed, b.Seal.State);

            b.Tick(36.0);
            Assert.Greater(b.Harness.Factory.CreateCount, created);
        }

        [Test]
        public void Brain_Hard_BossAtZeroDuringTheCounter_IsNotKilledAndWaitsForTheNextGimmick()
        {
            BrainRig b = new BrainRig(true, false, 0f);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);

            b.RunTurn(0.25, true);
            b.RunTurn(10.25, true);
            b.RunTurn(20.25, false);
            Assert.AreEqual(EWolbaekSealState.Counter, b.Seal.State);

            // 반격 중 체력이 정확히 0이 됐다
            b.Harness.Vitals.HealthRate   = 0f;
            b.Harness.Vitals.IsHealthZero = true;
            b.Tick(30.0);

            Assert.AreEqual(EBossFlowState.Combat, b.Harness.Brain.FlowState);
            Assert.IsFalse(b.Harness.Brain.IsBossDeathRecorded);

            b.Tick(35.25);
            b.Tick(40.0);

            Assert.AreEqual(EWolbaekSealState.NextBoundaryPending, b.Seal.State);
            Assert.AreEqual(EBossFlowState.Combat, b.Harness.Brain.FlowState);
            Assert.IsTrue(b.Seal.IsDamageBlocked);

            // 다음 기믹이 이어받은 뒤에는 사망 보류가 풀린다, 보류를 이어받는 일은 다음 기믹의 몫이다
            Assert.IsTrue(b.Seal.TryTakeHandoff());
            b.Tick(41.0);
            Assert.AreEqual(EBossFlowState.Defeated, b.Harness.Brain.FlowState);
        }

        [Test]
        public void Brain_Easy_BossAtZeroDuringTheCounter_DiesByTheNormalRule()
        {
            BrainRig b = new BrainRig(false, false, 0f);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);

            b.RunTurn(0.25, true);
            b.RunTurn(10.25, true);
            b.RunTurn(20.25, false);

            b.Harness.Vitals.HealthRate   = 0f;
            b.Harness.Vitals.IsHealthZero = true;
            b.Tick(30.0);

            Assert.AreEqual(EBossFlowState.Defeated, b.Harness.Brain.FlowState);
            Assert.AreEqual(EWolbaekSealState.Stopped, b.Seal.State);
        }

        [Test]
        public void Brain_WardThenSeal_StartsOnTheSameTickTheWardEndsAndHoldsTheTransition()
        {
            BrainRig b = new BrainRig(false, true, 0.5f);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = 0.5f;
            b.Tick(0.25);
            Assert.AreEqual(EWolbaekWardState.Gather, b.Ward.State);

            b.Harness.Player.Position = Vector3.zero;
            b.Tick(1.0);
            b.Tick(9.0);

            // 경계 도달이 50% 기믹 진행 중에 일어난 상황
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            for (double time = 9.5; time < 39.0; time += 0.5)
            {
                b.Harness.Player.Position = b.Ward.Stage.GetCenter(b.Ward.Members[0].Color);
                b.Tick(time);
                Assert.AreEqual(EWolbaekSealState.Dormant, b.Seal.State);
                Assert.IsTrue(b.Seal.IsDamageBlocked);
            }

            b.Harness.Player.Position = b.Ward.Stage.GetCenter(b.Ward.Members[0].Color);
            b.Tick(39.0);

            Assert.AreEqual(EWolbaekWardState.Completed, b.Ward.State);
            Assert.AreEqual(EWolbaekSealState.Active, b.Seal.State);
            Assert.AreEqual(39.0, b.Seal.Turn.StartServerTime, 0.0001);
            Assert.AreEqual(1, b.Listener.Count("start"));
            Assert.AreEqual(1, b.Harness.Brain.Phase);
        }
    }
}
