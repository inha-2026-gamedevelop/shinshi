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
    // 시각은 이진 분수로 넘겨 경계 판정에 부동소수 오차가 끼지 않게 한다.
    // 기본 시간표: 진입 t=0, 집결 시작 t=1(첫 틱), 집결 완료 t=9, 첫 지점 t=9에 시작해 광역 공격 16, 17.5, 19,
    // 두 번째 지점 19에서 26, 27.5, 29, 세 번째 지점 29에서 36, 37.5, 39에 성공한다. 예고는 판정 1.5초 전이다.
    // 후보는 전장 중심에서 11m 떨어진 8방위이고, 인접 후보 사이는 약 8.4m, 중앙에서 15m 이내라 조건을 만족하는 짝이 있다
    public class WolbaekColorWardTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TRIGGER_RATE   = 0.5f;
        private const float  ARENA_RADIUS   = 18f;
        private const float  MAX_HEALTH     = 1000f;
        private const int    FIRST_PLAYER   = 10;
        private const float  SITE_RING      = 11f;
        private const int    SITE_COUNT     = 8;
        private const string LINK_ID        = "wolbaek_ward_link";
        private const string STRIKE_ID      = "wolbaek_ward_strike";

    // 중앙 근처, 어느 후보 결계와도 9m 이상 떨어져 있어 어떤 색 결계 밖이다
    private static readonly Vector3 OUTSIDE_SPOT = new Vector3(0f, 0f, 2f);

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
            public List<Participant> Members { get; } = new List<Participant>();
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

        private sealed class FakeSites : IWolbaekWardSiteSource
        {
            public List<Vector3> Centers { get; } = new List<Vector3>();

            public FakeSites(bool ring)
            {
                if (!ring)
                {
                    return;
                }

                for (int i = 0; i < SITE_COUNT; ++i)
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

        private sealed class StillBoss : ICombatTarget, IBossMovement
        {
            public int                   CombatId      => BossHarness.SELF_ID;
            public ECombatFaction        Faction       => ECombatFaction.Enemy;
            public EParticipantLifeState LifeState     => EParticipantLifeState.Alive;
            public bool                  IsSummon      => false;
            public int                   OwnerCombatId => CombatIds.NONE;
            public Vector3               Position      { get; set; }
            public Vector3               Forward       => Vector3.forward;
            public float                 BodyRadius    => 2f;

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                Position = to;
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

        private sealed class Recorder : IWolbaekColorWardListener
        {
            public List<string>            Events      { get; } = new List<string>();
            public List<WolbaekWardStage>  Stages      { get; } = new List<WolbaekWardStage>();
            public List<WolbaekWardStrike> Telegraphs  { get; } = new List<WolbaekWardStrike>();
            public List<int>               Hits        { get; } = new List<int>();
            public List<int>               Protected   { get; } = new List<int>();
            public List<float>             LinkRates   { get; } = new List<float>();
            public WolbaekWardStart        Start       { get; private set; }

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

            public void OnWardStarted(in WolbaekWardStart start)
            {
                Start = start;
                Events.Add("start");
            }

            public void OnGatherHoldStarted(int castInstanceId, double serverTime)
            {
                Events.Add("holdStart@" + serverTime);
            }

            public void OnGatherHoldReset(int castInstanceId, double serverTime, EWolbaekHoldReset reason, int combatId)
            {
                Events.Add("reset:" + reason + ":" + combatId);
            }

            public void OnGatherCompleted(int castInstanceId, double serverTime)
            {
                Events.Add("gathered@" + serverTime);
            }

            public void OnStageStarted(int castInstanceId, in WolbaekWardStage stage)
            {
                Stages.Add(stage);
                Events.Add("stage:" + stage.Index);
            }

            public void OnStrikeTelegraphed(int castInstanceId, in WolbaekWardStrike strike)
            {
                Telegraphs.Add(strike);
                Events.Add("telegraph@" + strike.TelegraphServerTime);
            }

            public void OnStrikeResolved(int castInstanceId, int stageIndex, int strikeIndex, double strikeServerTime, int hitCount,
                int protectedCount)
            {
                Hits.Add(hitCount);
                Protected.Add(protectedCount);
                Events.Add("strike:" + stageIndex + "/" + strikeIndex + "@" + strikeServerTime);
            }

            public void OnLinkDamaged(int castInstanceId, double serverTime, int combatIdA, int combatIdB, float distance, float rate)
            {
                LinkRates.Add(rate);
                Events.Add("link@" + serverTime);
            }

            public void OnGraceStarted(int castInstanceId, int combatId, double graceEndServerTime)
            {
                Events.Add("graceStart:" + combatId + "/" + graceEndServerTime);
            }

            public void OnGraceEnded(int castInstanceId, double serverTime, int combatId, EWolbaekGraceEnd reason)
            {
                Events.Add("graceEnd:" + combatId + ":" + reason);
            }

            public void OnReviveWindowOpened(int castInstanceId, double serverTime)
            {
                Events.Add("reviveWindow@" + serverTime);
            }

            public void OnWardSucceeded(int castInstanceId, double serverTime)
            {
                Events.Add("success@" + serverTime);
            }

            public void OnWardFailed(int castInstanceId, double serverTime, EWolbaekWardFail reason)
            {
                Events.Add("fail:" + reason);
            }

            public void OnWardHalted(int castInstanceId, double serverTime, EWolbaekWardHaltReason reason)
            {
                Events.Add("halt:" + reason);
            }

            public void OnWardCleared(int castInstanceId, EBossInterruptReason reason)
            {
                Events.Add("cleared:" + reason);
            }
        }

        private static WolbaekColorWardSpec MakeSpec()
        {
            return new WolbaekColorWardSpec(TRIGGER_RATE, 5f, 8f, 3f, 3, 7f, 1.5f, 3, 1.5f, 0.3f, 1f, 15f, 8f, 3f, 4f, 1f,
                new[] { 5f, 6f }, new[] { 0.05f, 0.1f, 0.2f }, 8f);
        }

        private sealed class Rig
        {
            public StillBoss          Boss      { get; } = new StillBoss();
            public FakeRoster         Roster    { get; } = new FakeRoster();
            public FakeArena          Arena     { get; } = new FakeArena();
            public FakeSites          Sites     { get; }
            public PartyQuery         Query     { get; } = new PartyQuery();
            public FakeDamageResolver Damage    { get; } = new FakeDamageResolver();
            public FakeBossVitals     Vitals    { get; } = new FakeBossVitals();
            public FakeDisplacer      Displacer { get; } = new FakeDisplacer();
            public Recorder           Listener  { get; } = new Recorder();
            public WolbaekColorWard   Gimmick   { get; }

            public Rig(int partyCount, bool ring = true, IBossGimmick preceding = null, IBossRandom random = null)
            {
                Sites = new FakeSites(ring);
                for (int i = 0; i < partyCount; ++i)
                {
                    FakeCombatTarget target = new FakeCombatTarget { CombatId = FIRST_PLAYER + i, Faction = ECombatFaction.Player };
                    Roster.Members.Add(new Participant { Target = target });
                    Query.Players.Add(target);
                }

                Gimmick = new WolbaekColorWard(MakeSpec(), Boss, Vitals, Roster, Roster, Arena, Sites, Damage, Query,
                    random ?? new ScriptedRandom(), Displacer, preceding, Listener);
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

            public void Enter(double serverTime = 0.0)
            {
                Vitals.HealthRate = TRIGGER_RATE;
                Gimmick.Tick(serverTime, 1);
            }

            public void Tick(double serverTime)
            {
                Gimmick.Tick(serverTime, 1);
            }

            public void PlaceAll(Vector3 position)
            {
                for (int i = 0; i < Roster.Members.Count; ++i)
                {
                    Roster.Members[i].Target.Position = position;
                }
            }

            // 진입 뒤 모두 중앙에 세우고 집결을 채워 첫 지점(t=9)까지 간다
            public void GatherAndStart()
            {
                Enter();
                PlaceAll(Vector3.zero);
                Tick(1.0);
                Tick(9.0);
            }

            public Vector3 WardOf(int combatId)
            {
                Gimmick.TryGetColor(combatId, out EWolbaekWardColor color);
                return Gimmick.Stage.GetCenter(color);
            }

            public void PlaceAtOwnWards()
            {
                for (int i = 0; i < Roster.Members.Count; ++i)
                {
                    FakeCombatTarget target = Roster.Members[i].Target;
                    target.Position = WardOf(target.CombatId);
                }
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

            // 4명일 때 같은 색 두 명의 전투 번호
            public void Pair(EWolbaekWardColor color, out int first, out int second)
            {
                first  = CombatIds.NONE;
                second = CombatIds.NONE;
                IReadOnlyList<WolbaekWardMember> members = Gimmick.Members;
                for (int i = 0; i < members.Count; ++i)
                {
                    if (members[i].Color != color)
                    {
                        continue;
                    }

                    if (first == CombatIds.NONE)
                    {
                        first = members[i].CombatId;
                    }
                    else
                    {
                        second = members[i].CombatId;
                    }
                }
            }
        }

        private static int CountColor(WolbaekColorWard gimmick, EWolbaekWardColor color)
        {
            int total = 0;
            for (int i = 0; i < gimmick.Members.Count; ++i)
            {
                if (gimmick.Members[i].Color == color)
                {
                    ++total;
                }
            }

            return total;
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

            Assert.AreEqual(EWolbaekWardState.Dormant, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.IsFalse(r.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(r.Gimmick.BlocksSelection);
            Assert.IsFalse(r.Gimmick.ConsumeCleanupRequest());
        }

        [Test]
        public void CapDamage_BeforeTrigger_StopsExactlyAtBoundaryAndDiscardsExcess()
        {
            Rig r = new Rig(2);

            Assert.AreEqual(100f, r.Gimmick.CapDamage(600f, 1000f, 500f), 0.0001f);
            Assert.AreEqual(30f, r.Gimmick.CapDamage(600f, 1000f, 30f), 0.0001f);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(500f, 1000f, 50f), 0.0001f);
        }

        [Test]
        public void Tick_AtTrigger_EntersGatherAndRequestsCleanupOnce()
        {
            Rig r = new Rig(2);

            r.Enter();

            Assert.AreEqual(EWolbaekWardState.Gather, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.ConsumeCleanupRequest());
            Assert.IsFalse(r.Gimmick.ConsumeCleanupRequest());
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(500f, 1000f, 50f), 0.0001f);
            Assert.AreEqual("start", r.Listener.Events[0]);
        }

        [Test]
        public void Tick_AfterEntry_NeverStartsTwice()
        {
            Rig r = new Rig(1);
            r.Enter();
            r.PlaceAll(Vector3.zero);
            r.Tick(1.0);
            r.Tick(9.0);

            r.Tick(10.0);
            r.Vitals.HealthRate = TRIGGER_RATE;
            r.Tick(11.0);

            Assert.AreEqual(1, r.Listener.Count("start"));
        }

        [Test]
        public void Tick_SecondPhase_NeverStartsAndReleasesBoundary()
        {
            Rig r = new Rig(2);
            r.Vitals.HealthRate = TRIGGER_RATE;

            r.Gimmick.Tick(0.0, 2);

            Assert.AreEqual(EWolbaekWardState.Completed, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.AreEqual(25f, r.Gimmick.CapDamage(100f, 100f, 25f), 0.0001f);
        }

        /****************************************
        *       Preceding Gimmick
        ****************************************/

        [Test]
        public void Tick_PrecedingStillDeferring_WaitsInvulnerableThenStartsOnce()
        {
            FakePreceding preceding = new FakePreceding { Defers = true };
            Rig r = new Rig(2, true, preceding);

            r.Enter();
            r.Tick(1.0);

            Assert.AreEqual(EWolbaekWardState.Dormant, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);
            Assert.IsTrue(r.Gimmick.BlocksSelection);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.AreEqual(0, r.Listener.Events.Count);

            preceding.Defers = false;
            r.Tick(2.0);
            r.Tick(3.0);

            Assert.AreEqual(EWolbaekWardState.Gather, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Count("start"));
        }

        /****************************************
        *       Colors And Roster
        ****************************************/

        [Test]
        public void Entry_FourMembers_SplitsTwoAndTwo()
        {
            Rig r = new Rig(4);

            r.Enter();

            Assert.AreEqual(2, CountColor(r.Gimmick, EWolbaekWardColor.Red));
            Assert.AreEqual(2, CountColor(r.Gimmick, EWolbaekWardColor.Blue));
        }

        [Test]
        public void Entry_TwoMembers_ShareOneColorAndEitherColorOccurs()
        {
            bool sawRed = false;
            bool sawBlue = false;
            for (int seed = 0; seed < 40; ++seed)
            {
                Rig r = new Rig(2, true, null, new SeededRandom(seed));
                r.Enter();

                int red = CountColor(r.Gimmick, EWolbaekWardColor.Red);
                int blue = CountColor(r.Gimmick, EWolbaekWardColor.Blue);
                Assert.AreEqual(2, Mathf.Max(red, blue), "seed " + seed);
                Assert.AreEqual(0, Mathf.Min(red, blue), "seed " + seed);
                sawRed |= red == 2;
                sawBlue |= blue == 2;
            }

            Assert.IsTrue(sawRed && sawBlue);
        }

        [Test]
        public void Entry_ThreeMembers_SplitsTwoAndOne()
        {
            for (int seed = 0; seed < 20; ++seed)
            {
                Rig r = new Rig(3, true, null, new SeededRandom(seed));
                r.Enter();

                int red = CountColor(r.Gimmick, EWolbaekWardColor.Red);
                int blue = CountColor(r.Gimmick, EWolbaekWardColor.Blue);
                Assert.AreEqual(3, red + blue);
                Assert.AreEqual(1, Mathf.Min(red, blue), "seed " + seed);
            }
        }

        [Test]
        public void Entry_OneMember_GetsASingleColorWithoutLink()
        {
            Rig r = new Rig(1);

            r.Enter();

            Assert.AreEqual(1, r.Gimmick.RequiredCount);
            Assert.AreEqual((int)r.Gimmick.Members[0].Color, r.Gimmick.Members[0].SymbolId);
        }

        [Test]
        public void Entry_MoreThanFour_KeepsFirstFour()
        {
            Rig r = new Rig(5);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("상한"));
            r.Enter();

            Assert.AreEqual(4, r.Gimmick.RequiredCount);
        }

        [Test]
        public void Entry_IncludesDeadMembersAndKeepsColorsAfterRevive()
        {
            Rig r = new Rig(4);
            r.Roster.Members[2].Target.LifeState = EParticipantLifeState.Dead;

            r.Enter();
            Dictionary<int, EWolbaekWardColor> before = new Dictionary<int, EWolbaekWardColor>();
            for (int i = 0; i < r.Gimmick.Members.Count; ++i)
            {
                before[r.Gimmick.Members[i].CombatId] = r.Gimmick.Members[i].Color;
            }

            Assert.AreEqual(4, r.Gimmick.RequiredCount);
            Assert.IsTrue(before.ContainsKey(FIRST_PLAYER + 2));

            r.Roster.Members[2].Target.LifeState = EParticipantLifeState.Alive;
            r.Roster.Members[1].Connected = false;
            r.Tick(1.0);
            r.Roster.Members[1].Connected = true;
            r.Tick(2.0);

            foreach (KeyValuePair<int, EWolbaekWardColor> pair in before)
            {
                Assert.IsTrue(r.Gimmick.TryGetColor(pair.Key, out EWolbaekWardColor now));
                Assert.AreEqual(pair.Value, now);
            }
        }

        /****************************************
        *            Gather
        ****************************************/

        [Test]
        public void Gather_AllInside_CompletesAfterEightContinuousSeconds()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.PlaceAll(new Vector3(4f, 0f, 0f));

            r.Tick(1.0);
            r.Tick(8.75);
            Assert.AreEqual(EWolbaekWardState.Gather, r.Gimmick.State);

            r.Tick(9.0);
            Assert.AreEqual(EWolbaekWardState.Search, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Stages.Count);
            Assert.AreEqual(9.0, r.Listener.Stages[0].StartServerTime);
            Assert.AreEqual(16.0, r.Listener.Stages[0].FirstStrikeServerTime);
        }

        [Test]
        public void Gather_OutsideRadius_DoesNotStartHold()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.PlaceAll(new Vector3(4f, 0f, 0f));
            r.Roster.Members[1].Target.Position = new Vector3(5.5f, 0f, 0f);

            r.Tick(1.0);

            Assert.IsFalse(r.Gimmick.HasHold);
        }

        [Test]
        public void Gather_MemberLeaves_ResetsProgress()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.PlaceAll(Vector3.zero);
            r.Tick(1.0);
            r.Tick(5.0);

            r.Roster.Members[1].Target.Position = new Vector3(6f, 0f, 0f);
            r.Tick(5.25);
            Assert.IsFalse(r.Gimmick.HasHold);
            CollectionAssert.Contains(r.Listener.Events, "reset:Left:" + (FIRST_PLAYER + 1));

            r.Roster.Members[1].Target.Position = Vector3.zero;
            r.Tick(6.0);
            r.Tick(13.75);
            Assert.AreEqual(EWolbaekWardState.Gather, r.Gimmick.State);
            r.Tick(14.0);
            Assert.AreEqual(EWolbaekWardState.Search, r.Gimmick.State);
        }

        [Test]
        public void Gather_MemberDisconnects_ResetsProgress()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.PlaceAll(Vector3.zero);
            r.Tick(1.0);

            r.Roster.Members[0].Connected = false;
            r.Tick(2.0);

            Assert.IsFalse(r.Gimmick.HasHold);
            CollectionAssert.Contains(r.Listener.Events, "reset:Disconnected:" + FIRST_PLAYER);
        }

        [Test]
        public void Gather_DeathOnTheCompletionTick_WinsOverCompletion()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.PlaceAll(Vector3.zero);
            r.Tick(1.0);

            r.Roster.Members[1].Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(9.0);

            Assert.AreEqual(EWolbaekWardState.Gather, r.Gimmick.State);
            Assert.IsFalse(r.Gimmick.HasHold);
            CollectionAssert.Contains(r.Listener.Events, "reset:Died:" + (FIRST_PLAYER + 1));
        }

        [Test]
        public void Gather_RevivableDeadMember_BlocksUntilRevivedAndBack()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.PlaceAll(Vector3.zero);
            r.Roster.Members[1].Target.LifeState = EParticipantLifeState.Dead;

            r.Tick(1.0);
            r.Tick(20.0);
            Assert.AreEqual(EWolbaekWardState.Gather, r.Gimmick.State);

            r.Roster.Members[1].Target.LifeState = EParticipantLifeState.Alive;
            r.Tick(21.0);
            r.Tick(29.0);
            Assert.AreEqual(EWolbaekWardState.Search, r.Gimmick.State);
        }

        [Test]
        public void Gather_UnrevivableDeadMember_IsNotWaitedFor()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.PlaceAll(Vector3.zero);
            r.Roster.Members[1].Target.LifeState = EParticipantLifeState.Dead;
            r.Roster.Members[1].Revivable = false;

            r.Tick(1.0);
            r.Tick(9.0);

            Assert.AreEqual(EWolbaekWardState.Search, r.Gimmick.State);
        }

        [Test]
        public void Gather_EveryoneDead_FailsAsWipeAndStopsDamage()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.Roster.Members[0].Target.LifeState = EParticipantLifeState.Dead;
            r.Roster.Members[1].Target.LifeState = EParticipantLifeState.Dead;

            r.Tick(1.0);

            Assert.AreEqual(EWolbaekWardState.Failed, r.Gimmick.State);
            Assert.AreEqual(EWolbaekWardFail.Wipe, r.Gimmick.FailReason);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);
        }

        /****************************************
        *            Strikes
        ****************************************/

        [Test]
        public void Stage_Timeline_TelegraphsLastOneAndHalfSecondsThenThreeStrikes()
        {
            Rig r = new Rig(2);
            r.GatherAndStart();

            r.PlaceAtOwnWards();
            r.Tick(14.25);
            Assert.AreEqual(0, r.Listener.Telegraphs.Count);

            r.Tick(14.5);
            Assert.AreEqual(1, r.Listener.Telegraphs.Count);
            Assert.AreEqual(14.5, r.Listener.Telegraphs[0].TelegraphServerTime);
            Assert.AreEqual(16.0, r.Listener.Telegraphs[0].StrikeServerTime);

            r.Tick(16.0);
            Assert.AreEqual(1, r.Listener.Hits.Count);
            Assert.AreEqual(EWolbaekWardState.Strike, r.Gimmick.State);

            r.Tick(17.0);
            Assert.AreEqual(1, r.Listener.Hits.Count);
            r.Tick(17.5);
            Assert.AreEqual(2, r.Listener.Hits.Count);
            r.Tick(19.0);
            Assert.AreEqual(3, r.Listener.Hits.Count);
            Assert.AreEqual(EWolbaekWardState.Search, r.Gimmick.State);
            Assert.AreEqual(1, r.Gimmick.StageIndex);
            Assert.AreEqual(19.0, r.Gimmick.Stage.StartServerTime);
        }

        [Test]
        public void Strike_CorrectColorWard_BlocksDamageAndPush()
        {
            Rig r = new Rig(4);
            r.GatherAndStart();
            r.PlaceAtOwnWards();

            r.Tick(16.0);

            Assert.AreEqual(0, r.Requests(STRIKE_ID).Count);
            Assert.AreEqual(0, r.Displacer.Pushes.Count);
            Assert.AreEqual(4, r.Listener.Protected[0]);
            Assert.AreEqual(0, r.Listener.Hits[0]);
        }

        [Test]
        public void Strike_OutsideWard_HitsForThirtyPercentUnblockableAndPushesOutward()
        {
            Rig r = new Rig(1);
            r.GatherAndStart();
            r.Roster.Members[0].Target.Position = OUTSIDE_SPOT;

            r.Tick(16.0);

            List<DamageRequest> hits = r.Requests(STRIKE_ID);
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(300f, hits[0].RawDamage, 0.0001f);
            Assert.IsTrue(hits[0].IsUnblockable);
            Assert.AreEqual(1, r.Displacer.Pushes.Count);
            Assert.AreEqual(1f, r.Displacer.Pushes[0].Distance, 0.0001f);
            Assert.AreEqual(1f, Vector3.Dot(r.Displacer.Pushes[0].Direction, Vector3.forward), 0.0001f);
        }

        [Test]
        public void Strike_OtherColorWard_DoesNotProtect()
        {
            Rig r = new Rig(2);
            r.GatherAndStart();
            r.PlaceAtOwnWards();
            int id = FIRST_PLAYER;
            r.Gimmick.TryGetColor(id, out EWolbaekWardColor color);
            EWolbaekWardColor other = (color == EWolbaekWardColor.Red) ? EWolbaekWardColor.Blue : EWolbaekWardColor.Red;
            r.Member(id).Target.Position = r.Gimmick.Stage.GetCenter(other);

            r.Tick(16.0);

            List<DamageRequest> hits = r.Requests(STRIKE_ID);
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(id, hits[0].HitTargetId);
        }

        [Test]
        public void Strike_ExactlyAtWardRadius_StillProtected()
        {
            Rig r = new Rig(1);
            r.GatherAndStart();
            r.Roster.Members[0].Target.Position = r.WardOf(FIRST_PLAYER) + new Vector3(3f, 0f, 0f);

            r.Tick(16.0);

            Assert.AreEqual(0, r.Requests(STRIKE_ID).Count);
        }

        [Test]
        public void Strike_DeadPlayerIsNotHit()
        {
            Rig r = new Rig(2);
            r.GatherAndStart();
            r.Roster.Members[1].Target.LifeState = EParticipantLifeState.Dead;
            r.Roster.Members[0].Target.Position = OUTSIDE_SPOT;

            r.Tick(16.0);

            List<DamageRequest> hits = r.Requests(STRIKE_ID);
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(FIRST_PLAYER, hits[0].HitTargetId);
        }

        [Test]
        public void Strike_Damage_IsPlainRatioOfMaxHealthWithoutPartyScale()
        {
            Rig r = new Rig(1);
            r.Roster.MaxHealth = 5000f;
            r.GatherAndStart();
            r.Roster.Members[0].Target.Position = OUTSIDE_SPOT;

            r.Tick(16.0);

            Assert.AreEqual(1500f, r.Requests(STRIKE_ID)[0].RawDamage, 0.0001f);
        }

        [Test]
        public void Link_Damage_IsPlainRatioOfMaxHealthWithoutPartyScale()
        {
            Rig r = new Rig(4);
            r.Roster.MaxHealth = 5000f;
            StartApart(r, 5.5f, out int a, out int b);

            r.Tick(10.0);
            r.Tick(11.0);

            Assert.AreEqual(500f, r.Requests(LINK_ID)[0].RawDamage, 0.0001f);
        }

        [Test]
        public void Gather_NoArrivalDeadline_StillCompletesLongAfterEntry()
        {
            Rig r = new Rig(2);
            r.Enter();
            r.PlaceAll(new Vector3(15f, 0f, 0f));

            r.Tick(1.0);
            r.Tick(60.0);
            Assert.AreEqual(EWolbaekWardState.Gather, r.Gimmick.State);
            Assert.AreEqual(0, r.Listener.Count("fail"));

            r.PlaceAll(Vector3.zero);
            r.Tick(61.0);
            r.Tick(69.0);

            Assert.AreEqual(EWolbaekWardState.Search, r.Gimmick.State);
        }

        [Test]
        public void Strike_SharedActionNumberPerStrike()
        {
            Rig r = new Rig(2);
            r.GatherAndStart();
            r.PlaceAll(OUTSIDE_SPOT);

            r.Tick(16.0);
            r.Tick(17.5);

            List<DamageRequest> hits = r.Requests(STRIKE_ID);
            Assert.AreEqual(4, hits.Count);
            Assert.AreEqual(hits[0].ActionInstanceId, hits[1].ActionInstanceId);
            Assert.AreNotEqual(hits[0].ActionInstanceId, hits[2].ActionInstanceId);
        }

        [Test]
        public void Strike_CoarseTick_ProcessesEveryDueEventInOrder()
        {
            Rig r = new Rig(1);
            r.GatherAndStart();
            r.PlaceAtOwnWards();

            r.Tick(19.0);

            Assert.AreEqual(3, r.Listener.Hits.Count);
            Assert.AreEqual(1, r.Gimmick.StageIndex);
            Assert.AreEqual(1, r.Listener.Count("stage:1"));
        }

        /****************************************
        *         Death And Success
        ****************************************/

        [Test]
        public void Success_ThirdStageLastStrike_ClearsAndReleasesBoss()
        {
            Rig r = new Rig(2);
            r.GatherAndStart();
            r.PlaceAtOwnWards();

            r.Tick(29.0);
            r.PlaceAtOwnWards();
            r.Tick(38.0);
            Assert.AreEqual(EWolbaekWardState.Strike, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);

            r.Tick(39.0);

            Assert.AreEqual(EWolbaekWardState.Completed, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Count("success"));
            Assert.IsFalse(r.Gimmick.IsDamageBlocked);
            Assert.IsFalse(r.Gimmick.DefersPhaseTransition);
            Assert.IsFalse(r.Gimmick.BlocksSelection);
            Assert.AreEqual(25f, r.Gimmick.CapDamage(400f, 1000f, 25f), 0.0001f);

            int before = r.Damage.Requests.Count;
            r.PlaceAll(OUTSIDE_SPOT);
            r.Tick(41.0);
            r.Tick(45.0);
            Assert.AreEqual(before, r.Damage.Requests.Count);
            Assert.AreEqual(3, r.Listener.Stages.Count);
        }

        [Test]
        public void Death_DuringMove_IsNotAFailureAndSurvivorsCanFinish()
        {
            Rig r = new Rig(2);
            r.GatherAndStart();
            r.Roster.Members[1].Target.LifeState = EParticipantLifeState.Dead;
            r.Roster.Members[0].Target.Position = r.WardOf(FIRST_PLAYER);

            r.Tick(10.0);
            Assert.AreEqual(EWolbaekWardState.Search, r.Gimmick.State);

            for (double time = 16.0; time <= 39.0; time += 0.5)
            {
                r.Roster.Members[0].Target.Position = r.WardOf(FIRST_PLAYER);
                r.Tick(time);
            }

            Assert.AreEqual(EWolbaekWardState.Completed, r.Gimmick.State);
        }

        [Test]
        public void Death_OfEveryone_FailsAsWipe()
        {
            Rig r = new Rig(2);
            r.GatherAndStart();
            r.Roster.Members[0].Target.LifeState = EParticipantLifeState.Dead;
            r.Roster.Members[1].Target.LifeState = EParticipantLifeState.Dead;

            r.Tick(10.0);

            Assert.AreEqual(EWolbaekWardState.Failed, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Count("fail:Wipe"));
        }

        [Test]
        public void Death_LastSurvivorOnFinalStrikeTick_WinsOverSuccess()
        {
            Rig r = new Rig(1);
            r.GatherAndStart();
            r.PlaceAtOwnWards();
            r.Tick(29.0);
            r.PlaceAtOwnWards();
            r.Tick(38.0);

            r.Roster.Members[0].Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(39.0);

            Assert.AreEqual(EWolbaekWardState.Failed, r.Gimmick.State);
            Assert.AreEqual(0, r.Listener.Count("success"));
        }

        [Test]
        public void ReviveWindow_ClosedOnlyWhileStrikesRun()
        {
            Rig r = new Rig(1);
            r.GatherAndStart();
            r.PlaceAtOwnWards();
            Assert.IsTrue(r.Gimmick.IsReviveWindowOpen);

            r.Tick(16.0);
            Assert.IsFalse(r.Gimmick.IsReviveWindowOpen);
            r.Tick(17.5);
            Assert.IsFalse(r.Gimmick.IsReviveWindowOpen);

            r.Tick(19.0);
            Assert.IsTrue(r.Gimmick.IsReviveWindowOpen);
            CollectionAssert.Contains(r.Listener.Events, "reviveWindow@19");
        }

        [Test]
        public void ReviveWindow_FinalStrike_ReopensOnlyWhenWardEnds()
        {
            Rig r = new Rig(1);
            r.GatherAndStart();
            r.PlaceAtOwnWards();
            r.Tick(29.0);
            r.PlaceAtOwnWards();
            r.Tick(36.0);
            Assert.IsFalse(r.Gimmick.IsReviveWindowOpen);

            r.Tick(39.0);

            Assert.AreEqual(EWolbaekWardState.Completed, r.Gimmick.State);
            Assert.IsTrue(r.Gimmick.IsReviveWindowOpen);
        }

        /****************************************
        *               Link
        ****************************************/

        // 4명 중 같은 색 두 명을 첫 지점 탐색 시간에 4m보다 멀리 세운다, 나머지 둘은 제자리다
        private static void StartApart(Rig r, float apartDistance, out int a, out int b)
        {
            r.GatherAndStart();
            r.PlaceAtOwnWards();
            r.Pair(EWolbaekWardColor.Red, out a, out b);
            r.Member(b).Target.Position = r.WardOf(a) + new Vector3(apartDistance, 0f, 0f);
        }

        [Test]
        public void Link_FarApartForOnlyUnderOneSecond_NoDamage()
        {
            Rig r = new Rig(4);
            StartApart(r, 4.5f, out int a, out int b);

            r.Tick(10.0);
            r.Tick(10.75);

            Assert.AreEqual(0, r.Requests(LINK_ID).Count);
        }

        [Test]
        public void Link_FarApartOneFullSecond_DamagesBothThenEverySecond()
        {
            Rig r = new Rig(4);
            StartApart(r, 4.5f, out int a, out int b);

            r.Tick(10.0);
            r.Tick(11.0);
            List<DamageRequest> hits = r.Requests(LINK_ID);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(50f, hits[0].RawDamage, 0.0001f);
            Assert.IsTrue(hits[0].IsUnblockable);

            r.Tick(11.5);
            Assert.AreEqual(2, r.Requests(LINK_ID).Count);
            r.Tick(12.0);
            Assert.AreEqual(4, r.Requests(LINK_ID).Count);
        }

        [Test]
        public void Link_DamageTiersFollowDistance()
        {
            Rig r = new Rig(4);
            StartApart(r, 5.5f, out int a, out int b);
            r.Tick(10.0);
            r.Tick(11.0);
            Assert.AreEqual(100f, r.Requests(LINK_ID)[0].RawDamage, 0.0001f);

            r.Member(b).Target.Position = r.WardOf(a) + new Vector3(6.5f, 0f, 0f);
            r.Tick(12.0);
            Assert.AreEqual(200f, r.Requests(LINK_ID)[2].RawDamage, 0.0001f);
            CollectionAssert.AreEqual(new[] { 0.1f, 0.2f }, r.Listener.LinkRates);
        }

        [Test]
        public void Link_ExactlyFourMeters_NoDamage()
        {
            Rig r = new Rig(4);
            StartApart(r, 4f, out int a, out int b);

            r.Tick(10.0);
            r.Tick(12.0);

            Assert.AreEqual(0, r.Requests(LINK_ID).Count);
        }

        [Test]
        public void Link_ClosingBelowFour_StopsAtOnceAndRestartsTheOneSecond()
        {
            Rig r = new Rig(4);
            StartApart(r, 4.5f, out int a, out int b);
            r.Tick(10.0);
            r.Tick(11.0);
            int before = r.Requests(LINK_ID).Count;

            r.Member(b).Target.Position = r.WardOf(a);
            r.Tick(11.5);
            r.Member(b).Target.Position = r.WardOf(a) + new Vector3(4.5f, 0f, 0f);
            r.Tick(12.0);
            r.Tick(12.75);

            Assert.AreEqual(before, r.Requests(LINK_ID).Count);
            r.Tick(13.0);
            Assert.AreEqual(before + 2, r.Requests(LINK_ID).Count);
        }

        [Test]
        public void Link_PartnerDies_StopsDamageForSurvivor()
        {
            Rig r = new Rig(4);
            StartApart(r, 4.5f, out int a, out int b);
            r.Tick(10.0);

            r.Member(b).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(11.0);
            r.Tick(13.0);

            Assert.AreEqual(0, r.Requests(LINK_ID).Count);
        }

        [Test]
        public void Link_PartnerDisconnects_StopsDamage()
        {
            Rig r = new Rig(4);
            StartApart(r, 4.5f, out int a, out int b);
            r.Tick(10.0);

            r.Member(b).Connected = false;
            r.Tick(11.0);
            r.Tick(13.0);

            Assert.AreEqual(0, r.Requests(LINK_ID).Count);
        }

        [Test]
        public void Link_SingleOfAColor_HasNoLinkDamage()
        {
            Rig r = new Rig(3, true, null, new SeededRandom(3));
            r.GatherAndStart();
            r.PlaceAtOwnWards();
            r.Member(FIRST_PLAYER).Target.Position = new Vector3(-40f, 0f, 0f);

            r.Tick(10.0);
            r.Tick(13.0);

            // 같은 색이 한 명인 사람은 연결 거리 피해를 받지 않고, 두 명인 색은 서로 가까워 피해가 없다
            Assert.AreEqual(0, r.Requests(LINK_ID).Count - CountLinkHitsOnPairs(r));
        }

        private static int CountLinkHitsOnPairs(Rig r)
        {
            int total = 0;
            List<DamageRequest> hits = r.Requests(LINK_ID);
            for (int i = 0; i < hits.Count; ++i)
            {
                Assert.IsTrue(r.Gimmick.TryGetColor(hits[i].HitTargetId, out EWolbaekWardColor color));
                int same = CountColor(r.Gimmick, color);
                if (same == 2)
                {
                    ++total;
                }
            }

            return total;
        }

        /****************************************
        *               Grace
        ****************************************/

        [Test]
        public void Grace_ReturnerGetsEightSecondsWithoutLinkDamage()
        {
            Rig r = new Rig(4);
            StartApart(r, 6.5f, out int a, out int b);
            r.Member(b).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(10.0);

            r.Member(b).Target.LifeState = EParticipantLifeState.Alive;
            r.Tick(11.0);
            CollectionAssert.Contains(r.Listener.Events, "graceStart:" + b + "/19");
            Assert.IsTrue(r.Gimmick.IsInGrace(b, 11.0));

            for (double time = 12.0; time < 19.0; time += 1.0)
            {
                r.Tick(time);
            }

            Assert.AreEqual(0, r.Requests(LINK_ID).Count);
        }

        [Test]
        public void Grace_JoiningWithinFourMeters_EndsIt()
        {
            Rig r = new Rig(4);
            StartApart(r, 6.5f, out int a, out int b);
            r.Member(b).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(10.0);
            r.Member(b).Target.LifeState = EParticipantLifeState.Alive;
            r.Tick(11.0);

            r.Member(b).Target.Position = r.WardOf(a);
            r.Tick(12.0);

            CollectionAssert.Contains(r.Listener.Events, "graceEnd:" + b + ":Joined");
            Assert.IsFalse(r.Gimmick.IsInGrace(b, 12.0));
        }

        [Test]
        public void Grace_ExpiresWhileStillFar_StartsDamageForBoth()
        {
            Rig r = new Rig(4);
            StartApart(r, 6.5f, out int a, out int b);
            r.Member(b).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(10.0);
            r.Member(b).Target.LifeState = EParticipantLifeState.Alive;
            r.Tick(11.0);

            for (double time = 12.0; time <= 18.0; time += 1.0)
            {
                r.Tick(time);
            }

            Assert.AreEqual(0, r.Requests(LINK_ID).Count);
            r.Tick(19.0);

            CollectionAssert.Contains(r.Listener.Events, "graceEnd:" + b + ":Expired");
            Assert.AreEqual(2, r.Requests(LINK_ID).Count);
        }

        [Test]
        public void Grace_ReconnectCountsAsAReturnerToo()
        {
            Rig r = new Rig(4);
            StartApart(r, 6.5f, out int a, out int b);
            r.Member(b).Connected = false;
            r.Tick(10.0);

            r.Member(b).Connected = true;
            r.Tick(11.0);

            CollectionAssert.Contains(r.Listener.Events, "graceStart:" + b + "/19");
        }

        [Test]
        public void Grace_DoesNotProtectFromStrikes()
        {
            Rig r = new Rig(4);
            StartApart(r, 6.5f, out int a, out int b);
            r.Member(b).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(10.0);
            r.Member(b).Target.LifeState = EParticipantLifeState.Alive;
            r.Member(b).Target.Position = OUTSIDE_SPOT;
            r.Tick(11.0);

            r.Tick(16.0);

            bool hit = false;
            List<DamageRequest> hits = r.Requests(STRIKE_ID);
            for (int i = 0; i < hits.Count; ++i)
            {
                hit |= hits[i].HitTargetId == b;
            }

            Assert.IsTrue(hit);
        }

        /****************************************
        *           Sites And Halts
        ****************************************/

        [Test]
        public void Sites_EveryPlanKeepsTravelAndSeparationLimits()
        {
            for (int seed = 0; seed < 40; ++seed)
            {
                Rig r = new Rig(1, true, null, new SeededRandom(seed));
                r.Enter();
                r.PlaceAll(Vector3.zero);
                r.Tick(1.0);
                r.Tick(9.0);

                Vector3 previousRed  = Vector3.zero;
                Vector3 previousBlue = Vector3.zero;
                for (int stage = 0; stage < 3; ++stage)
                {
                    WolbaekWardStage current = r.Gimmick.Stage;
                    Assert.AreEqual(stage, current.Index);
                    Assert.LessOrEqual(Vector3.Distance(previousRed, current.RedCenter), 15.01f, "seed " + seed);
                    Assert.LessOrEqual(Vector3.Distance(previousBlue, current.BlueCenter), 15.01f, "seed " + seed);
                    Assert.GreaterOrEqual(Vector3.Distance(current.RedCenter, current.BlueCenter), 7.99f, "seed " + seed);
                    Assert.Greater(Vector3.Distance(previousRed, current.RedCenter), 0.01f);
                    Assert.Greater(Vector3.Distance(previousBlue, current.BlueCenter), 0.01f);

                    previousRed  = current.RedCenter;
                    previousBlue = current.BlueCenter;
                    r.Roster.Members[0].Target.Position = r.WardOf(FIRST_PLAYER);
                    r.Tick(current.LastStrikeServerTime);
                }
            }
        }

        [Test]
        public void Sites_NoCandidates_HaltsWithoutStartingOrFailing()
        {
            Rig r = new Rig(2, false);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("결계 후보"));
            r.Enter();

            Assert.AreEqual(EWolbaekWardState.Halted, r.Gimmick.State);
            Assert.AreEqual(EWolbaekWardHaltReason.SitesInsufficient, r.Gimmick.HaltReason);
            Assert.AreEqual(0, r.Listener.Count("start"));
            Assert.AreEqual(1, r.Listener.Count("halt:SitesInsufficient"));
            Assert.IsTrue(r.Gimmick.IsDamageBlocked);
            Assert.IsTrue(r.Gimmick.DefersPhaseTransition);
            Assert.IsTrue(r.Gimmick.ConsumeCleanupRequest());
        }

        [Test]
        public void Sites_OnlyUnreachableCandidates_Halts()
        {
            Rig r = new Rig(2, false);
            r.Sites.Centers.Add(new Vector3(16f, 0f, 0f));
            r.Sites.Centers.Add(new Vector3(-16f, 0f, 0f));

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("결계 후보"));
            r.Enter();

            Assert.AreEqual(EWolbaekWardState.Halted, r.Gimmick.State);
        }

        [Test]
        public void Sites_CandidatesOutsideTheArenaAreIgnored()
        {
            Rig r = new Rig(2, false);
            r.Sites.Centers.Add(new Vector3(30f, 0f, 0f));
            r.Sites.Centers.Add(new Vector3(0f, 0f, 30f));

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("결계 후보"));
            r.Enter();

            Assert.AreEqual(EWolbaekWardState.Halted, r.Gimmick.State);
        }

        [Test]
        public void Halt_ArenaUnavailable_HaltsAndKeepsBossInvulnerable()
        {
            Rig r = new Rig(2);
            r.Arena.Available = false;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("전장 정보"));
            r.Enter();

            Assert.AreEqual(EWolbaekWardState.Halted, r.Gimmick.State);
            Assert.AreEqual(EWolbaekWardHaltReason.ArenaUnavailable, r.Gimmick.HaltReason);
            Assert.AreEqual(0f, r.Gimmick.CapDamage(500f, 1000f, 50f), 0.0001f);
        }

        /****************************************
        *         Stop And Snapshot
        ****************************************/

        [Test]
        public void Stop_DuringStage_ClearsDisplayOnceAndClosesDamage()
        {
            Rig r = new Rig(1);
            r.GatherAndStart();
            r.Roster.Members[0].Target.Position = OUTSIDE_SPOT;

            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);
            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);
            r.Tick(16.0);

            Assert.AreEqual(EWolbaekWardState.Stopped, r.Gimmick.State);
            Assert.AreEqual(1, r.Listener.Count("cleared:CombatEnd"));
            Assert.AreEqual(0, r.Damage.Requests.Count);
        }

        [Test]
        public void Stop_BeforeEntry_AlsoStopsDormantWithoutNotice()
        {
            Rig r = new Rig(1);

            r.Gimmick.Stop(EBossInterruptReason.Death);

            Assert.AreEqual(EWolbaekWardState.Stopped, r.Gimmick.State);
            Assert.AreEqual(0, r.Listener.Events.Count);
        }

        [Test]
        public void Snapshot_OnlyWhileRunning_CarriesStageAndGrace()
        {
            Rig r = new Rig(4);
            Assert.IsFalse(r.Gimmick.TryGetSnapshot(out WolbaekWardSnapshot none));

            r.Enter();
            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekWardSnapshot gather));
            Assert.AreEqual(EWolbaekWardState.Gather, gather.State);
            Assert.AreEqual(4, gather.Start.Members.Count);

            StartApart(r, 6.5f, out int a, out int b);
            r.Member(b).Target.LifeState = EParticipantLifeState.Dead;
            r.Tick(10.0);
            r.Member(b).Target.LifeState = EParticipantLifeState.Alive;
            r.Tick(11.0);

            Assert.IsTrue(r.Gimmick.TryGetSnapshot(out WolbaekWardSnapshot stage));
            Assert.AreEqual(EWolbaekWardState.Search, stage.State);
            Assert.AreEqual(0, stage.Stage.Index);
            Assert.AreEqual(0, stage.NextStrikeIndex);
            Assert.Contains(19d, new List<double>(stage.GraceEndServerTimes));

            r.Gimmick.Stop(EBossInterruptReason.CombatEnd);
            Assert.IsFalse(r.Gimmick.TryGetSnapshot(out WolbaekWardSnapshot stopped));
        }

        /****************************************
        *        Cap Composite And Spec
        ****************************************/

        [Test]
        public void Composite_BlocksIfAnyBlocksAndCapsAtLowestBoundary()
        {
            Rig r = new Rig(1);
            BossDamageCapComposite composite = new BossDamageCapComposite(r.Gimmick, new FixedCap { Boundary = 0.55f });

            Assert.IsFalse(composite.IsDamageBlocked);
            Assert.AreEqual(50f, composite.CapDamage(600f, 1000f, 500f), 0.0001f);
            Assert.AreEqual(0f, composite.CapDamage(500f, 1000f, 50f), 0.0001f);

            r.Enter();
            Assert.IsTrue(composite.IsDamageBlocked);
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

        [Test]
        public void Spec_LinkRateBoundariesBelongToTheLowerTier()
        {
            WolbaekColorWardSpec spec = MakeSpec();

            Assert.AreEqual(0f, spec.GetLinkDamageRate(4f));
            Assert.AreEqual(0.05f, spec.GetLinkDamageRate(4.01f));
            Assert.AreEqual(0.05f, spec.GetLinkDamageRate(5f));
            Assert.AreEqual(0.1f, spec.GetLinkDamageRate(5.01f));
            Assert.AreEqual(0.1f, spec.GetLinkDamageRate(6f));
            Assert.AreEqual(0.2f, spec.GetLinkDamageRate(6.01f));
        }

        [Test]
        public void Spec_InvalidValues_Throw()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekColorWardSpec(0f, 5f, 8f, 3f, 3, 7f, 1.5f, 3, 1.5f,
                0.3f, 1f, 15f, 8f, 3f, 4f, 1f, new[] { 5f }, new[] { 0.05f, 0.1f }, 8f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WolbaekColorWardSpec(0.5f, 5f, 8f, 3f, 3, 1f, 1.5f, 3, 1.5f,
                0.3f, 1f, 15f, 8f, 3f, 4f, 1f, new[] { 5f }, new[] { 0.05f, 0.1f }, 8f));
            Assert.Throws<System.ArgumentException>(() => new WolbaekColorWardSpec(0.5f, 5f, 8f, 3f, 3, 7f, 1.5f, 3, 1.5f,
                0.3f, 1f, 15f, 8f, 3f, 4f, 1f, new[] { 5f }, new[] { 0.05f }, 8f));
        }

        /****************************************
        *         Brain Integration
        ****************************************/

        private sealed class BrainRig
        {
            public BossHarness      Harness  { get; }
            public WolbaekColorWard Ward     { get; }
            public WolbaekTalismanFormation Talisman { get; }
            public Recorder         Listener { get; } = new Recorder();
            public StillBoss        Boss     { get; } = new StillBoss();

            public BrainRig(bool withTalisman)
            {
                Harness = new BossHarness(BossTestFixtures.MakeBoss(), null, BossTestFixtures.MakePattern("p1"));
                Harness.Factory.HitOffsets = new[] { 0.0 };

                FakeRoster roster = new FakeRoster();
                roster.Members.Add(new Participant { Target = Harness.Player });
                PartyQuery query = new PartyQuery();
                query.Players.Add(Harness.Player);

                if (withTalisman)
                {
                    Talisman = new WolbaekTalismanFormation(WolbaekTalismanDevCatalog.CreateSpec(false, TRIGGER_RATE), Boss, 3541f,
                        Harness.Vitals, roster, new FakeArena(), Boss, Harness.Damage, query, new ScriptedRandom());
                    Assert.IsTrue(Harness.Brain.AddGimmick(Talisman));
                }

                Ward = new WolbaekColorWard(MakeSpec(), Boss, Harness.Vitals, roster, roster, new FakeArena(), new FakeSites(true),
                    Harness.Damage, query, new ScriptedRandom(), new FakeDisplacer(), Talisman, Listener);
                Assert.IsTrue(Harness.Brain.AddGimmick(Ward));
                Assert.IsTrue(Harness.Brain.Begin(0.0));
            }

            public void Tick(double serverTime)
            {
                Harness.Brain.Tick(serverTime);
            }
        }

        [Test]
        public void Brain_Entry_CancelsRunningPatternBlocksSelectionAndHoldsTransition()
        {
            BrainRig b = new BrainRig(false);
            b.Tick(0.0);
            Assert.AreEqual(EBossAttackState.Telegraph, b.Harness.Brain.AttackState);

            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);

            Assert.AreEqual(EWolbaekWardState.Gather, b.Ward.State);
            Assert.AreEqual(EBossAttackState.Ready, b.Harness.Brain.AttackState);
            Assert.AreEqual(EBossInterruptReason.GimmickEntry, b.Harness.Factory.Last.CancelReason);

            b.Tick(1.0);
            b.Tick(5.0);
            Assert.AreEqual(1, b.Harness.Factory.CreateCount);
            Assert.AreEqual(1, b.Harness.Brain.Phase);
        }

        [Test]
        public void Brain_Success_ReleasesTheHeldTransitionToSecondPhase()
        {
            BrainRig b = new BrainRig(false);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);
            b.Harness.Player.Position = Vector3.zero;
            b.Tick(1.0);
            b.Tick(9.0);

            for (double time = 9.5; time <= 39.0; time += 0.5)
            {
                b.Harness.Player.Position = b.Ward.Stage.GetCenter(b.Ward.Members[0].Color);
                b.Tick(time);
            }

            // 성공한 틱에 기믹이 전환 보류를 풀어, 같은 틱에 보류돼 있던 전환이 이어서 일어난다
            Assert.AreEqual(EWolbaekWardState.Completed, b.Ward.State);
            Assert.AreEqual(2, b.Harness.Brain.Phase);
            Assert.AreEqual(EBossPhaseState.AwaitingHandoff, b.Harness.Brain.PhaseState);
        }

        [Test]
        public void Brain_FiftyPercentReachedDuringTalismanCounter_ContinuesOnceAfterCounter()
        {
            BrainRig b = new BrainRig(true);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = 0.75f;
            b.Tick(0.25);
            b.Harness.Player.Position = b.Talisman.Structures[0].Center;
            b.Tick(3.25);
            b.Tick(8.25);
            Assert.AreEqual(EWolbaekTalismanState.Counter, b.Talisman.State);

            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(10.0);
            b.Tick(14.0);

            Assert.AreEqual(EWolbaekWardState.Dormant, b.Ward.State);
            Assert.AreEqual(1, b.Harness.Brain.Phase);
            Assert.AreEqual(EBossPhaseState.Active, b.Harness.Brain.PhaseState);
            Assert.IsTrue(b.Ward.IsDamageBlocked);
            Assert.AreEqual(0, b.Listener.Events.Count);

            b.Tick(14.25);

            Assert.AreEqual(EWolbaekTalismanState.Completed, b.Talisman.State);
            Assert.AreEqual(EWolbaekWardState.Gather, b.Ward.State);
            Assert.AreEqual(1, b.Harness.Brain.Phase);

            b.Tick(15.0);
            b.Tick(16.0);
            Assert.AreEqual(1, b.Listener.Count("start"));
            Assert.AreEqual(1, b.Harness.Factory.CreateCount);
        }

        [Test]
        public void Brain_BossDeathDuringStage_StopsAndClears()
        {
            BrainRig b = new BrainRig(false);
            b.Tick(0.0);
            b.Harness.Vitals.HealthRate = TRIGGER_RATE;
            b.Tick(0.25);

            b.Harness.Vitals.IsHealthZero = true;
            b.Tick(0.5);

            Assert.AreEqual(EBossFlowState.Defeated, b.Harness.Brain.FlowState);
            Assert.AreEqual(EWolbaekWardState.Stopped, b.Ward.State);
            CollectionAssert.Contains(b.Listener.Events, "cleared:Death");
        }
    }
}
