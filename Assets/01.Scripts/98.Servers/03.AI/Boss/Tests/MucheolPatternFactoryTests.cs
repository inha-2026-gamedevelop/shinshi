// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
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
    // 무철 P01(팔 휘두르기), P02(양손 내려찍기) 실행 검증, 시작 수치는 플레이 테스트 전 임시값이라 운영 카탈로그가 아니라 이 테스트의 데이터를 쓴다
    // 경계 시각(예고 끝, 추적 끝)은 부동소수 오차가 판정을 뒤집지 못하게 경계에서 충분히 떨어진 시각으로만 검증한다
    public class MucheolPatternFactoryTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID   = "boss_wolbaek";
        private const string OTHER_ID     = "mucheol_p03";
        private const float  TEST_HREF    = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  ANGLE_TOLERANCE = 0.05f;
        private const float  TIME_MARGIN  = 0.1f;  // 초, 경계 전후로 두는 여유
        private const int    PROBE_ID     = 11;

        /****************************************
        *               Helpers
        ****************************************/

        // 임시 시작 수치 묶음, 설계표의 값 그대로이고 기본 가중치와 거리 배율은 선택 쪽 값이라 정의 생성에만 쓴다
        private readonly struct Spec
        {
            public readonly string Id;
            public readonly float  Range;
            public readonly float  Arc;
            public readonly float  Weight;
            public readonly float  Telegraph;
            public readonly float  Tracking;
            public readonly float  TurnRate;
            public readonly float  Recovery;
            public readonly float  Damage;

            public Spec(string id, float range, float arc, float weight, float telegraph, float tracking, float turnRate,
                float recovery, float damage)
            {
                Id        = id;
                Range     = range;
                Arc       = arc;
                Weight    = weight;
                Telegraph = telegraph;
                Tracking  = tracking;
                TurnRate  = turnRate;
                Recovery  = recovery;
                Damage    = damage;
            }

            public float HalfArc        => Arc * 0.5f;
            public float MaxTrackDegree => TurnRate * Tracking;
            public float EndTime        => Telegraph + Recovery;
        }

        private static readonly Spec P01 = new Spec(MucheolPatternFactory.P01_ID, 5f, 120f, 30f, 0.7f, 0.35f, 180f, 0.6f, 0.20f);
        private static readonly Spec P02 = new Spec(MucheolPatternFactory.P02_ID, 5f, 70f, 25f, 1.2f, 0.6f, 120f, 1.2f, 0.32f);

        private static Spec SpecOf(string id)
        {
            return (id == MucheolPatternFactory.P01_ID) ? P01 : P02;
        }

        private static PatternDefinition Make(Spec spec)
        {
            return new PatternDefinition(spec.Id, MucheolPatternFactory.BOSS_ID, 1, spec.Weight,
                new[] { 1f, 0f, 0f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                spec.Telegraph, spec.Tracking, spec.TurnRate, 0f, spec.Recovery,
                EnemyCatalogSO.AttackShapeSpec.Fan(spec.Range, spec.Arc), spec.Damage, true);
        }

        private static Vector3 Polar(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
        }

        private static int CountHits(FakeDamageResolver resolver, int targetId)
        {
            int count = 0;
            for (int i = 0; i < resolver.Requests.Count; ++i)
            {
                if (resolver.Requests[i].HitTargetId == targetId)
                {
                    ++count;
                }
            }

            return count;
        }

        private sealed class SwingQuery : ICombatTargetQuery
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

        private sealed class SwingListener : IBossFanTelegraphListener
        {
            public int                        StartCount;
            public int                        HitEventCount;
            public int                        LastHitCount = -1;
            public float                      StartRadius;
            public float                      StartArc;
            public List<EBossInterruptReason> Cancels = new List<EBossInterruptReason>();

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float radius,
                float arcAngle, float telegraphSeconds)
            {
                ++StartCount;
                StartRadius = radius;
                StartArc    = arcAngle;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction)
            {
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction)
            {
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                ++HitEventCount;
                LastHitCount = hitCount;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Cancels.Add(reason);
            }
        }

        // 팩토리가 만든 패턴을 공격 FSM에 올린 최소 구성, 추적 대상은 보스 앞 3m에 선다
        private sealed class Rig
        {
            public FakeCombatTarget   Self     { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget   Tracked  { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public SwingQuery         Query    { get; } = new SwingQuery();
            public FakeDamageResolver Resolver { get; } = new FakeDamageResolver();
            public SwingListener      Listener { get; } = new SwingListener();
            public BossAttackFsm      Fsm      { get; } = new BossAttackFsm();
            public BossDamageGate     Gate     { get; }
            public BossPatternBase    Pattern  { get; }
            public PatternDefinition  Definition { get; }
            public BossPatternContext Context  { get; }
            public Spec               Spec     { get; }

            public Rig(string patternId)
            {
                Spec = SpecOf(patternId);
                Query.Players.Add(Tracked);
                Gate       = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = Make(Spec);
                Pattern    = (BossPatternBase)new MucheolPatternFactory(TEST_HREF, Listener).Create(Definition);
                Context    = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position };
                Query.Players.Add(player);
                return player;
            }

            public void Start(double serverTime)
            {
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }
        }

        /****************************************
        *               Factory
        ****************************************/

        [TestCase(MucheolPatternFactory.P01_ID)]
        [TestCase(MucheolPatternFactory.P02_ID)]
        public void Create_ImplementedPattern_ReturnsFanPattern(string patternId)
        {
            var factory = new MucheolPatternFactory(TEST_HREF);
            PatternDefinition definition = Make(SpecOf(patternId));

            Assert.IsTrue(factory.CanCreate(definition));
            Assert.IsInstanceOf<BossFanBlastPattern>(factory.Create(definition));
        }

        [Test]
        public void Create_OtherPatternIds_ReturnsNullAndCannotCreate()
        {
            var factory = new MucheolPatternFactory(TEST_HREF);
            PatternDefinition wolbaek = new PatternDefinition("wolbaek_p02", WOLBAEK_ID, 1, EEnemyAttackShape.Fan, true);
            PatternDefinition other   = new PatternDefinition(OTHER_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);

            Assert.IsFalse(factory.CanCreate(wolbaek));
            Assert.IsNull(factory.Create(wolbaek));
            Assert.IsFalse(factory.CanCreate(other));
            Assert.IsNull(factory.Create(other));
            Assert.IsFalse(factory.CanCreate(null));
            Assert.IsNull(factory.Create(null));
        }

        [Test]
        public void Constructor_NonPositiveReferenceHealth_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolPatternFactory(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolPatternFactory(-1f));
        }

        [Test]
        public void Create_CalledTwice_ReturnsSeparateInstances()
        {
            var factory = new MucheolPatternFactory(TEST_HREF);
            PatternDefinition definition = Make(P01);

            Assert.AreNotSame(factory.Create(definition), factory.Create(definition));
        }

        /****************************************
        *            Swing Behaviour
        ****************************************/

        [TestCase(MucheolPatternFactory.P01_ID)]
        [TestCase(MucheolPatternFactory.P02_ID)]
        public void Tick_BeforeTelegraphEnds_AppliesNoDamageAndReportsShape(string patternId)
        {
            Rig rig = new Rig(patternId);
            rig.Start(0.0);

            rig.Fsm.Tick(rig.Spec.Telegraph * 0.5f);
            rig.Fsm.Tick(rig.Spec.Telegraph - TIME_MARGIN);

            Assert.AreEqual(EBossAttackState.Telegraph, rig.Fsm.State);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(1, rig.Listener.StartCount);
            Assert.AreEqual(rig.Spec.Range, rig.Listener.StartRadius, 0.0001f);
            Assert.AreEqual(rig.Spec.Arc, rig.Listener.StartArc, 0.0001f);
        }

        [TestCase(MucheolPatternFactory.P01_ID)]
        [TestCase(MucheolPatternFactory.P02_ID)]
        public void Tick_AfterTelegraphEnd_HitsTargetInsideSectorOnceWithExpectedRequest(string patternId)
        {
            Rig rig = new Rig(patternId);
            rig.Start(0.0);

            rig.Fsm.Tick(rig.Spec.Telegraph - TIME_MARGIN);
            rig.Fsm.Tick(rig.Spec.Telegraph + TIME_MARGIN);

            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            DamageRequest request = rig.Resolver.Requests[0];
            Assert.AreEqual(rig.Self.CombatId, request.AttackerId);
            Assert.AreEqual(rig.Tracked.CombatId, request.HitTargetId);
            Assert.AreEqual(rig.Context.CastInstanceId, request.CastInstanceId);
            Assert.AreEqual(patternId, request.SourceDefinitionId);
            Assert.AreEqual(rig.Spec.Damage * TEST_HREF, request.RawDamage, 0.001f);
            Assert.AreEqual(EDamageFlags.None, request.Flags);
            Assert.IsTrue(request.HasValidIds);
            Assert.AreEqual(1, rig.Listener.HitEventCount);
            Assert.AreEqual(1, rig.Listener.LastHitCount);
        }

        [TestCase(MucheolPatternFactory.P01_ID)]
        [TestCase(MucheolPatternFactory.P02_ID)]
        public void Tick_TargetsOutsideRangeOrAngle_AreNotHit(string patternId)
        {
            Rig rig = new Rig(patternId);
            Spec spec = rig.Spec;
            FakeCombatTarget tooFar     = rig.AddPlayer(PROBE_ID, Polar(0f, spec.Range + 1f));
            FakeCombatTarget tooWide    = rig.AddPlayer(PROBE_ID + 1, Polar(spec.HalfArc + 10f, 3f));
            FakeCombatTarget behind     = rig.AddPlayer(PROBE_ID + 2, Polar(180f, 3f));
            FakeCombatTarget insideEdge = rig.AddPlayer(PROBE_ID + 3, Polar(spec.HalfArc - 1f, spec.Range - 0.5f));
            rig.Start(0.0);

            rig.Fsm.Tick(spec.Telegraph - TIME_MARGIN);
            rig.Fsm.Tick(spec.Telegraph + TIME_MARGIN);

            Assert.AreEqual(0, CountHits(rig.Resolver, tooFar.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, tooWide.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, behind.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, insideEdge.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
        }

        // 같은 위치라도 각도가 넓은 P01은 맞고 좁은 P02는 맞지 않아야 한다, 두 패턴의 각도 값이 섞이지 않는다는 확인이다
        [Test]
        public void Tick_TargetBetweenHalfArcs_IsHitByP01ButNotByP02()
        {
            float betweenDegrees = (P01.HalfArc + P02.HalfArc) * 0.5f;

            Rig first = new Rig(MucheolPatternFactory.P01_ID);
            FakeCombatTarget firstProbe = first.AddPlayer(PROBE_ID, Polar(betweenDegrees, 3f));
            first.Start(0.0);
            first.Fsm.Tick(P01.Telegraph + TIME_MARGIN);

            Rig second = new Rig(MucheolPatternFactory.P02_ID);
            FakeCombatTarget secondProbe = second.AddPlayer(PROBE_ID, Polar(betweenDegrees, 3f));
            second.Start(0.0);
            second.Fsm.Tick(P02.Telegraph + TIME_MARGIN);

            Assert.AreEqual(1, CountHits(first.Resolver, firstProbe.CombatId));
            Assert.AreEqual(0, CountHits(second.Resolver, secondProbe.CombatId));
        }

        [TestCase(MucheolPatternFactory.P01_ID)]
        [TestCase(MucheolPatternFactory.P02_ID)]
        public void Tick_AfterStrike_DoesNotHitSameTargetAgain(string patternId)
        {
            Rig rig = new Rig(patternId);
            Spec spec = rig.Spec;
            FakeCombatTarget bystander = rig.AddPlayer(PROBE_ID, Polar(spec.HalfArc * 0.5f, 4f));
            rig.Start(0.0);

            rig.Fsm.Tick(spec.Telegraph + TIME_MARGIN);
            rig.Fsm.Tick(spec.Telegraph + (TIME_MARGIN * 2f));
            rig.Fsm.Tick(spec.Telegraph + (spec.Recovery * 0.5f));
            rig.Fsm.Tick(spec.EndTime + TIME_MARGIN);
            rig.Fsm.Tick(spec.EndTime + 1f);

            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, bystander.CombatId));
            Assert.AreEqual(1, rig.Listener.HitEventCount);
            Assert.IsTrue(rig.Pattern.IsFinished);
        }

        [TestCase(MucheolPatternFactory.P01_ID)]
        [TestCase(MucheolPatternFactory.P02_ID)]
        public void Cancel_DuringTelegraph_AppliesNoDamageEvenAfterTelegraphTime(string patternId)
        {
            Rig rig = new Rig(patternId);
            rig.Start(0.0);
            rig.Fsm.Tick(rig.Spec.Telegraph * 0.5f);

            rig.Fsm.Cancel(EBossInterruptReason.Death);
            rig.Fsm.Tick(rig.Spec.Telegraph + TIME_MARGIN);
            rig.Fsm.Tick(rig.Spec.EndTime + TIME_MARGIN);

            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Listener.HitEventCount);
            Assert.AreEqual(1, rig.Listener.Cancels.Count);
            Assert.AreEqual(EBossInterruptReason.Death, rig.Listener.Cancels[0]);
        }

        // 추적 구간 안에서는 회전 속도만큼, 구간이 끝나면 회전 속도 x 추적 시간에서 고정된다
        [TestCase(MucheolPatternFactory.P01_ID)]
        [TestCase(MucheolPatternFactory.P02_ID)]
        public void Tick_DuringTracking_RotatesAtMostTurnRateAndLocksAfterTrackingWindow(string patternId)
        {
            Rig rig = new Rig(patternId);
            Spec spec = rig.Spec;
            rig.Tracked.Position = new Vector3(3f, 0f, 0f); // 정면 대비 90도, 두 패턴의 최대 회전량보다 크다
            rig.Start(0.0);
            BossFanBlastPattern fan = (BossFanBlastPattern)rig.Pattern;
            Vector3 initial = rig.Self.Forward;
            float midTime = spec.Tracking * 0.5f;

            rig.Fsm.Tick(midTime);

            Assert.AreEqual(spec.TurnRate * midTime, Vector3.Angle(initial, fan.AimDirection), ANGLE_TOLERANCE);
            Assert.IsFalse(fan.IsDirectionLocked);

            rig.Fsm.Tick(spec.Tracking + TIME_MARGIN);

            Assert.IsTrue(fan.IsDirectionLocked);
            Assert.AreEqual(spec.MaxTrackDegree, Vector3.Angle(initial, fan.AimDirection), ANGLE_TOLERANCE);
        }

        /****************************************
        *            Production Catalog
        ****************************************/

        [Test]
        public void CatalogAsset_HasMucheolBoss_ButIsNotTunedAndHasNoMucheolPatterns()
        {
            BossPatternCatalogSO catalog = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assert.IsNotNull(catalog);

            Assert.IsTrue(catalog.TryGetBoss(MucheolPatternFactory.BOSS_ID, out _));
            Assert.IsFalse(catalog.IsTuned(MucheolPatternFactory.BOSS_ID));

            // 1페이즈 나머지가 미구현인 채 운영 카탈로그에 무철 패턴이 들어가면 미구현 패턴이 선택될 수 있다
            for (int i = 0; i < catalog.Patterns.Count; ++i)
            {
                Assert.AreNotEqual(MucheolPatternFactory.BOSS_ID, catalog.Patterns[i].BossId);
            }

            // 새 보스를 더해도 월백 수치는 그대로여야 한다
            Assert.IsTrue(catalog.IsTuned(WOLBAEK_ID));
        }
    }
}
