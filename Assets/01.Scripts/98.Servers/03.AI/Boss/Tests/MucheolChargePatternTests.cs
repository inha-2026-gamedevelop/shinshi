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
    // 무철 P06(뿔 들이받기) 실행 검증, 시작 수치는 플레이 테스트 전 임시값이라 운영 카탈로그가 아니라 이 테스트의 데이터를 쓴다
    // 가짜 이동 창구와 가짜 지형(z축 방향 벽 평면), 가짜 구조물(z축 방향 평면, 좌우 폭 없음)로 팩토리가 만든 패턴을 확인한다.
    // 이 테스트는 서버 틱, 실제 NavMesh와 물리 벽, 실제 구조물 탐지와 파괴, 복제, 표현을 거치지 않는다.
    // 공용 공격 FSM은 충돌 회복을 아직 읽지 않는다. 그 사실은 실제 FSM으로, 제안하는 FSM 변경의 수용 기준은 이 파일 안의 가짜 FSM으로 따로 확인한다
    public class MucheolChargePatternTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TEST_HREF   = 3541f; // 테스트가 명시적으로 주입하는 값이다
        private const float  BODY_RADIUS = 2f;
        private const float  POSITION_TOLERANCE = 0.002f;
        private const float  ANGLE_TOLERANCE    = 0.05f;
        private const float  STEP        = 0.01f;
        private const double TIME_MARGIN = 0.1;
        private const int    PROBE_ID    = 11;
        private const int    STRUCTURE_A = 7;
        private const int    STRUCTURE_B = 9;

        // 임시 시작 수치
        private const float WEIGHT       = 10f;
        private const float RANGE        = 18f;
        private const float WIDTH        = 3f;
        private const float TELEGRAPH    = 1.6f;
        private const float TRACKING     = 0.6f;
        private const float TURN_RATE    = 90f;
        private const float ACTIVE       = 0.75f;
        private const float RECOVERY     = 1.5f;
        private const float COLLISION_RECOVERY = 2.5f;
        private const float DAMAGE_REF   = 0.40f;

        private static readonly Vector3 Origin = Vector3.zero;

        /****************************************
        *               Helpers
        ****************************************/

        private static PatternDefinition MakeP06()
        {
            return new PatternDefinition(MucheolPatternFactory.P06_ID, MucheolPatternFactory.BOSS_ID, 1, WEIGHT,
                new[] { 0f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                TELEGRAPH, TRACKING, TURN_RATE, ACTIVE, RECOVERY, EnemyCatalogSO.AttackShapeSpec.Dash(RANGE, WIDTH),
                DAMAGE_REF, false);
        }

        private static float AimDegrees(Vector3 aim)
        {
            return Vector3.SignedAngle(Vector3.forward, aim, Vector3.up);
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

        // FSM이 단계 경계를 만드는 식과 같다(앞 경계에 double로 더함)
        private static double ExecuteStart => (double)TELEGRAPH;
        private static double ExecuteEnd   => ExecuteStart + ACTIVE;

        // 벽은 z = WallZ 평면이다. 몸체 반경만큼 앞에서 멈춘다
        private sealed class ChargeMovement : IBossMovement
        {
            public FakeCombatTarget Self;
            public float            WallZ       = float.PositiveInfinity; // 예고도 아는 벽
            public float            HiddenWallZ = float.PositiveInfinity; // 이동할 때만 막는 벽, 거리 측정 뒤에 생긴 장애물 모사
            public int              MoveCalls;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                return Mathf.Clamp(LimitAlong(WallZ, origin, direction, bodyRadius, maxDistance), 0f, maxDistance);
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                ++MoveCalls;

                Vector3 delta    = to - from;
                float   distance = delta.magnitude;
                Vector3 reached  = to;

                if (distance > 0f)
                {
                    Vector3 direction = delta / distance;
                    float   allowed   = Mathf.Min(
                        LimitAlong(WallZ, from, direction, bodyRadius, distance),
                        LimitAlong(HiddenWallZ, from, direction, bodyRadius, distance));
                    reached = from + (direction * Mathf.Clamp(allowed, 0f, distance));
                }

                Self.Position = reached;
                return reached;
            }

            private static float LimitAlong(float wallZ, Vector3 origin, Vector3 direction, float bodyRadius, float maxDistance)
            {
                if ((float.IsPositiveInfinity(wallZ)) || (direction.z <= 0.0001f))
                {
                    return maxDistance;
                }

                return (wallZ - bodyRadius - origin.z) / direction.z;
            }
        }

        // 구조물은 z 평면이다. 같은 경로를 여러 번 물으면 같은 구조물을 반복해 돌려주는 무상태 조회이고, 가장 가까운 구조물만 돌려준다
        private sealed class FakeStructures : IBossChargeStructureQuery
        {
            private readonly List<KeyValuePair<int, float>> _structures = new List<KeyValuePair<int, float>>();

            public bool Enabled = true;
            public int  QueryCalls;

            public void Add(int id, float z)
            {
                _structures.Add(new KeyValuePair<int, float>(id, z));
            }

            public bool TryGetFirstStructure(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius,
                out BossChargeStructureHit hit)
            {
                ++QueryCalls;
                hit = default;

                if ((!Enabled) || (direction.z <= 0.0001f))
                {
                    return false;
                }

                bool  found    = false;
                float bestDist = float.PositiveInfinity;
                int   bestId   = 0;

                for (int i = 0; i < _structures.Count; ++i)
                {
                    float distance = (_structures[i].Value - bodyRadius - origin.z) / direction.z;
                    if ((distance >= 0f) && (distance <= maxDistance) && (distance < bestDist))
                    {
                        found    = true;
                        bestDist = distance;
                        bestId   = _structures[i].Key;
                    }
                }

                if (found)
                {
                    hit = new BossChargeStructureHit(bestId, bestDist);
                }

                return found;
            }
        }

        private sealed class ChargeQuery : ICombatTargetQuery
        {
            public List<FakeCombatTarget> Targets { get; } = new List<FakeCombatTarget>();

            public int GetLivingPlayers(List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if ((Targets[i].LifeState == EParticipantLifeState.Alive) && (!Targets[i].IsSummon))
                    {
                        results.Add(Targets[i]);
                    }
                }

                return results.Count;
            }

            public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
                List<ICombatTarget> results)
            {
                results.Clear();
                for (int i = 0; i < Targets.Count; ++i)
                {
                    FakeCombatTarget candidate = Targets[i];
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
                for (int i = 0; i < Targets.Count; ++i)
                {
                    if (Targets[i].CombatId == combatId)
                    {
                        target = Targets[i];
                        return true;
                    }
                }

                target = null;
                return false;
            }
        }

        private sealed class ChargeDashListener : IBossDashTelegraphListener
        {
            public int   StartCount;
            public float StartLength;
            public float StartWidth;
            public int   LockCount;
            public int   AimChangeCount;
            public Vector3 TelegraphDirection;
            public Vector3 LockDirection;
            public Vector3 DashDirection;
            public int   DashStartCount;
            public float DashDistance;
            public float DashSeconds;
            public float DashSpeed;
            public int   HitTotal;
            public List<EBossInterruptReason> Cancels = new List<EBossInterruptReason>();

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
                float telegraphSeconds)
            {
                ++StartCount;
                StartLength = length;
                StartWidth  = width;
                TelegraphDirection = direction;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction, float length)
            {
                ++AimChangeCount;
            }

            public void OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
            {
                ++LockCount;
                LockDirection = direction;
            }

            public void OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction, float distance, float seconds, float speed,
                double startServerTime)
            {
                ++DashStartCount;
                DashDirection = direction;
                DashDistance = distance;
                DashSeconds  = seconds;
                DashSpeed    = speed;
            }

            public void OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall)
            {
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                HitTotal += hitCount;
            }

            public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
            {
                Cancels.Add(reason);
            }
        }

        private sealed class OutcomeListener : IBossChargeOutcomeListener
        {
            public List<EBossChargeEnd>      Ends       = new List<EBossChargeEnd>();
            public List<BossChargeCollision> Collisions = new List<BossChargeCollision>();
            public List<float>               Recoveries = new List<float>();
            public List<int>                 CastIds    = new List<int>();

            public void OnChargeEnded(int castInstanceId, EBossChargeEnd end, BossChargeCollision collision, float recoverySeconds)
            {
                CastIds.Add(castInstanceId);
                Ends.Add(end);
                Collisions.Add(collision);
                Recoveries.Add(recoverySeconds);
            }
        }

        // 팩토리가 만든 패턴을 공격 FSM에 올린 최소 구성. 보스는 원점에서 +z를 보고 추적 대상은 앞 3m에 선다
        private sealed class Rig
        {
            public FakeCombatTarget    Self       { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy, Position = Origin, BodyRadius = BODY_RADIUS };
            public FakeCombatTarget    Tracked    { get; } = new FakeCombatTarget { CombatId = 10, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public ChargeQuery         Query      { get; } = new ChargeQuery();
            public FakeDamageResolver  Resolver   { get; } = new FakeDamageResolver();
            public ChargeDashListener  Dash       { get; } = new ChargeDashListener();
            public OutcomeListener     Outcome    { get; } = new OutcomeListener();
            public ChargeMovement      Movement   { get; } = new ChargeMovement();
            public FakeStructures      Structures { get; } = new FakeStructures();
            public BossAttackFsm       Fsm        { get; } = new BossAttackFsm();
            public BossDamageGate      Gate       { get; }
            public MucheolChargePattern Pattern   { get; }
            public PatternDefinition   Definition { get; }
            public BossPatternContext  Context    { get; }

            public Rig()
            {
                Query.Targets.Add(Tracked);
                Movement.Self = Self;
                Gate          = new BossDamageGate(Resolver);
                Gate.Open();
                Definition = MakeP06();
                Pattern    = (MucheolChargePattern)new MucheolPatternFactory(TEST_HREF, null, Movement, Dash, null, null, null,
                    new MucheolChargeSpec(COLLISION_RECOVERY), Structures, Outcome).Create(Definition);
                Context    = new BossPatternContext(Definition, Gate.IssueInstanceId(), Tracked.CombatId, Self, Query, Gate);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 position)
            {
                FakeCombatTarget player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = position };
                Query.Targets.Add(player);
                return player;
            }

            public void Start(double serverTime = 0.0)
            {
                BossPatternContext context = Context;
                Assert.IsTrue(Fsm.Start(Definition, Pattern, in context, serverTime));
            }

            // from 다음 틱부터 to까지 일정 간격으로 틱한다
            public void TickRange(double from, double to)
            {
                int count = (int)Math.Round((to - from) / STEP);
                for (int i = 1; i <= count; ++i)
                {
                    Fsm.Tick(from + (i * (double)STEP));
                }
            }
        }

        // 제안하는 FSM 변경의 수용 기준을 확인하는 가짜 FSM, 실제 BossAttackFsm이 아니다.
        // 실제와 다른 점은 실행 종료 경계에서 회복 단계에 들어간 뒤(EnterState 호출 뒤)에 패턴이 정한 회복 시간을 읽어 단계 끝을 정하는 것뿐이다
        private sealed class ProposedRecoveryFsm
        {
            private const int MAX_TRANSITIONS_PER_TICK = 4;

            private PatternDefinition _definition;
            private IBossPattern      _pattern;
            private double            _stateEnd;

            public EBossAttackState State { get; private set; } = EBossAttackState.Ready;

            public void Start(PatternDefinition definition, IBossPattern pattern, in BossPatternContext context, double serverTime)
            {
                _definition = definition;
                _pattern    = pattern;
                State       = EBossAttackState.Telegraph;
                _stateEnd   = serverTime + definition.TelegraphSeconds;
                _pattern.Begin(in context, serverTime);
            }

            public void Tick(double serverTime)
            {
                for (int i = 0; (i < MAX_TRANSITIONS_PER_TICK) && (State != EBossAttackState.Ready); ++i)
                {
                    _pattern.Tick(State, serverTime);
                    if (serverTime < _stateEnd)
                    {
                        return;
                    }

                    Advance();
                }
            }

            private void Advance()
            {
                double boundary = _stateEnd;

                switch (State)
                {
                    case EBossAttackState.Telegraph:
                        State     = EBossAttackState.Execute;
                        _stateEnd = boundary + _definition.ActiveSeconds;
                        _pattern.EnterState(State, boundary);
                        break;
                    case EBossAttackState.Execute:
                        State = EBossAttackState.Recover;
                        _pattern.EnterState(State, boundary);
                        float recovery = (_pattern is IBossRecoveryDuration provider)
                            ? provider.GetRecoverySeconds(_definition.RecoverySeconds)
                            : _definition.RecoverySeconds;
                        _stateEnd = boundary + recovery;
                        break;
                    case EBossAttackState.Recover:
                        IBossPattern pattern = _pattern;
                        State       = EBossAttackState.Ready;
                        _definition = null;
                        _pattern    = null;
                        pattern.Finish();
                        break;
                    default:
                        break;
                }
            }
        }

        /****************************************
        *               Factory
        ****************************************/

        [Test]
        public void Create_P06WithMovementAndSpec_ReturnsChargePattern()
        {
            Rig rig = new Rig();
            var factory = new MucheolPatternFactory(TEST_HREF, null, rig.Movement, null, null, null, null,
                new MucheolChargeSpec(COLLISION_RECOVERY));

            Assert.IsTrue(factory.CanCreate(rig.Definition));
            Assert.IsInstanceOf<MucheolChargePattern>(factory.Create(rig.Definition));
        }

        [Test]
        public void Create_P06WithoutMovementOrSpec_CannotCreateAndReturnsNull()
        {
            Rig rig = new Rig();
            var noMovement = new MucheolPatternFactory(TEST_HREF, null, null, null, null, null, null,
                new MucheolChargeSpec(COLLISION_RECOVERY));
            var noSpec = new MucheolPatternFactory(TEST_HREF, null, rig.Movement);
            var neither = new MucheolPatternFactory(TEST_HREF);

            Assert.IsFalse(noMovement.CanCreate(rig.Definition));
            Assert.IsNull(noMovement.Create(rig.Definition));
            Assert.IsFalse(noSpec.CanCreate(rig.Definition));
            Assert.IsNull(noSpec.Create(rig.Definition));
            Assert.IsFalse(neither.CanCreate(rig.Definition));
            Assert.IsNull(neither.Create(rig.Definition));
        }

        // P06 인자를 모두 넘겨도 P01~P05의 종류와 생성 조건은 그대로다
        [Test]
        public void Create_OtherPatterns_KeepTheirTypesWithChargeArguments()
        {
            Rig rig = new Rig();
            MucheolComboSpec combo = new MucheolComboSpec(new MucheolComboHit(5f, 120f, 0.14f, 0f));
            var full = new MucheolPatternFactory(TEST_HREF, null, rig.Movement, null, null, combo, null,
                new MucheolChargeSpec(COLLISION_RECOVERY), rig.Structures, rig.Outcome);
            var bare = new MucheolPatternFactory(TEST_HREF);
            PatternDefinition p01 = new PatternDefinition(MucheolPatternFactory.P01_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);
            PatternDefinition p02 = new PatternDefinition(MucheolPatternFactory.P02_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);
            PatternDefinition p03 = new PatternDefinition(MucheolPatternFactory.P03_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Dash, true);
            PatternDefinition p04 = new PatternDefinition(MucheolPatternFactory.P04_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Circle, true);
            PatternDefinition p05 = new PatternDefinition(MucheolPatternFactory.P05_ID, MucheolPatternFactory.BOSS_ID, 1, EEnemyAttackShape.Fan, true);

            Assert.IsInstanceOf<BossFanBlastPattern>(full.Create(p01));
            Assert.IsInstanceOf<BossFanBlastPattern>(full.Create(p02));
            Assert.IsInstanceOf<BossDashPattern>(full.Create(p03));
            Assert.IsInstanceOf<BossCircleBlastPattern>(full.Create(p04));
            Assert.IsInstanceOf<MucheolComboPattern>(full.Create(p05));

            Assert.IsInstanceOf<BossFanBlastPattern>(bare.Create(p01));
            Assert.IsInstanceOf<BossFanBlastPattern>(bare.Create(p02));
            Assert.IsNull(bare.Create(p03));
            Assert.IsInstanceOf<BossCircleBlastPattern>(bare.Create(p04));
            Assert.IsNull(bare.Create(p05));
        }

        [Test]
        public void Spec_NonPositiveOrNonFiniteRecovery_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeSpec(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeSpec(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeSpec(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MucheolChargeSpec(float.PositiveInfinity));
            Assert.AreEqual(COLLISION_RECOVERY, new MucheolChargeSpec(COLLISION_RECOVERY).CollisionRecoverySeconds, 0.0001f);
        }

        /****************************************
        *            Distance And Width
        ****************************************/

        [Test]
        public void Begin_ReportsDefinitionLengthAndWidth()
        {
            Rig rig = new Rig();
            rig.Start();

            Assert.AreEqual(1, rig.Dash.StartCount);
            Assert.AreEqual(RANGE, rig.Dash.StartLength, POSITION_TOLERANCE);
            Assert.AreEqual(WIDTH, rig.Dash.StartWidth, POSITION_TOLERANCE);
        }

        [Test]
        public void Tick_ThroughExecute_MovesBossEighteenMetersAndEndsNormally()
        {
            Rig rig = new Rig();
            rig.Start();

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(RANGE, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(1, rig.Dash.DashStartCount);
            Assert.AreEqual(RANGE, rig.Dash.DashDistance, POSITION_TOLERANCE);
            Assert.AreEqual(RANGE / ACTIVE, rig.Dash.DashSpeed, 0.01f);
            Assert.AreEqual(EBossChargeEnd.Normal, rig.Pattern.EndKind);
            Assert.AreEqual(EBossChargeObstacleKind.None, rig.Pattern.Collision.Kind);
            Assert.AreEqual(RECOVERY, rig.Pattern.RecoverySeconds, 0.0001f);
            Assert.AreEqual(RECOVERY, rig.Pattern.GetRecoverySeconds(RECOVERY), 0.0001f);
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
            Assert.AreEqual(EBossChargeEnd.Normal, rig.Outcome.Ends[0]);
            Assert.AreEqual(rig.Context.CastInstanceId, rig.Outcome.CastIds[0]);
        }

        // 판정 폭 3m라 중심선에서 좌우 1.5m까지다, 시작점 뒤와 끝점 너머는 맞지 않는다. 방어 불가라 피해 플래그가 Unblockable이다
        [Test]
        public void Tick_ThroughExecute_HitsOnlyInsideWidthAndLengthOncePerTarget()
        {
            Rig rig = new Rig();
            FakeCombatTarget insideRight = rig.AddPlayer(PROBE_ID, new Vector3(1.2f, 0f, 6f));
            FakeCombatTarget insideLeft  = rig.AddPlayer(PROBE_ID + 1, new Vector3(-1.2f, 0f, 12f));
            FakeCombatTarget tooWide     = rig.AddPlayer(PROBE_ID + 2, new Vector3(2f, 0f, 6f));
            FakeCombatTarget behind      = rig.AddPlayer(PROBE_ID + 3, new Vector3(0f, 0f, -1f));
            FakeCombatTarget beyondEnd   = rig.AddPlayer(PROBE_ID + 4, new Vector3(0f, 0f, RANGE + 1f));
            rig.Start();

            rig.TickRange(0.0, ExecuteEnd + 3.0);

            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, insideRight.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, insideLeft.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, tooWide.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, behind.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, beyondEnd.CombatId));
            Assert.AreEqual(3, rig.Dash.HitTotal);

            DamageRequest request = rig.Resolver.Requests[0];
            Assert.AreEqual(MucheolPatternFactory.P06_ID, request.SourceDefinitionId);
            Assert.AreEqual(DAMAGE_REF * TEST_HREF, request.RawDamage, 0.001f);
            Assert.AreEqual(EDamageFlags.Unblockable, request.Flags);
            Assert.AreEqual(rig.Context.CastInstanceId, request.CastInstanceId);
            Assert.IsTrue(request.HasValidIds);
        }

        /****************************************
        *            Direction Lock
        ****************************************/

        // 이지 조준: 예고를 시작하는 순간 대상 쪽을 보고, 각도 제한 없이 고정 시각(0.6초)까지 따라간 뒤 고정한다
        [Test]
        public void Tick_TargetOnTheSide_AimsAtOnceWithoutTurnLimitAndLocksAfterTrackingEnd()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();

            Assert.AreEqual(90f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE, "정의의 회전 속도 x 시간(54도)이 아니라 바로 90도다");
            Assert.AreEqual(90f, AimDegrees(rig.Dash.TelegraphDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(0, rig.Dash.AimChangeCount, "예고 시작 알림이 방향을 싣는다");

            rig.TickRange(0.0, 0.3);
            Assert.AreEqual(90f, AimDegrees(rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.IsFalse(rig.Pattern.IsDirectionLocked);

            rig.TickRange(0.3, 0.8);
            Assert.IsTrue(rig.Pattern.IsDirectionLocked);
            Assert.AreEqual(90f, AimDegrees(rig.Dash.LockDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(1, rig.Dash.LockCount);
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(-90f)]
        [TestCase(135f)]
        [TestCase(180f)]
        [TestCase(-150f)]
        public void Tick_PlayerAnywhere_TelegraphLockMovementAndHitsFollowTheSameDirection(float degrees)
        {
            Rig rig = new Rig();
            Vector3 direction = Phase2Helpers.Polar(degrees, 1f);
            Vector3 right = new Vector3(direction.z, 0f, -direction.x);
            rig.Tracked.Position = direction * 30f;
            FakeCombatTarget inside  = rig.AddPlayer(PROBE_ID, (direction * 9f) + (right * 1.4f));  // 폭의 절반(1.5m) 안
            FakeCombatTarget outside = rig.AddPlayer(PROBE_ID + 1, (direction * 9f) + (right * 1.6f));
            rig.Start();

            Assert.AreEqual(0f, Vector3.Angle(direction, rig.Dash.TelegraphDirection), ANGLE_TOLERANCE, "예고 시작 방향");

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(0f, Vector3.Angle(direction, rig.Dash.LockDirection), ANGLE_TOLERANCE, "고정 방향");
            Assert.AreEqual(0f, Vector3.Angle(direction, rig.Dash.DashDirection), ANGLE_TOLERANCE, "돌진 시작 방향");
            Assert.AreEqual(0f, Vector3.Angle(direction, rig.Self.Position), ANGLE_TOLERANCE, "실제 이동 방향");
            Assert.AreEqual(RANGE, rig.Self.Position.magnitude, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeEnd.Normal, rig.Pattern.EndKind);
            Assert.AreEqual(1, CountHits(rig.Resolver, inside.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, outside.CombatId));
        }

        [Test]
        public void Tick_TargetMovesBeforeLock_FollowsItAndThenKeepsTheLockedDirection()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, -10f);
            rig.Start();
            Assert.AreEqual(180f, Mathf.Abs(AimDegrees(rig.Pattern.AimDirection)), ANGLE_TOLERANCE);

            rig.TickRange(0.0, 0.3);
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);  // 고정 전에는 같은 대상을 따라간다
            rig.TickRange(0.3, 0.8);
            Assert.AreEqual(90f, AimDegrees(rig.Dash.LockDirection), ANGLE_TOLERANCE);

            rig.Tracked.Position = new Vector3(-10f, 0f, 0f);  // 고정 뒤에는 따라가지 않는다
            rig.TickRange(0.8, ExecuteEnd + 1.0);

            Assert.AreEqual(90f, AimDegrees(rig.Dash.DashDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(RANGE, rig.Self.Position.x, POSITION_TOLERANCE);
            Assert.AreEqual(0f, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        [Test]
        public void Tick_AfterLock_TargetMovingDoesNotChangeDirectionOrPath()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            rig.Start();
            rig.TickRange(0.0, 0.8);
            Vector3 locked = rig.Pattern.AimDirection;
            Assert.AreEqual(90f, AimDegrees(locked), ANGLE_TOLERANCE);

            rig.Tracked.Position = new Vector3(-10f, 0f, 0f);
            rig.TickRange(0.8, ExecuteEnd + 1.0);

            Assert.AreEqual(0f, Vector3.Angle(locked, rig.Pattern.AimDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(1, rig.Dash.LockCount);
            Vector3 expected = locked * RANGE;
            Assert.AreEqual(expected.x, rig.Self.Position.x, POSITION_TOLERANCE);
            Assert.AreEqual(expected.z, rig.Self.Position.z, POSITION_TOLERANCE);
        }

        // 정확히 조준한 방향에 실제 벽이 있으면 막히는 것이 정상이고, 옛 방향의 벽은 뒤에 있는 대상 조준을 막지 않는다
        [Test]
        public void Tick_WallBetweenBossAndPlayer_StopsAtTheWall_ButWallBehindTheAimIsIrrelevant()
        {
            Rig blocked = new Rig();
            blocked.Movement.WallZ = 10f;
            blocked.Tracked.Position = new Vector3(0f, 0f, 20f);
            blocked.Start();
            blocked.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(EBossChargeEnd.Collision, blocked.Pattern.EndKind);
            Assert.AreEqual(EBossChargeObstacleKind.Wall, blocked.Pattern.Collision.Kind);
            Assert.AreEqual(8f, blocked.Self.Position.z, POSITION_TOLERANCE);

            Rig open = new Rig();
            open.Movement.WallZ = 10f;  // 보스가 원래 보던 쪽의 벽
            open.Tracked.Position = new Vector3(0f, 0f, -20f);
            open.Start();
            open.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(EBossChargeEnd.Normal, open.Pattern.EndKind);
            Assert.AreEqual(-RANGE, open.Self.Position.z, POSITION_TOLERANCE);
        }

        [Test]
        public void Tick_SingleHugeStepWithPlayerBehind_MovesAndHitsTowardThePlayerOnly()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, -12f);
            FakeCombatTarget behind = rig.AddPlayer(PROBE_ID, new Vector3(0f, 0f, -9f));
            FakeCombatTarget front  = rig.AddPlayer(PROBE_ID + 1, new Vector3(0f, 0f, 9f));
            rig.Start();

            rig.Fsm.Tick(100.0);

            Assert.AreEqual(-RANGE, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, behind.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, front.CombatId));
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
        }

        [Test]
        public void Cancel_DuringDashTowardAPlayerBehind_StopsMovementAndLaterTargetsAreNotHit()
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(0f, 0f, -3f);
            FakeCombatTarget far = rig.AddPlayer(PROBE_ID, new Vector3(0f, 0f, -14f));
            rig.Start();

            rig.TickRange(0.0, ExecuteStart + 0.2);
            rig.Fsm.Cancel(EBossInterruptReason.CombatEnd);
            float stoppedZ = rig.Self.Position.z;
            int movesAtCancel = rig.Movement.MoveCalls;
            rig.Tracked.Position = new Vector3(20f, 0f, 0f);
            rig.TickRange(ExecuteStart + 0.2, ExecuteEnd + 3.0);

            Assert.Less(stoppedZ, -3f);
            Assert.Greater(stoppedZ, -14f);
            Assert.AreEqual(stoppedZ, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(0f, rig.Self.Position.x, POSITION_TOLERANCE);
            Assert.AreEqual(movesAtCancel, rig.Movement.MoveCalls);
            Assert.AreEqual(0, CountHits(rig.Resolver, far.CombatId));
            Assert.AreEqual(0, rig.Outcome.Ends.Count);
        }

        // 선택된 대상이 죽거나 사라지면 마지막 유효 방향을 유지하고 다른 플레이어로 바꾸지 않는다
        [Test]
        public void Tick_TargetDeadAtStart_KeepsFacingAndNeverSwitchesToAnotherPlayer()
        {
            Rig rig = new Rig();
            rig.Tracked.Position  = new Vector3(0f, 0f, -12f);
            rig.Tracked.LifeState = EParticipantLifeState.Dead;
            FakeCombatTarget behind = rig.AddPlayer(PROBE_ID, new Vector3(0f, 0f, -9f));
            FakeCombatTarget front  = rig.AddPlayer(PROBE_ID + 1, new Vector3(0f, 0f, 9f));
            rig.Start();

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(0f, Vector3.Angle(Vector3.forward, rig.Dash.DashDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(0, CountHits(rig.Resolver, behind.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, front.CombatId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Tick_TargetDiesOrVanishesDuringTracking_KeepsTheLastValidDirection(bool vanishes)
        {
            Rig rig = new Rig();
            rig.Tracked.Position = new Vector3(10f, 0f, 0f);
            FakeCombatTarget other = rig.AddPlayer(PROBE_ID, new Vector3(-9f, 0f, 0f));
            rig.Start();

            rig.TickRange(0.0, 0.3);
            if (vanishes)
            {
                rig.Query.Targets.Remove(rig.Tracked);
            }
            else
            {
                rig.Tracked.LifeState = EParticipantLifeState.Dead;
            }

            rig.TickRange(0.3, ExecuteEnd + 1.0);

            Assert.AreEqual(90f, AimDegrees(rig.Dash.LockDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(90f, AimDegrees(rig.Dash.DashDirection), ANGLE_TOLERANCE);
            Assert.AreEqual(0, CountHits(rig.Resolver, other.CombatId), "다른 플레이어에게 예고 없이 방향을 바꾸지 않는다");
        }

        /****************************************
        *            Wall And Structure
        ****************************************/

        // 벽은 z=10이고 몸체 반경이 2m라 8m에서 멈춘다. 벽 너머 대상은 맞지 않고 충돌 종료로 긴 회복을 요청한다
        [Test]
        public void Tick_WallAhead_StopsInFrontAndEndsWithWallCollision()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 10f;
            FakeCombatTarget beforeStop = rig.AddPlayer(PROBE_ID, new Vector3(0f, 0f, 7f));
            FakeCombatTarget pastStop   = rig.AddPlayer(PROBE_ID + 1, new Vector3(0f, 0f, 9f));
            rig.Start();

            Assert.AreEqual(8f, rig.Dash.StartLength, POSITION_TOLERANCE);

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(8f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeEnd.Collision, rig.Pattern.EndKind);
            Assert.AreEqual(EBossChargeObstacleKind.Wall, rig.Pattern.Collision.Kind);
            Assert.AreEqual(0, rig.Pattern.Collision.StructureId);
            Assert.AreEqual(8f, rig.Pattern.Collision.TravelDistance, POSITION_TOLERANCE);
            Assert.AreEqual(8f, rig.Pattern.Collision.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(COLLISION_RECOVERY, rig.Pattern.RecoverySeconds, 0.0001f);
            Assert.AreEqual(COLLISION_RECOVERY, rig.Pattern.GetRecoverySeconds(RECOVERY), 0.0001f);
            Assert.AreEqual(1, CountHits(rig.Resolver, beforeStop.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, pastStop.CombatId));
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
            Assert.AreEqual(COLLISION_RECOVERY, rig.Outcome.Recoveries[0], 0.0001f);
        }

        // 구조물은 z=12 평면이라 10m에서 멈춘다. 식별자가 결과에 실린다
        [Test]
        public void Tick_StructureAhead_StopsInFrontAndReportsStructureId()
        {
            Rig rig = new Rig();
            rig.Structures.Add(STRUCTURE_A, 12f);
            FakeCombatTarget beforeStop = rig.AddPlayer(PROBE_ID, new Vector3(0f, 0f, 9f));
            FakeCombatTarget pastStop   = rig.AddPlayer(PROBE_ID + 1, new Vector3(0f, 0f, 11f));
            rig.Start();

            Assert.AreEqual(10f, rig.Dash.StartLength, POSITION_TOLERANCE);

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(10f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeEnd.Collision, rig.Pattern.EndKind);
            Assert.AreEqual(EBossChargeObstacleKind.Structure, rig.Pattern.Collision.Kind);
            Assert.AreEqual(STRUCTURE_A, rig.Pattern.Collision.StructureId);
            Assert.AreEqual(10f, rig.Pattern.Collision.TravelDistance, POSITION_TOLERANCE);
            Assert.AreEqual(COLLISION_RECOVERY, rig.Pattern.GetRecoverySeconds(RECOVERY), 0.0001f);
            Assert.AreEqual(1, CountHits(rig.Resolver, beforeStop.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, pastStop.CombatId));
        }

        // 벽이 구조물보다 가까우면 벽에 막힌 것이다
        [Test]
        public void Tick_WallCloserThanStructure_EndsWithWallCollision()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 10f;
            rig.Structures.Add(STRUCTURE_A, 14f);
            rig.Start();

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(8f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeObstacleKind.Wall, rig.Pattern.Collision.Kind);
            Assert.AreEqual(0, rig.Pattern.Collision.StructureId);
        }

        // 구조물이 벽보다 가까우면 구조물에 막힌 것이다
        [Test]
        public void Tick_StructureCloserThanWall_EndsWithStructureCollision()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 14f;
            rig.Structures.Add(STRUCTURE_A, 12f);
            rig.Start();

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(10f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeObstacleKind.Structure, rig.Pattern.Collision.Kind);
            Assert.AreEqual(STRUCTURE_A, rig.Pattern.Collision.StructureId);
        }

        // 예고 때는 없던 구조물이 실행 전에 생겨도 이동 도중에 막는다
        [Test]
        public void Tick_StructureAppearsAfterTelegraphStart_StillStopsMovement()
        {
            Rig rig = new Rig();
            rig.Structures.Add(STRUCTURE_A, 12f);
            rig.Structures.Enabled = false;
            rig.Start();
            Assert.AreEqual(RANGE, rig.Dash.StartLength, POSITION_TOLERANCE);

            rig.TickRange(0.0, 1.0);
            rig.Structures.Enabled = true;
            rig.TickRange(1.0, ExecuteEnd + 1.0);

            Assert.AreEqual(10f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeEnd.Collision, rig.Pattern.EndKind);
            Assert.AreEqual(EBossChargeObstacleKind.Structure, rig.Pattern.Collision.Kind);
            Assert.AreEqual(STRUCTURE_A, rig.Pattern.Collision.StructureId);
        }

        // 실행 시작 때 구조물 앞 거리로 돌진 길이를 정한 뒤 구조물이 사라져도, 보스는 그 위치에서 멈추고 구조물 충돌로 보고된다.
        // 멈춘 뒤 다시 조회해 종류를 정하면 벽으로 보고되는 결함이었다
        [Test]
        public void Tick_StructureVanishesAfterLengthWasLimited_StillReportsStructureAndId()
        {
            Rig rig = new Rig();
            rig.Structures.Add(STRUCTURE_A, 12f);
            rig.Start();
            rig.TickRange(0.0, ExecuteStart + 0.1);
            Assert.AreEqual(1, rig.Dash.DashStartCount);
            Assert.AreEqual(10f, rig.Dash.DashDistance, POSITION_TOLERANCE);

            rig.Structures.Enabled = false;
            rig.TickRange(ExecuteStart + 0.1, ExecuteEnd + 1.0);
            rig.Fsm.Tick(ExecuteEnd + 100.0);

            Assert.AreEqual(10f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeEnd.Collision, rig.Pattern.EndKind);
            Assert.AreEqual(EBossChargeObstacleKind.Structure, rig.Pattern.Collision.Kind);
            Assert.AreEqual(STRUCTURE_A, rig.Pattern.Collision.StructureId);
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
            Assert.AreEqual(STRUCTURE_A, rig.Outcome.Collisions[0].StructureId);
            Assert.AreEqual(COLLISION_RECOVERY, rig.Pattern.GetRecoverySeconds(RECOVERY), 0.0001f);
        }

        // 구조물이 돌진이 시작되기 전에 사라지면 실행 시작 때 다시 재므로 막히지 않고 정상 종료한다
        [Test]
        public void Tick_StructureVanishesBeforeExecute_RunsFullRangeNormally()
        {
            Rig rig = new Rig();
            rig.Structures.Add(STRUCTURE_A, 12f);
            rig.Start();
            Assert.AreEqual(10f, rig.Dash.StartLength, POSITION_TOLERANCE);

            rig.TickRange(0.0, 1.0);
            rig.Structures.Enabled = false;
            rig.TickRange(1.0, ExecuteEnd + 1.0);

            Assert.AreEqual(RANGE, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeEnd.Normal, rig.Pattern.EndKind);
            Assert.AreEqual(EBossChargeObstacleKind.None, rig.Pattern.Collision.Kind);
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
        }

        // 실행 중에 새로 생긴 구조물도 이동을 막고 그 식별자로 보고된다
        [Test]
        public void Tick_StructureAppearsDuringExecution_StopsAndReportsStructureId()
        {
            Rig rig = new Rig();
            rig.Structures.Add(STRUCTURE_A, 12f);
            rig.Structures.Enabled = false;
            rig.Start();
            rig.TickRange(0.0, ExecuteStart + 0.1);
            Assert.AreEqual(RANGE, rig.Dash.DashDistance, POSITION_TOLERANCE);

            rig.Structures.Enabled = true;
            rig.TickRange(ExecuteStart + 0.1, ExecuteEnd + 1.0);

            Assert.AreEqual(10f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeEnd.Collision, rig.Pattern.EndKind);
            Assert.AreEqual(EBossChargeObstacleKind.Structure, rig.Pattern.Collision.Kind);
            Assert.AreEqual(STRUCTURE_A, rig.Pattern.Collision.StructureId);
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
        }

        // 측정 때 몰랐던 벽이 이동 중에 구조물보다 먼저 막으면 벽이고, 구조물보다 나중이면 구조물이다
        [Test]
        public void Tick_HiddenWallBeforeStructure_ReportsWall()
        {
            Rig rig = new Rig();
            rig.Structures.Add(STRUCTURE_A, 16f);
            rig.Movement.HiddenWallZ = 12f;
            rig.Start();
            Assert.AreEqual(14f, rig.Dash.StartLength, POSITION_TOLERANCE);

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(10f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeObstacleKind.Wall, rig.Pattern.Collision.Kind);
            Assert.AreEqual(0, rig.Pattern.Collision.StructureId);
        }

        [Test]
        public void Tick_HiddenWallAfterStructure_ReportsStructure()
        {
            Rig rig = new Rig();
            rig.Structures.Add(STRUCTURE_A, 12f);
            rig.Movement.HiddenWallZ = 16f;
            rig.Start();

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(10f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeObstacleKind.Structure, rig.Pattern.Collision.Kind);
            Assert.AreEqual(STRUCTURE_A, rig.Pattern.Collision.StructureId);
        }

        // 구조물이 벽과 같은 거리에 있으면 식별자가 있는 구조물로 보고된다
        [Test]
        public void Tick_StructureAtSameDistanceAsWall_ReportsStructure()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 12f;
            rig.Structures.Add(STRUCTURE_A, 12f);
            rig.Start();

            rig.TickRange(0.0, ExecuteEnd + 1.0);

            Assert.AreEqual(10f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeObstacleKind.Structure, rig.Pattern.Collision.Kind);
            Assert.AreEqual(STRUCTURE_A, rig.Pattern.Collision.StructureId);
        }

        // 충돌 종류는 돌진 실행의 이동 상태로 정한다. 표현용 돌진 알림(OnDashStopped)을 받는 쪽이 없어도 결과가 같다
        [Test]
        public void Tick_WithoutDashListener_ReportsSameCollision()
        {
            Rig rig = new Rig();
            rig.Structures.Add(STRUCTURE_A, 12f);
            MucheolChargePattern silent = (MucheolChargePattern)new MucheolPatternFactory(TEST_HREF, null, rig.Movement, null,
                null, null, null, new MucheolChargeSpec(COLLISION_RECOVERY), rig.Structures, rig.Outcome).Create(rig.Definition);
            BossPatternContext context = rig.Context;
            BossAttackFsm fsm = new BossAttackFsm();
            Assert.IsTrue(fsm.Start(rig.Definition, silent, in context, 0.0));

            fsm.Tick(ExecuteEnd + 5.0);

            Assert.AreEqual(EBossChargeEnd.Collision, silent.EndKind);
            Assert.AreEqual(EBossChargeObstacleKind.Structure, silent.Collision.Kind);
            Assert.AreEqual(STRUCTURE_A, silent.Collision.StructureId);
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
        }

        // 조회가 같은 구조물을 반복해 돌려주고 더 먼 구조물이 있어도 충돌 통지는 시전당 한 번, 첫 구조물이다
        [Test]
        public void Tick_RepeatedStructureReports_NotifiesCollisionOnlyOnce()
        {
            Rig rig = new Rig();
            rig.Structures.Add(STRUCTURE_A, 12f);
            rig.Structures.Add(STRUCTURE_B, 16f);
            rig.Start();

            rig.TickRange(0.0, ExecuteEnd + 4.0);
            rig.Fsm.Tick(ExecuteEnd + 10.0);

            Assert.Greater(rig.Structures.QueryCalls, 1);
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
            Assert.AreEqual(STRUCTURE_A, rig.Outcome.Collisions[0].StructureId);
            Assert.AreEqual(rig.Context.CastInstanceId, rig.Outcome.CastIds[0]);
        }

        // 구조물 조회가 없는 구성에서는 벽에만 막힌다
        [Test]
        public void Tick_WithoutStructureQuery_BehavesLikeInnerMovement()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 10f;
            MucheolChargePattern noQuery = (MucheolChargePattern)new MucheolPatternFactory(TEST_HREF, null, rig.Movement, rig.Dash,
                null, null, null, new MucheolChargeSpec(COLLISION_RECOVERY), null, rig.Outcome).Create(rig.Definition);
            BossPatternContext context = rig.Context;
            BossAttackFsm fsm = new BossAttackFsm();
            Assert.IsTrue(fsm.Start(rig.Definition, noQuery, in context, 0.0));

            fsm.Tick(ExecuteEnd + 5.0);

            Assert.AreEqual(8f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeObstacleKind.Wall, noQuery.Collision.Kind);
        }

        /****************************************
        *               Cancel
        ****************************************/

        [Test]
        public void Cancel_DuringTelegraph_NeverMovesHitsOrReportsOutcome()
        {
            Rig rig = new Rig();
            rig.Start();
            rig.TickRange(0.0, 1.0);

            rig.Fsm.Cancel(EBossInterruptReason.Death);
            rig.TickRange(1.0, ExecuteEnd + 1.0);

            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.AreEqual(0, rig.Movement.MoveCalls);
            Assert.AreEqual(0f, Vector3.Distance(Origin, rig.Self.Position), POSITION_TOLERANCE);
            Assert.AreEqual(0, rig.Dash.DashStartCount);
            Assert.AreEqual(0, rig.Resolver.Requests.Count);
            Assert.AreEqual(0, rig.Outcome.Ends.Count);
            Assert.AreEqual(EBossChargeEnd.None, rig.Pattern.EndKind);
            Assert.AreEqual(1, rig.Dash.Cancels.Count);
        }

        [Test]
        public void Cancel_DuringDash_StopsMovementAndTargetsAheadAreNotHit()
        {
            Rig rig = new Rig();
            FakeCombatTarget ahead = rig.AddPlayer(PROBE_ID, new Vector3(0f, 0f, 14f));
            rig.Start();

            // 실행 시작 0.2초 뒤에는 4.8m까지 갔고 추적 대상(3m)만 지나갔다
            rig.TickRange(0.0, ExecuteStart + 0.2);
            rig.Fsm.Cancel(EBossInterruptReason.CombatEnd);
            float stoppedZ = rig.Self.Position.z;
            int movesAtCancel = rig.Movement.MoveCalls;

            rig.TickRange(ExecuteStart + 0.2, ExecuteEnd + 3.0);

            Assert.IsTrue(rig.Pattern.IsCancelled);
            Assert.Greater(stoppedZ, 3f);
            Assert.Less(stoppedZ, 14f);
            Assert.AreEqual(stoppedZ, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(movesAtCancel, rig.Movement.MoveCalls);
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(0, CountHits(rig.Resolver, ahead.CombatId));
            Assert.AreEqual(0, rig.Outcome.Ends.Count);
            Assert.AreEqual(EBossChargeEnd.None, rig.Pattern.EndKind);
        }

        /****************************************
        *            Coarse Tick Intervals
        ****************************************/

        // 한 틱이 예고부터 회복 끝까지 넘어가도 이동은 정의의 거리까지, 대상별 한 번, 결과 통지는 한 번이다
        [Test]
        public void Tick_SingleHugeStepNormal_MovesFullRangeHitsOnceAndReportsOnce()
        {
            Rig rig = new Rig();
            FakeCombatTarget bystander = rig.AddPlayer(PROBE_ID, new Vector3(1f, 0f, 9f));
            rig.Start();

            rig.Fsm.Tick(ExecuteEnd + 100.0);
            rig.Fsm.Tick(ExecuteEnd + 200.0);

            Assert.AreEqual(RANGE, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, bystander.CombatId));
            Assert.AreEqual(EBossChargeEnd.Normal, rig.Pattern.EndKind);
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
            Assert.AreEqual(RECOVERY, rig.Pattern.GetRecoverySeconds(RECOVERY), 0.0001f);
            Assert.IsTrue(rig.Pattern.IsStopped);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Tick_SingleHugeStepCollision_StopsAtObstacleAndKeepsCollisionRecovery(bool useStructure)
        {
            Rig rig = new Rig();
            if (useStructure)
            {
                rig.Structures.Add(STRUCTURE_A, 12f);
            }
            else
            {
                rig.Movement.WallZ = 12f;
            }

            FakeCombatTarget pastStop = rig.AddPlayer(PROBE_ID, new Vector3(0f, 0f, 11f));
            rig.Start();

            rig.Fsm.Tick(ExecuteEnd + 100.0);
            rig.Fsm.Tick(ExecuteEnd + 200.0);

            Assert.AreEqual(10f, rig.Self.Position.z, POSITION_TOLERANCE);
            Assert.AreEqual(EBossChargeEnd.Collision, rig.Pattern.EndKind);
            Assert.AreEqual(useStructure ? EBossChargeObstacleKind.Structure : EBossChargeObstacleKind.Wall, rig.Pattern.Collision.Kind);
            Assert.AreEqual(COLLISION_RECOVERY, rig.Pattern.GetRecoverySeconds(RECOVERY), 0.0001f);
            Assert.AreEqual(0, CountHits(rig.Resolver, pastStop.CombatId));
            Assert.AreEqual(1, CountHits(rig.Resolver, rig.Tracked.CombatId));
            Assert.AreEqual(1, rig.Outcome.Ends.Count);
        }

        /****************************************
        *               Recovery
        ****************************************/

        // 공용 공격 FSM은 패턴이 IBossRecoveryDuration으로 정한 회복 시간을 읽는다. 충돌로 끝나면 일반 회복 시간이 지나도
        // 아직 회복 중이고 충돌 회복(2.5초)이 지나야 끝난다
        [Test]
        public void RealFsm_CollisionRecovery_IsAppliedFromExecuteEndBoundary()
        {
            Rig rig = new Rig();
            rig.Movement.WallZ = 12f;
            rig.Start();

            rig.Fsm.Tick(ExecuteEnd + RECOVERY + TIME_MARGIN);

            Assert.AreEqual(EBossAttackState.Recover, rig.Fsm.State);
            Assert.IsTrue(rig.Pattern.IsStopped);
            Assert.AreEqual(COLLISION_RECOVERY, rig.Pattern.GetRecoverySeconds(RECOVERY), 0.0001f);

            rig.Fsm.Tick(ExecuteEnd + COLLISION_RECOVERY + TIME_MARGIN);

            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
        }

        // 제안하는 FSM 변경의 수용 기준, 일반 종료는 실행 종료 경계 + 1.5초에 끝난다
        [Test]
        public void ProposedFsm_NormalEnd_RecoversForDefinitionSecondsFromExecuteEnd()
        {
            Rig rig = new Rig();
            ProposedRecoveryFsm fsm = new ProposedRecoveryFsm();
            BossPatternContext context = rig.Context;
            fsm.Start(rig.Definition, rig.Pattern, in context, 0.0);

            fsm.Tick(ExecuteEnd + (RECOVERY - TIME_MARGIN));
            Assert.AreEqual(EBossAttackState.Recover, fsm.State);

            fsm.Tick(ExecuteEnd + RECOVERY + TIME_MARGIN);
            Assert.AreEqual(EBossAttackState.Ready, fsm.State);
            Assert.AreEqual(EBossChargeEnd.Normal, rig.Pattern.EndKind);
        }

        // 충돌 종료는 충돌 시각이 아니라 실행 종료 경계부터 2.5초 회복한다
        [TestCase(true)]
        [TestCase(false)]
        public void ProposedFsm_CollisionEnd_RecoversForCollisionSecondsFromExecuteEnd(bool useStructure)
        {
            Rig rig = new Rig();
            if (useStructure)
            {
                rig.Structures.Add(STRUCTURE_A, 12f);
            }
            else
            {
                rig.Movement.WallZ = 12f;
            }

            ProposedRecoveryFsm fsm = new ProposedRecoveryFsm();
            BossPatternContext context = rig.Context;
            fsm.Start(rig.Definition, rig.Pattern, in context, 0.0);

            fsm.Tick(ExecuteEnd + RECOVERY + TIME_MARGIN);
            Assert.AreEqual(EBossAttackState.Recover, fsm.State);
            fsm.Tick(ExecuteEnd + (COLLISION_RECOVERY - TIME_MARGIN));
            Assert.AreEqual(EBossAttackState.Recover, fsm.State);

            fsm.Tick(ExecuteEnd + COLLISION_RECOVERY + TIME_MARGIN);
            Assert.AreEqual(EBossAttackState.Ready, fsm.State);
            Assert.AreEqual(EBossChargeEnd.Collision, rig.Pattern.EndKind);
        }

        // 큰 틱 한 번이 예고와 실행 경계를 넘어 회복 중간에 닿아도 올바른 회복 시간이 적용된다
        // 실행 종료 경계 + 2.0초는 일반 회복(1.5초)은 끝났고 충돌 회복(2.5초)은 끝나지 않은 지점이다
        [Test]
        public void ProposedFsm_SingleHugeStepIntoRecovery_AppliesRecoveryPerEndKind()
        {
            Rig normal = new Rig();
            ProposedRecoveryFsm normalFsm = new ProposedRecoveryFsm();
            BossPatternContext normalContext = normal.Context;
            normalFsm.Start(normal.Definition, normal.Pattern, in normalContext, 0.0);
            normalFsm.Tick(ExecuteEnd + 2.0);

            Rig collided = new Rig();
            collided.Movement.WallZ = 12f;
            ProposedRecoveryFsm collidedFsm = new ProposedRecoveryFsm();
            BossPatternContext collidedContext = collided.Context;
            collidedFsm.Start(collided.Definition, collided.Pattern, in collidedContext, 0.0);
            collidedFsm.Tick(ExecuteEnd + 2.0);

            Assert.AreEqual(EBossAttackState.Ready, normalFsm.State);
            Assert.AreEqual(EBossChargeEnd.Normal, normal.Pattern.EndKind);
            Assert.AreEqual(EBossAttackState.Recover, collidedFsm.State);
            Assert.AreEqual(EBossChargeEnd.Collision, collided.Pattern.EndKind);
            Assert.AreEqual(10f, collided.Self.Position.z, POSITION_TOLERANCE);

            collidedFsm.Tick(ExecuteEnd + COLLISION_RECOVERY + TIME_MARGIN);
            Assert.AreEqual(EBossAttackState.Ready, collidedFsm.State);
        }
    }
}
