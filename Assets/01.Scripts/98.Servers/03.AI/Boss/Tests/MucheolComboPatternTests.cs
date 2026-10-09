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
    // 무철 P05(2연타) 실행 검증, 시작 수치는 플레이 테스트 전 임시값이라 운영 카탈로그가 아니라 이 테스트의 데이터를 쓴다
    // 경계(예고 끝, 실행 끝, 각도)는 부동소수 오차가 판정을 뒤집지 않게 경계에서 떨어진 시각과 각도로 검증한다.
    // 실행 끝 경계에 정확히 걸치는 둘째 타만 FSM과 같은 식(double로 더함)으로 시각을 만들어 따로 확인한다
    public class MucheolComboPatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float TEST_HREF   = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float TOLERANCE   = 0.0001f;
        private const float TIME_MARGIN = 0.1f;  // 초, 경계 전후로 두는 여유
        private const float ANGLE_TOLERANCE = 0.05f;
        private const int   PROBE_ID    = 11;

        // 임시 시작 수치, 정의 쪽(예고, 추적, 실행, 회복)과 콤보 데이터 쪽(타별)
        private const float WEIGHT      = 10f;
        private const float TELEGRAPH   = 0.8f;
        private const float TRACKING    = 0.4f;
        private const float TURN_RATE   = 150f;
        private const float ACTIVE      = 0.5f;
        private const float RECOVERY    = 1.2f;
        private const float RANGE       = 5f;
        private const float FIRST_ARC   = 120f;
        private const float SECOND_ARC  = 70f;
        private const float FIRST_DAMAGE  = 0.14f;
        private const float SECOND_DAMAGE = 0.24f;
        private const float SECOND_DELAY  = 0.5f;

        // 실행 시작 0.8초, 둘째 타 1.3초, 회복 끝 2.5초
        private const float EXECUTE_START = TELEGRAPH;
        private const float SECOND_AT     = TELEGRAPH + SECOND_DELAY;
        private const float END_AT        = TELEGRAPH + ACTIVE + RECOVERY;

        /****************************************
        *               Helpers
        ****************************************/

        private static MucheolComboSpec MakeSpec()
        {
            return new MucheolComboSpec(
                new MucheolComboHit(RANGE, FIRST_ARC, FIRST_DAMAGE, 0f),
                new MucheolComboHit(RANGE, SECOND_ARC, SECOND_DAMAGE, SECOND_DELAY));
        }

        // 정의의 형태와 피해는 이 패턴이 쓰지 않는다. 예고, 추적, 실행, 회복, 방어 가능 여부만 읽는다
        private static PatternDefinition MakeP05()
        {
            return new PatternDefinition(MucheolPatternFactory.P05_ID, MucheolPatternFactory.BOSS_ID, 1, WEIGHT,
                new[] { 1f, 0f, 0f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, TRACKING, TURN_RATE, ACTIVE, RECOVERY, EnemyCatalogSO.AttackShapeSpec.Fan(RANGE, FIRST_ARC), FIRST_DAMAGE, true);
        }

        private static Vector3 Polar(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
        }

        private static List<DamageRequest> RequestsFor(FakeDamageResolver resolver, int targetId)
        {
            List<DamageRequest> list = new List<DamageRequest>();
            for (int i = 0; i < resolver.Requests.Count; ++i)
            {
                if (resolver.Requests[i].HitTargetId == targetId)
                {
                    list.Add(resolver.Requests[i]);
                }
            }

            return list;
        }

        private sealed class ComboQuery : ICombatTargetQuery
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
                    FakeCombatTarget candidate = Players[i];
                    Vector3 offset = candidate.Position - center;
                    offset.y = 0f;

                    if ((candidate.LifeState == EParticipantLifeState.Alive) && (candidate.Faction == faction)
                        && (includeSummons || !candidate.IsSummon) && (offset.sqrMagnitude <= (radius * radius)))
                    {
                        results.Add(candidate);
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

        private sealed class ComboListener : IBossComboTelegraphListener
        {
            public int                        StartCount;
            public float                      StartSeconds;
            public int                        StartHitCount;
            public IReadOnlyList<MucheolComboHit> StartHits;
            public int                        LockCount;
            public Vector3                    LockDirection;
            public List<int>                  HitIndices = new List<int>();
            public List<int>                  HitCounts  = new List<int>();
            public List<EBossInterruptReason> Cancels    = new List<EBossInterruptReason>();

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float telegraphSeconds,
                IReadOnlyList<MucheolComboHit> hits)
            {
                ++StartCount;
                StartSeconds  = telegraphSeconds;
                StartHitCount = hits.Count;
                StartHits     = hits;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction)
            {
                ++LockCount;
                LockDirection = direction;
            }

            public void OnHit(int castInstanceId, int hitIndex, int hitCount)
            {
                HitIndices.Add(hitIndex);
                HitCounts.Add(hitCount);
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Cancels.Add(reason);
            }
        }

        // 팩토리가 만든 패턴을 공격 FSM에 올린 최소 구성, 추적 대상은 보스 앞 3m에 선다
        private sealed class Rig
        {
            public FakeCombatTarget    Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget    Tracked  { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public ComboQuery          Query    { get; } = new ComboQuery();
            public FakeDamageResolver  Resolver { get; } = new FakeDamageResolver();
            public ComboListener       Listener { get; } = new ComboListener();
            public BossAttackFsm       Fsm      { get; } = new BossAttackFsm();
            public BossDamageGate      Gate     { get; }
            public MucheolComboPattern Pattern  { get; }
            public PatternDefinition   Definition { get; }
            public BossPatternContext  Context  { get; }

            public Rig(MucheolComboSpec spec = null)
            {
                Query.Players.Add(Tracked);
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = MakeP05();
                Pattern    = (MucheolComboPattern)new MucheolPatternFactory(TEST_HREF, null, null, null, null, spec ?? MakeSpec(), Listener).Create(Definition);
                Context    = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position };
                Query.Players.Add(player);
                return player;
            }

            public void Start(double serverTime = 0.0)
            {
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }
        }

        /****************************************
        *            Combo Spec
        ****************************************/

        [Test]
        public void Spec_ValidHits_KeepsOrderAndCopiesInput()
        {
            MucheolComboHit[] source =
            {
                new MucheolComboHit(RANGE, FIRST_ARC, FIRST_DAMAGE, 0f),
                new MucheolComboHit(RANGE, SECOND_ARC, SECOND_DAMAGE, SECOND_DELAY)
            };
            MucheolComboSpec spec = new MucheolComboSpec(source);

            source[1] = new MucheolComboHit(1f, 10f, 9f, 9f); // 만든 뒤 원본 배열을 바꿔도 영향이 없어야 한다

            Assert.AreEqual(2, spec.HitCount);
            Assert.AreEqual(SECOND_ARC, spec.Hits[1].ArcAngle, TOLERANCE);
            Assert.AreEqual(SECOND_DAMAGE, spec.Hits[1].DamageRef, TOLERANCE);
            Assert.AreEqual(SECOND_DELAY, spec.Hits[1].DelaySeconds, TOLERANCE);
        }

        // 공개 목록으로는 값을 바꿀 수 없다. 배열로 캐스팅할 수 없고, 목록 인터페이스로 쓰기를 시도하면 예외다
        [Test]
        public void Spec_PublicHits_CannotBeMutatedThroughTheListOrACast()
        {
            MucheolComboSpec spec = MakeSpec();
            IReadOnlyList<MucheolComboHit> hits = spec.Hits;

            Assert.IsNull(hits as MucheolComboHit[]);

            IList<MucheolComboHit> asList = hits as IList<MucheolComboHit>;
            if (asList != null)
            {
                Assert.IsTrue(asList.IsReadOnly);
                Assert.Throws<NotSupportedException>(() => asList[0] = new MucheolComboHit(1f, 10f, 9f, 9f));
                Assert.Throws<NotSupportedException>(() => asList.Add(new MucheolComboHit(1f, 10f, 9f, 9f)));
                Assert.Throws<NotSupportedException>(() => asList.RemoveAt(0));
                Assert.Throws<NotSupportedException>(() => asList.Clear());
            }

            ICollection<MucheolComboHit> asCollection = hits as ICollection<MucheolComboHit>;
            if (asCollection != null)
            {
                Assert.IsTrue(asCollection.IsReadOnly);
            }

            Assert.AreEqual(2, spec.HitCount);
            Assert.AreEqual(FIRST_ARC, spec.Hits[0].ArcAngle, TOLERANCE);
            Assert.AreEqual(SECOND_DAMAGE, spec.Hits[1].DamageRef, TOLERANCE);
        }

        // 예고 알림으로 넘어가는 목록도 같은 읽기 전용 목록이다
        [Test]
        public void Begin_HitListHandedToListener_CannotBeMutated()
        {
            Rig rig = new Rig();
            rig.Start();

            IReadOnlyList<MucheolComboHit> received = rig.Listener.StartHits;

            Assert.IsNotNull(received);
            Assert.IsNull(received as MucheolComboHit[]);
            IList<MucheolComboHit> asList = received as IList<MucheolComboHit>;
            if (asList != null)
            {
                Assert.Throws<NotSupportedException>(() => asList[0] = new MucheolComboHit(1f, 10f, 9f, 9f));
            }

            Assert.AreEqual(FIRST_DAMAGE, received[0].DamageRef, TOLERANCE);
        }

        [Test]
        public void Spec_InvalidValues_Throw()
        {
            Assert.Throws<ArgumentException>(() => new MucheolComboSpec());
            Assert.Throws<ArgumentException>(() => new MucheolComboSpec((MucheolComboHit[])null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolComboSpec(new MucheolComboHit(0f, 90f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolComboSpec(new MucheolComboHit(5f, 0f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolComboSpec(new MucheolComboHit(5f, 361f, 0.1f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolComboSpec(new MucheolComboHit(5f, 90f, 0f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolComboSpec(new MucheolComboHit(5f, 90f, 0.1f, -0.1f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolComboSpec(new MucheolComboHit(5f, 90f, 0.1f, float.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolComboSpec(
                new MucheolComboHit(5f, 90f, 0.1f, 0.5f), new MucheolComboHit(5f, 90f, 0.1f, 0.25f)));
        }

        /****************************************
        *               Factory
        ****************************************/

        [Test]
        public void Create_P05WithoutComboSpec_CannotCreateAndReturnsNull()
        {
            var factory = new MucheolPatternFactory(TEST_HREF);
            PatternDefinition definition = MakeP05();

            Assert.IsFalse(factory.CanCreate(definition));
            Assert.IsNull(factory.Create(definition));
        }

        [Test]
        public void Create_P05WithComboSpec_ReturnsComboPattern()
        {
            var factory = new MucheolPatternFactory(TEST_HREF, null, null, null, null, MakeSpec());
            PatternDefinition definition = MakeP05();

            Assert.IsTrue(factory.CanCreate(definition));
            Assert.IsInstanceOf<MucheolComboPattern>(factory.Create(definition));
        }

        // 콤보 데이터를 넘겨도 P01~P04의 종류와 생성 조건은 그대로다
        [Test]
        public void Create_OtherPatterns_KeepTheirTypesWithComboSpec()
        {
            var withSpec    = new MucheolPatternFactory(TEST_HREF, null, null, null, null, MakeSpec());
            var withMovement = new MucheolPatternFactory(TEST_HREF, null, new StubMovement(), null, null, MakeSpec());
            PatternDefinition p01 = new PatternDefinition(MucheolPatternFactory.P01_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);
            PatternDefinition p02 = new PatternDefinition(MucheolPatternFactory.P02_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);
            PatternDefinition p03 = new PatternDefinition(MucheolPatternFactory.P03_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Dash, true);
            PatternDefinition p04 = new PatternDefinition(MucheolPatternFactory.P04_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Circle, true);

            Assert.IsInstanceOf<BossFanBlastPattern>(withSpec.Create(p01));
            Assert.IsInstanceOf<BossFanBlastPattern>(withSpec.Create(p02));
            Assert.IsFalse(withSpec.CanCreate(p03));
            Assert.IsNull(withSpec.Create(p03));
            Assert.IsInstanceOf<BossCircleBlastPattern>(withSpec.Create(p04));
            Assert.IsInstanceOf<BossDashPattern>(withMovement.Create(p03));
        }

        private sealed class StubMovement : IBossMovement
        {
            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return maxDistance;
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                return to;
            }
        }

        /****************************************
        *            Timing And Damage
        ****************************************/

        [Test]
        public void Begin_ReportsTelegraphSecondsAndHitList()
        {
            Rig rig = new Rig();
            rig.Start();

            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(TELEGRAPH, rig.Listener.StartSeconds, TOLERANCE);
            Assert.AreEqual(2, rig.Listener.StartHitCount);
        }

        [Test]
        public void Tick_BeforeExecute_AppliesNoDamage()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.Fsm.Tick(TELEGRAPH * 0.5f);
            rig.Fsm.Tick(TELEGRAPH - TIME_MARGIN);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Fsm.State);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.HitIndices.Count);
        }

        [Test]
        public void Tick_BetweenHits_AppliesOnlyFirstHitWithFirstDamage()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.Fsm.Tick(TELEGRAPH - TIME_MARGIN);
            rig.Fsm.Tick(EXECUTE_START + TIME_MARGIN);
            rig.Fsm.Tick(SECOND_AT - TIME_MARGIN);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            Assert.AreEqual(FIRST_DAMAGE * TEST_HREF, rig.Resolver.Requests[0].RawDamage, 0.001f);
            CollectionAssert.AreEqual(new[] { 0 }, rig.Listener.HitIndices);
        }

        [Test]
        public void Tick_ThroughBothHits_AppliesTwoHitsInOrderWithSeparateActionIdsAndSharedCastId()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.Fsm.Tick(TELEGRAPH - TIME_MARGIN);
            rig.Fsm.Tick(EXECUTE_START + TIME_MARGIN);
            rig.Fsm.Tick(SECOND_AT + TIME_MARGIN);
            rig.Fsm.Tick(END_AT + 1f);

            List<DamageRequest> hits = RequestsFor(rig.Resolver, rig.Tracked.CombatId);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(FIRST_DAMAGE * TEST_HREF, hits[0].RawDamage, 0.001f);
            Assert.AreEqual(SECOND_DAMAGE * TEST_HREF, hits[1].RawDamage, 0.001f);
            Assert.AreEqual(rig.Context.CastInstanceId, hits[0].CastInstanceId);
            Assert.AreEqual(rig.Context.CastInstanceId, hits[1].CastInstanceId);
            Assert.AreNotEqual(hits[0].ActionInstanceId, hits[1].ActionInstanceId);
            Assert.AreEqual(MucheolPatternFactory.P05_ID, hits[0].SourceDefinitionId);
            Assert.AreEqual(EDamageFlags.None, hits[0].Flags);
            Assert.AreEqual(EDamageFlags.None, hits[1].Flags);
            Assert.IsTrue(hits[0].HasValidIds);
            CollectionAssert.AreEqual(new[] { 0, 1 }, rig.Listener.HitIndices);
            CollectionAssert.AreEqual(new[] { 1, 1 }, rig.Listener.HitCounts);
            Assert.AreEqual(2, rig.Pattern.ResolvedHitCount);
            Assert.IsTrue(rig.Pattern.IsFinished);
        }

        // 한 타 안에서 대상이 여러 명이어도 대상당 한 번이고, 타가 바뀌면 다시 맞을 수 있다
        [Test]
        public void Tick_MultipleTargets_EachHitOncePerComboHit()
        {
            Rig rig = new Rig();
            FakeCombatTarget second = rig.AddPlayer(PROBE_ID, Polar(10f, 4f));
            rig.Start();

            rig.Fsm.Tick(EXECUTE_START + TIME_MARGIN);
            rig.Fsm.Tick(SECOND_AT + TIME_MARGIN);
            rig.Fsm.Tick(END_AT + 1f);

            Assert.AreEqual(2, RequestsFor(rig.Resolver, rig.Tracked.CombatId).Count);
            Assert.AreEqual(2, RequestsFor(rig.Resolver, second.CombatId).Count);
            Assert.AreEqual(4, rig.Resolver.Requests.Count);
            CollectionAssert.AreEqual(new[] { 2, 2 }, rig.Listener.HitCounts);
        }

        /****************************************
        *            Hit Shape Per Hit
        ****************************************/

        // 정면 대비 50도는 첫 타(반각 60도)에는 맞고 둘째 타(반각 35도)에는 맞지 않는다
        [Test]
        public void Tick_TargetsAtDifferentAngles_AreHitByMatchingHitsOnly()
        {
            Rig rig = new Rig();
            FakeCombatTarget wide    = rig.AddPlayer(PROBE_ID, Polar(50f, 3f));       // 첫 타만
            FakeCombatTarget center  = rig.AddPlayer(PROBE_ID + 1, Polar(20f, 3f));   // 두 타 모두
            FakeCombatTarget outside = rig.AddPlayer(PROBE_ID + 2, Polar(80f, 3f));   // 둘 다 아님
            FakeCombatTarget tooFar  = rig.AddPlayer(PROBE_ID + 3, Polar(0f, RANGE + 1f));
            rig.Start();

            rig.Fsm.Tick(EXECUTE_START + TIME_MARGIN);
            rig.Fsm.Tick(SECOND_AT + TIME_MARGIN);

            Assert.AreEqual(1, RequestsFor(rig.Resolver, wide.CombatId).Count);
            Assert.AreEqual(FIRST_DAMAGE * TEST_HREF, RequestsFor(rig.Resolver, wide.CombatId)[0].RawDamage, 0.001f);
            Assert.AreEqual(2, RequestsFor(rig.Resolver, center.CombatId).Count);
            Assert.AreEqual(0, RequestsFor(rig.Resolver, outside.CombatId).Count);
            Assert.AreEqual(0, RequestsFor(rig.Resolver, tooFar.CombatId).Count);
        }

        // 타 사이에 움직인 대상은 둘째 타의 위치로 판정된다
        [Test]
        public void Tick_TargetMovesBetweenHits_SecondHitUsesNewPosition()
        {
            Rig rig = new Rig();
            FakeCombatTarget leaves  = rig.AddPlayer(PROBE_ID, Polar(20f, 3f));
            FakeCombatTarget arrives = rig.AddPlayer(PROBE_ID + 1, Polar(80f, 3f));
            rig.Start();

            rig.Fsm.Tick(EXECUTE_START + TIME_MARGIN);
            leaves.Position  = Polar(80f, 3f);
            arrives.Position = Polar(20f, 3f);
            rig.Fsm.Tick(SECOND_AT + TIME_MARGIN);

            Assert.AreEqual(1, RequestsFor(rig.Resolver, leaves.CombatId).Count);
            Assert.AreEqual(FIRST_DAMAGE * TEST_HREF, RequestsFor(rig.Resolver, leaves.CombatId)[0].RawDamage, 0.001f);
            Assert.AreEqual(1, RequestsFor(rig.Resolver, arrives.CombatId).Count);
            Assert.AreEqual(SECOND_DAMAGE * TEST_HREF, RequestsFor(rig.Resolver, arrives.CombatId)[0].RawDamage, 0.001f);
        }

        /****************************************
        *            Shared Direction
        ****************************************/

        // 타깃이 오른쪽 90도에 있으면 추적 시간 안에 최단 경로로 돌아 고정 시각에 90도에 닿고(절반 시각에는 절반), 두 타가 이 방향을 쓴다
        [Test]
        public void Tick_LocksDirectionAfterTrackingWindow_AndBothHitsShareIt()
        {
            const float TARGET_DEGREES = 90f;

            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            FakeCombatTarget onAim = rig.AddPlayer(PROBE_ID, Polar(TARGET_DEGREES, 3f));
            rig.Start();

            rig.Fsm.Tick(TRACKING * 0.5f);
            Assert.AreEqual(TARGET_DEGREES * 0.5f, Vector3.Angle(Vector3.forward, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            rig.Fsm.Tick(TRACKING + TIME_MARGIN);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
            Assert.AreEqual(TARGET_DEGREES, Vector3.Angle(Vector3.forward, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Vector3 locked = rig.Pattern.AimDirection;

            // 고정 뒤 타깃이 반대편으로 가도 방향은 그대로다
            rig.Tracked.Position = new Vector3(-10f, 0f, 0f);
            rig.Fsm.Tick(TELEGRAPH - TIME_MARGIN);
            rig.Fsm.Tick(EXECUTE_START + TIME_MARGIN);
            rig.Fsm.Tick(SECOND_AT + TIME_MARGIN);

            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(1, rig.Listener.LockCount);
            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Listener.LockDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(2, RequestsFor(rig.Resolver, onAim.CombatId).Count);
        }

        /****************************************
        *               Cancel
        ****************************************/

        [Test]
        public void Cancel_DuringTelegraph_AppliesNoDamage()
        {
            Rig rig = new Rig();
            rig.Start();
            rig.Fsm.Tick(TELEGRAPH * 0.5f);

            rig.Fsm.Cancel(EBossInterruptReason.Death);
            rig.Fsm.Tick(EXECUTE_START + TIME_MARGIN);
            rig.Fsm.Tick(END_AT + 1f);

            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.HitIndices.Count);
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
        }

        // 타 사이에 끊기면 첫 타 피해는 남고 둘째 타는 나오지 않는다
        [Test]
        public void Cancel_BetweenHits_KeepsFirstHitAndSkipsSecond()
        {
            Rig rig = new Rig();
            rig.Start();
            rig.Fsm.Tick(TELEGRAPH - TIME_MARGIN);
            rig.Fsm.Tick(EXECUTE_START + TIME_MARGIN);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);

            rig.Fsm.Cancel(EBossInterruptReason.CombatEnd);
            rig.Fsm.Tick(SECOND_AT + TIME_MARGIN);
            rig.Fsm.Tick(END_AT + 1f);

            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            CollectionAssert.AreEqual(new[] { 0 }, rig.Listener.HitIndices);
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.CombatEnd, rig.Listener.Cancels[0]);
        }

        // 피해 게이트가 닫히면(보스 사망, 전투 종료) 패턴이 취소되지 않아도 둘째 타 요청이 나가지 않는다
        [Test]
        public void Tick_GateClosedBetweenHits_SkipsSecondHitRequest()
        {
            Rig rig = new Rig();
            rig.Start();
            rig.Fsm.Tick(EXECUTE_START + TIME_MARGIN);

            rig.Gate.CloseForGood();
            rig.Fsm.Tick(SECOND_AT + TIME_MARGIN);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
        }

        /****************************************
        *            Coarse Tick Intervals
        ****************************************/

        // 한 틱이 예고부터 회복 끝까지 넘어가도 두 타가 순서대로 한 번씩만 나간다
        [Test]
        public void Tick_SingleHugeStep_AppliesEachHitExactlyOnceInOrder()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.Fsm.Tick(END_AT + 5f);
            rig.Fsm.Tick(END_AT + 6f);
            rig.Fsm.Tick(END_AT + 7f);

            List<DamageRequest> hits = RequestsFor(rig.Resolver, rig.Tracked.CombatId);
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(FIRST_DAMAGE * TEST_HREF, hits[0].RawDamage, 0.001f);
            Assert.AreEqual(SECOND_DAMAGE * TEST_HREF, hits[1].RawDamage, 0.001f);
            CollectionAssert.AreEqual(new[] { 0, 1 }, rig.Listener.HitIndices);
            Assert.IsTrue(rig.Pattern.IsFinished);
        }

        /****************************************
        *       Hit Delay Versus Recovery End
        ****************************************/

        // 실행 시작 기준 지연, 실행 중(0, 0.5), 회복 중(1.6, 회복 끝 1.7 직전), 회복 종료 뒤(2.0)
        private const float LATE_IN_RECOVERY_DELAY = 1.6f;
        private const float AFTER_RECOVERY_DELAY   = 2.0f;

        private static MucheolComboSpec MakeSpecWithLateHits()
        {
            return new MucheolComboSpec(
                new MucheolComboHit(RANGE, FIRST_ARC, FIRST_DAMAGE, 0f),
                new MucheolComboHit(RANGE, SECOND_ARC, SECOND_DAMAGE, SECOND_DELAY),
                new MucheolComboHit(RANGE, FIRST_ARC, FIRST_DAMAGE, LATE_IN_RECOVERY_DELAY),
                new MucheolComboHit(RANGE, FIRST_ARC, SECOND_DAMAGE, AFTER_RECOVERY_DELAY));
        }

        // 회복 종료보다 늦은 타는 틱이 아무리 커도 피해 요청과 OnHit이 나오지 않는다. 앞의 세 타는 그대로 나간다
        [Test]
        public void Tick_SingleHugeStep_DropsHitDelayedPastRecoveryEnd()
        {
            Rig rig = new Rig(MakeSpecWithLateHits());
            rig.Start();

            rig.Fsm.Tick(END_AT + 1000f);
            rig.Fsm.Tick(END_AT + 2000f);

            List<DamageRequest> hits = RequestsFor(rig.Resolver, rig.Tracked.CombatId);
            Assert.AreEqual(3, hits.Count);
            Assert.AreEqual(FIRST_DAMAGE * TEST_HREF, hits[0].RawDamage, 0.001f);
            Assert.AreEqual(SECOND_DAMAGE * TEST_HREF, hits[1].RawDamage, 0.001f);
            Assert.AreEqual(FIRST_DAMAGE * TEST_HREF, hits[2].RawDamage, 0.001f);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rig.Listener.HitIndices);
            Assert.AreEqual(3, rig.Pattern.ScheduledHitCount);
            Assert.AreEqual(3, rig.Pattern.ResolvedHitCount);
            Assert.IsTrue(rig.Pattern.IsFinished);
        }

        // 지연이 회복 종료 뒤인 타만 남겨도 같다, 큰 틱 한 번과 작은 틱 여러 번 모두 요청이 나오지 않는다
        [Test]
        public void Tick_OnlyHitPastRecoveryEnd_NeverAppliesDamageOrOnHit()
        {
            MucheolComboSpec onlyLate = new MucheolComboSpec(
                new MucheolComboHit(RANGE, FIRST_ARC, FIRST_DAMAGE, AFTER_RECOVERY_DELAY));

            Rig huge = new Rig(onlyLate);
            huge.Start();
            huge.Fsm.Tick(END_AT + 1000f);

            Rig stepped = new Rig(onlyLate);
            stepped.Start();
            stepped.Fsm.Tick(EXECUTE_START + TIME_MARGIN);
            stepped.Fsm.Tick(SECOND_AT + TIME_MARGIN);
            stepped.Fsm.Tick(END_AT - TIME_MARGIN);
            stepped.Fsm.Tick(END_AT + 1000f);

            Assert.AreEqual(0, huge.Resolver.Requests.Count);
            Assert.AreEqual(0, huge.Listener.HitIndices.Count);
            Assert.AreEqual(0, huge.Pattern.ScheduledHitCount);
            Assert.AreEqual(0, stepped.Resolver.Requests.Count);
            Assert.AreEqual(0, stepped.Listener.HitIndices.Count);
            Assert.IsTrue(huge.Pattern.IsFinished);
            Assert.IsTrue(stepped.Pattern.IsFinished);
        }

        // 회복 중에 걸리는 타는 유효하다. 실행이 끝난 뒤 회복 구간 안의 틱에서 나가고 순서가 유지된다
        [Test]
        public void Tick_HitDelayedInsideRecovery_StillFiresInOrder()
        {
            Rig rig = new Rig(MakeSpecWithLateHits());
            rig.Start();

            rig.Fsm.Tick(SECOND_AT + TIME_MARGIN);
            Assert.AreEqual(2, rig.Resolver.Requests.Count);

            rig.Fsm.Tick(EXECUTE_START + LATE_IN_RECOVERY_DELAY - TIME_MARGIN);
            Assert.AreEqual(2, rig.Resolver.Requests.Count);

            rig.Fsm.Tick(EXECUTE_START + LATE_IN_RECOVERY_DELAY + (TIME_MARGIN * 0.5f));
            Assert.AreEqual(3, rig.Resolver.Requests.Count);

            rig.Fsm.Tick(END_AT + 1f);
            Assert.AreEqual(3, rig.Resolver.Requests.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rig.Listener.HitIndices);
        }

        // 틱 하나가 실행 시작과 둘째 타를 함께 넘어도 누락도 중복도 없다
        [Test]
        public void Tick_StepAcrossBothHitTimes_AppliesEachHitOnce()
        {
            Rig rig = new Rig();
            rig.Start();
            rig.Fsm.Tick(TELEGRAPH - TIME_MARGIN);

            rig.Fsm.Tick(SECOND_AT + TIME_MARGIN);
            rig.Fsm.Tick(SECOND_AT + (TIME_MARGIN * 2f));
            rig.Fsm.Tick(END_AT + 1f);

            Assert.AreEqual(2, RequestsFor(rig.Resolver, rig.Tracked.CombatId).Count);
            CollectionAssert.AreEqual(new[] { 0, 1 }, rig.Listener.HitIndices);
        }

        // 틱이 실행 끝 경계에 정확히 닿아도 둘째 타가 한 번 나간다, 시각은 FSM이 경계를 만드는 식과 같다
        [Test]
        public void Tick_ExactlyAtExecuteEnd_AppliesSecondHitOnce()
        {
            Rig rig = new Rig();
            rig.Start();
            double executeStart = 0.0 + TELEGRAPH;
            double executeEnd   = executeStart + ACTIVE;

            rig.Fsm.Tick(executeStart);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);

            rig.Fsm.Tick(executeEnd);
            rig.Fsm.Tick(executeEnd + TIME_MARGIN);

            Assert.AreEqual(2, rig.Resolver.Requests.Count);
            CollectionAssert.AreEqual(new[] { 0, 1 }, rig.Listener.HitIndices);
        }
    }
}
