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

using EPatternGroup     = Shinshi.Data.BossPatternCatalogSO.EPatternGroup;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 무철 하드 2페이즈 P07(전장 공격) 실행 검증. 수치는 시험 전 임시안이라 개발 카탈로그의 정의를 그대로 읽어 쓰고,
    // 여기서 확인하는 것은 그 값이 판정, 스냅샷, 피해, 밀림 규칙으로 올바르게 이어지는지다. 밸런스가 맞는지는 이 테스트가 증명하지 않는다.
    // 각도 경계는 공격 영역 쪽에 포함이고, 경계에서 떨어진 값으로도 양쪽을 따로 확인한다
    public class MucheolBattlefieldBlastPatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TEST_HREF      = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  ARENA          = 44f;
        private const float  ARENA_RADIUS   = 22f;
        private const float  BOSS_RADIUS    = 2f;
        private const float  PLAYER_HEALTH  = 1500f;
        private const float  DEFENSE        = 100f;
        private const float  DEFENSE_CONST  = 1000f;
        private const float  REDUCTION_CAP  = 0.6f;
        private const float  TOLERANCE      = 0.01f;
        private const float  POSITION_TOLERANCE = 0.001f;
        private const double TELEGRAPH_END  = 8.0;
        private const float  HALF_ARC       = 157.5f; // 315도의 절반, 안전지대는 뒤쪽 45도다

        private static readonly Vector3 BossAt = Vector3.zero;

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP07()
        {
            PatternDefinition definition = new MucheolDevCatalog(ARENA, true, true).Find(MucheolPhase2PatternFactory.P07_ID);
            Assert.IsNotNull(definition);
            return definition;
        }

        private static Vector3 Polar(float degrees, float distance)
        {
            return Phase2Helpers.Polar(degrees, distance);
        }

        private static float ExpectedDamage(float defense)
        {
            return 0.30f * TEST_HREF * (DEFENSE_CONST / (DEFENSE_CONST + defense));
        }

        private sealed class BattlefieldRecorder : IMucheolBattlefieldListener
        {
            public int                        StartCount;
            public Vector3                    StartOrigin;
            public Vector3                    StartDirection;
            public float                      StartRadius;
            public float                      StartArc;
            public float                      StartSeconds;
            public int                        HitEventCount;
            public int                        LastHitCount = -1;
            public Dictionary<int, float>     Pushed = new Dictionary<int, float>();
            public List<EBossInterruptReason> Cancels = new List<EBossInterruptReason>();

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float radius, float arcAngle,
                float telegraphSeconds)
            {
                ++StartCount;
                StartOrigin    = origin;
                StartDirection = direction;
                StartRadius    = radius;
                StartArc       = arcAngle;
                StartSeconds   = telegraphSeconds;
            }

            public void OnPushed(int castInstanceId, int combatId, float distance)
            {
                Pushed[combatId] = distance;
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

        // 막힘이 정해진 가짜 벽: 어느 방향이든 maxClear보다 멀리는 못 간다
        private sealed class FixedWall : IBossWallQuery
        {
            public float MaxClear = float.PositiveInfinity;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return Mathf.Min(maxDistance, MaxClear);
            }
        }

        // 요청을 기록만 하고 결과를 정해 주는 가짜 피해 창구
        private sealed class ScriptedResolver : IDamageResolver
        {
            private int _nextId = 1;

            public DamageResult Result = DamageResult.Applied(1f, false, false);
            public List<DamageRequest> Requests { get; } = new List<DamageRequest>();

            public DamageResult Apply(in DamageRequest request)
            {
                Requests.Add(request);
                return Result;
            }

            public int IssueInstanceId()
            {
                return _nextId++;
            }
        }

        // 실제 대상 등록부, 피해 창구, 이동 대역을 이은 한 판. 보스는 원점에서 +z를 본다
        private sealed class Rig
        {
            public BossTestRigBody       Boss       { get; }
            public CombatTargetRegistry  Registry   { get; }
            public DamageResolver        Resolver   { get; }
            public BossDamageGate        Gate       { get; }
            public BossTestRigDisplacer  Displacer  { get; }
            public BattlefieldRecorder   Recorder   { get; } = new BattlefieldRecorder();
            public MucheolBattlefieldBlastPattern Pattern { get; }
            public PatternDefinition     Definition { get; }

            public Rig(float pushDistance = 1f, IBossWallQuery walls = null, Vector3? bossPosition = null, Vector3? bossForward = null)
            {
                Registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
                Resolver = new DamageResolver(Registry, new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY,
                    Constants.Combat.HIT_LEDGER_RETENTION_SECONDS, Constants.Combat.LONGEST_ACTION_SECONDS), DEFENSE_CONST, REDUCTION_CAP);
                Gate = new BossDamageGate(Resolver);
                Gate.Open();

                Boss = new BossTestRigBody(Registry.IssueCombatId(), bossPosition ?? BossAt, bossForward ?? Vector3.forward,
                    1000000f, 0f, BOSS_RADIUS, Vector3.zero, ARENA_RADIUS, null);
                Registry.Register(Boss);

                Displacer  = new BossTestRigDisplacer(walls ?? Boss);
                Definition = MakeP07();
                Pattern    = new MucheolBattlefieldBlastPattern(TEST_HREF, new MucheolBattlefieldSpec(pushDistance), Displacer, Recorder);
            }

            public BossTestRigCombatant Add(Vector3 position, float health = PLAYER_HEALTH, bool isSummon = false, float bodyRadius = 0f)
            {
                var target = new BossTestRigCombatant(Registry.IssueCombatId(), position, health, DEFENSE, bodyRadius, isSummon,
                    isSummon ? 99 : CombatIds.NONE);
                Registry.Register(target);
                Displacer.Register(target);
                return target;
            }

            public void Begin(double serverTime = 0.0)
            {
                var context = new BossPatternContext(Definition, 1, Boss.CombatId, Boss, Registry, Gate);
                Pattern.Begin(in context, serverTime);
            }

            public void Strike()
            {
                Resolver.BeginTick(TELEGRAPH_END);
                Pattern.EnterState(EBossAttackState.Execute, TELEGRAPH_END);
            }

            public void Cast()
            {
                Begin();
                Pattern.Tick(EBossAttackState.Telegraph, 2.5);
                Strike();
            }
        }

        /****************************************
        *               Definition
        ****************************************/

        [Test]
        public void Definition_P07_CarriesTheStartingValues()
        {
            PatternDefinition p07 = MakeP07();

            Assert.AreEqual("mucheol_ph2_p07", p07.DefinitionId);
            Assert.AreEqual(2, p07.Phase);
            Assert.AreEqual(EPatternGroup.Large, p07.Group);
            Assert.AreEqual(3f, p07.BaseWeight);
            Assert.AreEqual(3f, p07.EnhancedBaseWeight);
            Assert.AreEqual(44f, p07.Shape.Range);
            Assert.AreEqual(315f, p07.Shape.ArcAngle);
            Assert.AreEqual(8f, p07.TelegraphSeconds);
            Assert.AreEqual(0f, p07.TrackingSeconds, "방향을 따라 돌지 않는다");
            Assert.AreEqual(0f, p07.TurnRate);
            Assert.AreEqual(0f, p07.ActiveSeconds);
            Assert.AreEqual(3.6f, p07.RecoverySeconds);
            Assert.AreEqual(0.30f, p07.DamageRef);
            Assert.IsFalse(p07.Guardable);
            CollectionAssert.AreEqual(new[] { 1f, 1f, 1f }, new List<float>(p07.DistanceMultipliers));
            CollectionAssert.AreEqual(new[] { 1f, 1f, 1f }, new List<float>(p07.DirectionMultipliers));
            CollectionAssert.AreEqual(new[] { 1f, 1f, 1f }, new List<float>(p07.NearbyMultipliers));
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f, 1f }, new List<float>(p07.RecentUseMultipliers));
        }

        [Test]
        public void Spec_NegativeOrNonFinitePush_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolBattlefieldSpec(-0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolBattlefieldSpec(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolBattlefieldSpec(float.PositiveInfinity));
            Assert.AreEqual(0f, new MucheolBattlefieldSpec(0f).PushDistance);
        }

        [Test]
        public void Constructor_MissingSpecOrDisplacer_Throws()
        {
            var displacer = new BossTestRigDisplacer(new FixedWall());

            Assert.Throws<ArgumentNullException>(() => new MucheolBattlefieldBlastPattern(TEST_HREF, null, displacer));
            Assert.Throws<ArgumentNullException>(() => new MucheolBattlefieldBlastPattern(TEST_HREF, new MucheolBattlefieldSpec(1f), null));
        }

        /****************************************
        *          Telegraph And Snapshot
        ****************************************/

        [Test]
        public void Begin_ReportsTheSnapshotAndNeverAnAimUpdate()
        {
            Rig rig = new Rig(bossPosition: new Vector3(3f, 0f, -2f), bossForward: new Vector3(1f, 0f, 1f));
            rig.Add(Polar(0f, 8f));

            rig.Begin();

            Assert.AreEqual(1, rig.Recorder.StartCount);
            Assert.AreEqual(new Vector3(3f, 0f, -2f), rig.Recorder.StartOrigin);
            Assert.AreEqual(new Vector3(1f, 0f, 1f).normalized.x, rig.Recorder.StartDirection.x, POSITION_TOLERANCE);
            Assert.AreEqual(new Vector3(1f, 0f, 1f).normalized.z, rig.Recorder.StartDirection.z, POSITION_TOLERANCE);
            Assert.AreEqual(44f, rig.Recorder.StartRadius);
            Assert.AreEqual(315f, rig.Recorder.StartArc);
            Assert.AreEqual(8f, rig.Recorder.StartSeconds);
            Assert.AreEqual(rig.Recorder.StartOrigin, rig.Pattern.Origin);
        }

        [Test]
        public void Strike_AfterTheBossMovedAndTurned_StillUsesTheSavedOriginAndDirection()
        {
            Rig rig = new Rig();
            BossTestRigCombatant behind = rig.Add(Polar(180f, 8f));       // 저장한 정면의 반대쪽, 안전지대
            BossTestRigCombatant front  = rig.Add(Polar(0f, 8f));         // 저장한 정면 쪽, 공격 영역

            rig.Begin();

            // 예고 중에 보스를 3m 옮기고 반대 방향으로 돌린다. 현재 값을 읽는 구현이면 위 두 대상의 결과가 뒤집힌다
            rig.Boss.Move(rig.Boss.Position, rig.Boss.Position + (Vector3.right * 3f), BOSS_RADIUS);
            rig.Boss.Face(Vector3.back);
            rig.Pattern.Tick(EBossAttackState.Telegraph, 2.5);
            rig.Strike();

            Assert.AreEqual(PLAYER_HEALTH, behind.Health, "저장한 안전지대는 보스가 돌아도 안전하다");
            Assert.Less(front.Health, PLAYER_HEALTH, "저장한 공격 영역은 보스가 돌아도 맞는다");
            Assert.AreEqual(Vector3.forward, rig.Pattern.AimDirection);
            Assert.AreEqual(BossAt, rig.Pattern.Origin);
            Assert.AreEqual(BossAt, rig.Recorder.StartOrigin);

            // 밀림도 저장한 위치에서 바깥이다. 옮겨진 보스(x=3) 기준이면 +x 성분이 달라진다
            Assert.AreEqual(8f + 1f, front.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(0f, front.Position.x, POSITION_TOLERANCE);
        }

        /****************************************
        *            Zone Boundaries
        ****************************************/

        // 경계 각도에서 떨어진 값과 경계 자체. 앞쪽은 모두 맞고, 뒤쪽 45도(157.5도 초과)만 안전하다
        [TestCase(0f, true)]
        [TestCase(90f, true)]
        [TestCase(-90f, true)]
        [TestCase(157.4f, true)]
        [TestCase(-157.4f, true)]
        [TestCase(HALF_ARC, true)]
        [TestCase(-HALF_ARC, true)]
        [TestCase(157.6f, false)]
        [TestCase(-157.6f, false)]
        [TestCase(180f, false)]
        public void Strike_ByAngleFromTheSavedForward_HitsOnlyOutsideTheBackWedge(float degrees, bool isHit)
        {
            Rig rig = new Rig();
            BossTestRigCombatant target = rig.Add(Polar(degrees, 10f));

            rig.Cast();

            Assert.AreEqual(isHit, target.Health < PLAYER_HEALTH, $"각도 {degrees}");
            Assert.AreEqual(isHit ? 1 : 0, rig.Recorder.LastHitCount);
        }

        [Test]
        public void Strike_BackWedgeIs45DegreesWide_AndMirrorsOnBothSides()
        {
            Rig rig = new Rig();
            BossTestRigCombatant left  = rig.Add(Polar(-170f, 10f));
            BossTestRigCombatant right = rig.Add(Polar(170f, 10f));
            BossTestRigCombatant edge  = rig.Add(Polar(157.4f, 10f));

            rig.Cast();

            Assert.AreEqual(PLAYER_HEALTH, left.Health);
            Assert.AreEqual(PLAYER_HEALTH, right.Health);
            Assert.Less(edge.Health, PLAYER_HEALTH);
        }

        [TestCase(43.9f, true)]
        [TestCase(44.1f, false)]
        public void Strike_ByDistance_RadiusIs44Metres(float distance, bool isHit)
        {
            Rig rig = new Rig();
            BossTestRigCombatant target = rig.Add(Polar(0f, distance));

            rig.Cast();

            Assert.AreEqual(isHit, target.Health < PLAYER_HEALTH);
        }

        [Test]
        public void Strike_TargetOnTheBossPosition_IsHitAndPushedAlongTheSavedForward()
        {
            Rig rig = new Rig();
            BossTestRigCombatant target = rig.Add(BossAt);

            rig.Cast();

            Assert.Less(target.Health, PLAYER_HEALTH);
            Assert.AreEqual(1f, Vector3.Distance(BossAt, target.Position), POSITION_TOLERANCE);
            Assert.AreEqual(1f, target.Position.z, POSITION_TOLERANCE, "방향이 없어 저장한 정면으로 민다");
        }

        /****************************************
        *            Damage Rules
        ****************************************/

        [Test]
        public void Strike_PlayerCompanionAndSummon_EachTakeOneIdenticalHit()
        {
            Rig rig = new Rig();
            BossTestRigCombatant player    = rig.Add(Polar(30f, 8f));
            BossTestRigCombatant companion = rig.Add(Polar(-30f, 8f), isSummon: true);
            BossTestRigCombatant summon    = rig.Add(Polar(60f, 12f), isSummon: true);

            rig.Cast();
            rig.Pattern.EnterState(EBossAttackState.Execute, TELEGRAPH_END); // 같은 시전을 한 번 더 부른다
            rig.Pattern.Tick(EBossAttackState.Recover, TELEGRAPH_END + 1.0);

            float expected = ExpectedDamage(DEFENSE);
            Assert.AreEqual(PLAYER_HEALTH - expected, player.Health, TOLERANCE);
            Assert.AreEqual(PLAYER_HEALTH - expected, companion.Health, TOLERANCE);
            Assert.AreEqual(PLAYER_HEALTH - expected, summon.Health, TOLERANCE);
            Assert.AreEqual(1, rig.Recorder.HitEventCount, "타격은 시전당 한 번이다");
            Assert.AreEqual(3, rig.Recorder.LastHitCount);
        }

        [Test]
        public void Strike_DifferentDefence_ChangesOnlyTheFinalDamage()
        {
            Rig rig = new Rig();

            // 같은 방어 전 피해를 모든 대상에 보내고 최종 피해만 방어력이 가른다
            var weak = rig.Add(Polar(20f, 8f));
            var tough = new BossTestRigCombatant(rig.Registry.IssueCombatId(), Polar(-20f, 8f), PLAYER_HEALTH, 400f, 0f);
            rig.Registry.Register(tough);
            rig.Displacer.Register(tough);

            rig.Cast();

            Assert.AreEqual(PLAYER_HEALTH - ExpectedDamage(DEFENSE), weak.Health, TOLERANCE);
            Assert.AreEqual(PLAYER_HEALTH - ExpectedDamage(400f), tough.Health, TOLERANCE);
            Assert.Less(weak.Health, tough.Health);
        }

        [Test]
        public void Strike_RequestsAreUnblockableAndRawDamageIsTheSameForEveryTarget()
        {
            var resolver = new ScriptedResolver();
            var query    = new Phase2Query();
            var self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = new Vector3(2f, 0f, 3f) };
            var player   = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(2f, 0f, 13f) };
            var summon   = new FakeCombatTarget { CombatId = 11, Faction = ECombatFaction.Player, IsSummon = true, OwnerCombatId = 10, Position = new Vector3(5f, 0f, 9f) };
            query.Players.Add(player);
            query.Players.Add(summon);

            var gate = new BossDamageGate(resolver);
            gate.Open();
            var pattern = new MucheolBattlefieldBlastPattern(TEST_HREF, new MucheolBattlefieldSpec(0f), new BossTestRigDisplacer(new FixedWall()));
            var context = new BossPatternContext(MakeP07(), 7, 10, self, query, gate);

            pattern.Begin(in context, 0.0);
            pattern.EnterState(EBossAttackState.Execute, TELEGRAPH_END);

            Assert.AreEqual(2, resolver.Requests.Count);
            for (int i = 0; i < resolver.Requests.Count; ++i)
            {
                DamageRequest request = resolver.Requests[i];
                Assert.IsTrue(request.IsUnblockable);
                Assert.AreEqual(0.30f * TEST_HREF, request.RawDamage, POSITION_TOLERANCE);
                Assert.AreEqual(new Vector3(2f, 0f, 3f), request.SourcePosition);
                Assert.AreEqual("mucheol_ph2_p07", request.SourceDefinitionId);
                Assert.AreEqual(resolver.Requests[0].ActionInstanceId, request.ActionInstanceId, "한 동작 번호를 공유한다");
            }
        }

        [Test]
        public void Strike_MaxHealthTargetThatDiesFromTheHit_IsStillPushed()
        {
            Rig rig = new Rig();
            BossTestRigCombatant companion = rig.Add(Polar(0f, 6f), 900f, isSummon: true); // 966 피해보다 체력이 낮다
            BossTestRigCombatant player    = rig.Add(Polar(40f, 6f));

            rig.Cast();

            Assert.AreEqual(EParticipantLifeState.Dead, companion.LifeState);
            Assert.AreEqual(EParticipantLifeState.Alive, player.LifeState);
            Assert.AreEqual(7f, companion.Position.z, POSITION_TOLERANCE, "쓰러뜨린 피해에도 밀림을 시도한다");
            Assert.AreEqual(1f, rig.Recorder.Pushed[companion.CombatId], POSITION_TOLERANCE);
            Assert.AreEqual(2, rig.Recorder.LastHitCount);
        }

        /****************************************
        *               Push
        ****************************************/

        [Test]
        public void Push_MovesTheHitTargetOneMetreAwayFromTheSavedOrigin()
        {
            Rig rig = new Rig();
            Vector3 start = Polar(45f, 10f);
            BossTestRigCombatant target = rig.Add(start);

            rig.Cast();

            Assert.AreEqual(11f, Vector3.Distance(BossAt, target.Position), POSITION_TOLERANCE);
            Assert.AreEqual(start.normalized.x * 11f, target.Position.x, POSITION_TOLERANCE);
            Assert.AreEqual(start.normalized.z * 11f, target.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(1f, rig.Recorder.Pushed[target.CombatId], POSITION_TOLERANCE);
        }

        [Test]
        public void Push_NotAppliedForTargetsOutsideTheZone()
        {
            Rig rig = new Rig();
            Vector3 start = Polar(180f, 10f);
            BossTestRigCombatant target = rig.Add(start);

            rig.Cast();

            Assert.AreEqual(start, target.Position);
            Assert.IsFalse(rig.Recorder.Pushed.ContainsKey(target.CombatId));
        }

        [Test]
        public void Push_StopsAtTheArenaBoundary_AndStaysPutWhenAlreadyBeyondIt()
        {
            Rig rig = new Rig();
            BossTestRigCombatant nearEdge = rig.Add(Polar(60f, 21.5f));     // 경계 22m까지 0.5m
            BossTestRigCombatant beyond   = rig.Add(Polar(-60f, 23f));      // 이미 경계 밖

            rig.Cast();

            Assert.AreEqual(0.5f, rig.Recorder.Pushed[nearEdge.CombatId], 0.01f);
            Assert.AreEqual(ARENA_RADIUS, nearEdge.Position.magnitude, 0.01f, "경계를 넘지 않는다");
            Assert.AreEqual(0f, rig.Recorder.Pushed[beyond.CombatId], POSITION_TOLERANCE);
            Assert.AreEqual(23f, beyond.Position.magnitude, POSITION_TOLERANCE);
        }

        [Test]
        public void Push_StopsInFrontOfAWall()
        {
            var wall = new FixedWall { MaxClear = 0.3f };
            Rig rig = new Rig(walls: wall);
            BossTestRigCombatant target = rig.Add(Polar(0f, 6f));

            rig.Cast();

            Assert.AreEqual(0.3f, rig.Recorder.Pushed[target.CombatId], POSITION_TOLERANCE);
            Assert.AreEqual(6.3f, target.Position.z, POSITION_TOLERANCE);
        }

        [Test]
        public void Push_ZeroDistanceSpec_DoesNotMoveOrReport()
        {
            Rig rig = new Rig(pushDistance: 0f);
            BossTestRigCombatant target = rig.Add(Polar(0f, 6f));

            rig.Cast();

            Assert.Less(target.Health, PLAYER_HEALTH);
            Assert.AreEqual(Polar(0f, 6f), target.Position);
            Assert.AreEqual(0, rig.Recorder.Pushed.Count);
        }

        [Test]
        public void Push_OnlyForTargetsWhoseDamageWasActuallyApplied()
        {
            var resolver = new ScriptedResolver { Result = DamageResult.Ignored(EDamageOutcome.Invulnerable) };
            var query    = new Phase2Query();
            var self     = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            var player   = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = Polar(0f, 8f) };
            query.Players.Add(player);

            var gate = new BossDamageGate(resolver);
            gate.Open();
            var recorder  = new BattlefieldRecorder();
            var pattern   = new MucheolBattlefieldBlastPattern(TEST_HREF, new MucheolBattlefieldSpec(1f), new BossTestRigDisplacer(new FixedWall()), recorder);
            var context   = new BossPatternContext(MakeP07(), 1, 10, self, query, gate);

            pattern.Begin(in context, 0.0);
            pattern.EnterState(EBossAttackState.Execute, TELEGRAPH_END);

            Assert.AreEqual(1, resolver.Requests.Count);
            Assert.AreEqual(0, recorder.LastHitCount);
            Assert.AreEqual(0, recorder.Pushed.Count);
        }

        /****************************************
        *            Arena Position
        ****************************************/

        [Test]
        public void Cast_AtTheArenaCentre_HitsTheWholeArenaExceptTheBackWedge()
        {
            Rig rig = new Rig();
            BossTestRigCombatant front = rig.Add(Polar(0f, 20f));
            BossTestRigCombatant back  = rig.Add(Polar(180f, 20f));

            rig.Cast();

            Assert.Less(front.Health, PLAYER_HEALTH);
            Assert.AreEqual(PLAYER_HEALTH, back.Health);
        }

        // 보스가 가장자리에서 안쪽을 보면 등 쪽 안전지대는 전장 밖이다. 그래도 시전은 취소되거나 옮겨지지 않고 전장 안은 모두 맞는다
        [Test]
        public void Cast_WhereTheSafeWedgeIsOutsideTheArena_IsNotCancelledOrMovedAndHitsEveryoneInside()
        {
            Vector3 edge = new Vector3(ARENA_RADIUS - BOSS_RADIUS - 0.1f, 0f, 0f);
            Rig rig = new Rig(bossPosition: edge, bossForward: Vector3.left);
            BossTestRigCombatant center = rig.Add(Vector3.zero);
            BossTestRigCombatant far    = rig.Add(new Vector3(-20f, 0f, 0f));
            BossTestRigCombatant side   = rig.Add(new Vector3(edge.x, 0f, 15f));

            rig.Cast();

            Assert.AreEqual(0, rig.Recorder.Cancels.Count);
            Assert.AreEqual(edge, rig.Pattern.Origin, "중앙으로 옮기지 않는다");
            Assert.Less(center.Health, PLAYER_HEALTH);
            Assert.Less(far.Health, PLAYER_HEALTH, "맵 반대편까지 닿는다");
            Assert.Less(side.Health, PLAYER_HEALTH);
            Assert.AreEqual(3, rig.Recorder.LastHitCount);
        }

        [Test]
        public void Cast_AtTheEdgeFacingOutward_PutsTheSafeWedgeInsideTheArena()
        {
            Vector3 edge = new Vector3(ARENA_RADIUS - BOSS_RADIUS - 0.1f, 0f, 0f);
            Rig rig = new Rig(bossPosition: edge, bossForward: Vector3.right);
            BossTestRigCombatant inWedge = rig.Add(new Vector3(edge.x - 10f, 0f, 0f));
            BossTestRigCombatant outside = rig.Add(new Vector3(edge.x - 10f, 0f, 10f));

            rig.Cast();

            Assert.AreEqual(PLAYER_HEALTH, inWedge.Health);
            Assert.Less(outside.Health, PLAYER_HEALTH);
        }

        /****************************************
        *            Cancel And Gate
        ****************************************/

        [Test]
        public void Cancel_BeforeTheStrike_NoDamageAndNoPush()
        {
            Rig rig = new Rig();
            BossTestRigCombatant target = rig.Add(Polar(0f, 6f));

            rig.Begin();
            rig.Pattern.Cancel(EBossInterruptReason.Death);
            rig.Strike();

            Assert.AreEqual(PLAYER_HEALTH, target.Health);
            Assert.AreEqual(Polar(0f, 6f), target.Position);
            Assert.AreEqual(1, rig.Recorder.Cancels.Count);
            Assert.AreEqual(0, rig.Recorder.HitEventCount);
        }

        [Test]
        public void ClosedGate_NoDamageAndNoPush()
        {
            Rig rig = new Rig();
            BossTestRigCombatant target = rig.Add(Polar(0f, 6f));

            rig.Begin();
            rig.Gate.CloseForGood();
            rig.Strike();

            Assert.AreEqual(PLAYER_HEALTH, target.Health);
            Assert.AreEqual(Polar(0f, 6f), target.Position);
            Assert.AreEqual(0, rig.Recorder.Pushed.Count);
        }

        /****************************************
        *               Factory
        ****************************************/

        [Test]
        public void Factory_P07_NeedsTheSpecAndTheDisplacer()
        {
            var vitals   = new FakeBossVitals();
            var catalog  = new MucheolDevCatalog(ARENA, true, true);
            PatternDefinition p07 = catalog.Find(MucheolPhase2PatternFactory.P07_ID);
            MucheolPhase2Spec full = MucheolDevCatalog.CreatePhaseTwoSpec();
            var displacer = new BossTestRigDisplacer(new FixedWall());

            var withBoth     = new MucheolPhase2PatternFactory(TEST_HREF, full, vitals, catalog, null, null, null, displacer);
            var noDisplacer  = new MucheolPhase2PatternFactory(TEST_HREF, full, vitals, catalog);
            var noSpec       = new MucheolPhase2PatternFactory(TEST_HREF, new MucheolPhase2Spec(full.P01, full.P02, full.P03, full.P04, full.P05, full.P06),
                vitals, catalog, null, null, null, displacer);

            Assert.IsTrue(withBoth.CanCreate(p07));
            Assert.IsInstanceOf<MucheolBattlefieldBlastPattern>(withBoth.Create(p07));
            Assert.IsFalse(noDisplacer.CanCreate(p07), "밀림 창구가 없으면 밀림 규칙이 빠진 채 시전되므로 만들지 않는다");
            Assert.IsNull(noDisplacer.Create(p07));
            Assert.IsFalse(noSpec.CanCreate(p07));
            Assert.IsNull(noSpec.Create(p07));
        }
    }
}
