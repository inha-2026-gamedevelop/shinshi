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
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 기대 위치는 클라이언트 요청서의 경로 표(보스 로컬 오른쪽 x, 앞 z) 값으로 손으로 계산했다. 보스는 +z를 보고 시작한다.
    // 시간, 사거리, 피해는 이 테스트가 정한 값이며 기획 값이 아니다
    public class MucheolBodyShiftTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TEST_HREF  = 3541f;
        private const float  RANGE      = 5f;
        private const float  TOLERANCE  = 0.005f;
        private const double TICK       = 1.0 / 30.0;
        private const float  TELEGRAPH  = 2.0f;
        private const float  RECOVERY   = 1.6f;
        private const int    SELF_ID    = 1;
        private const int    TRACKED_ID = 10;

        private static readonly Vector3 BossStart = new Vector3(2f, 0f, 3f);

        // 발 구르기 타격(클립 1.65초) (0.15, 0.82)에서 시작 자세(클립 0.15초) (0.00, -0.01)를 뺀 값
        private static readonly Vector3 StompStrike = new Vector3(0.15f, 0f, 0.83f);

        // 발 구르기 끝(클립 3.4초) (0.05, 0.22)에서 시작 자세를 뺀 값
        private static readonly Vector3 StompEnd = new Vector3(0.05f, 0f, 0.23f);

        /****************************************
        *               Helpers
        ****************************************/

        // 벽은 z = WallZ 평면이고 +z로 갈 때만 막는다
        private sealed class TrackMovement : IBossMovement
        {
            public FakeCombatTarget Self;
            public float            WallZ = float.PositiveInfinity;
            public int              MoveCalls;

            public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
            {
                if ((direction.z <= 0f) || float.IsPositiveInfinity(WallZ))
                {
                    return maxDistance;
                }

                return Mathf.Clamp((WallZ - origin.z) / direction.z, 0f, maxDistance);
            }

            public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
            {
                ++MoveCalls;

                Vector3 delta    = to - from;
                float   distance = delta.magnitude;
                if (distance <= 0f)
                {
                    return Self.Position;
                }

                Vector3 direction = delta / distance;
                Self.Position = from + (direction * GetClearDistance(from, direction, distance, bodyRadius));
                return Self.Position;
            }
        }

        // 수평 거리로 반경 안의 살아 있는 플레이어를 돌려준다
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
                    if (offset.sqrMagnitude <= (radius * radius))
                    {
                        results.Add(Players[i]);
                    }
                }

                return results.Count;
            }

            public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
            {
                return Players.Count;
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

        private sealed class Recorder : IBossCircleTelegraphListener, IBossFanTelegraphListener, IBossComboTelegraphListener,
            IMucheolSequenceTelegraphListener
        {
            public Vector3 TelegraphOrigin;
            public int     Hits;

            public void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds)
            {
                TelegraphOrigin = center;
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float radius, float arcAngle,
                float telegraphSeconds)
            {
                TelegraphOrigin = origin;
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float telegraphSeconds,
                IReadOnlyList<MucheolComboHit> hits)
            {
                TelegraphOrigin = origin;
            }

            public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float telegraphSeconds,
                IReadOnlyList<MucheolSequenceStep> steps)
            {
                TelegraphOrigin = origin;
            }

            public void OnHit(int castInstanceId, int hitCount)
            {
                Hits += hitCount;
            }

            public void OnHit(int castInstanceId, int hitIndex, int hitCount)
            {
                Hits += hitCount;
            }

            public void OnStepHit(int castInstanceId, int stepIndex, int hitCount)
            {
                Hits += hitCount;
            }

            public void OnAimChanged(int castInstanceId, Vector3 direction) { }
            public void OnDirectionLocked(int castInstanceId, Vector3 direction) { }
            public void OnCancelled(int castInstanceId, EBossInterruptReason reason) { }
        }

        private sealed class Rig
        {
            public FakeCombatTarget   Self      { get; } = new FakeCombatTarget { CombatId = SELF_ID, Faction = ECombatFaction.Enemy, Position = BossStart };
            public FakeCombatTarget   Tracked   { get; } = new FakeCombatTarget { CombatId = TRACKED_ID, Faction = ECombatFaction.Player };
            public RadiusQuery        Query     { get; } = new RadiusQuery();
            public FakeDamageResolver Resolver  { get; } = new FakeDamageResolver();
            public Recorder           Listener  { get; } = new Recorder();
            public TrackMovement      Movement  { get; } = new TrackMovement();
            public BossAttackFsm      Fsm       { get; } = new BossAttackFsm();
            public BossDamageGate     Gate      { get; }

            public Rig()
            {
                Movement.Self = Self;
                Gate          = new BossDamageGate(Resolver);
                Gate.Open();
            }

            // 추적 대상은 사거리 밖 보스 정면에 둬 맞지 않고 조준만 +z로 묶는다
            public void PlaceTracked(float forwardDistance)
            {
                Tracked.Position = BossStart + new Vector3(0f, 0f, forwardDistance);
                Query.Players.Add(Tracked);
            }

            public FakeCombatTarget AddPlayer(int id, Vector3 offset)
            {
                var player = new FakeCombatTarget { CombatId = id, Faction = ECombatFaction.Player, Position = BossStart + offset };
                Query.Players.Add(player);
                return player;
            }

            public MucheolPatternFactory PhaseOne(bool shiftsBody)
            {
                return new MucheolPatternFactory(TEST_HREF, Listener, Movement, null, Listener,
                    new MucheolComboSpec(new MucheolComboHit(RANGE, 120f, 0.14f, 0f), new MucheolComboHit(RANGE, 70f, 0.24f, 0.5f)),
                    Listener, shiftsBody: shiftsBody);
            }

            public MucheolPhase2PatternFactory PhaseTwo()
            {
                return new MucheolPhase2PatternFactory(TEST_HREF, MucheolDevCatalog.CreatePhaseTwoSpec(), new FakeBossVitals(),
                    new FakeBossCatalog(), Movement, Listener, shiftsBody: true);
            }

            public void Start(PatternDefinition definition, IBossPattern pattern)
            {
                var context = new BossPatternContext(definition, Gate.IssueInstanceId(), TRACKED_ID, Self, Query, Gate);
                Assert.IsTrue(Fsm.Start(definition, pattern, in context, 0.0));
            }

            public void RunUntil(double endTime)
            {
                for (double t = TICK; (t <= endTime) && (Fsm.State != EBossAttackState.Ready); t += TICK)
                {
                    Fsm.Tick(t);
                }
            }
        }

        private static PatternDefinition Make(string id, float telegraph, float tracking, float turnRate, float active,
            float recovery, EnemyCatalogSO.AttackShapeSpec shape)
        {
            return new PatternDefinition(id, MucheolPatternFactory.BOSS_ID, 1, 10f,
                new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f, 1f },
                telegraph, tracking, turnRate, active, recovery, shape, 0.2f, true);
        }

        private static PatternDefinition MakeStomp(float recovery = RECOVERY)
        {
            return Make(MucheolPatternFactory.P04_ID, TELEGRAPH, 0f, 0f, 0f, recovery, EnemyCatalogSO.AttackShapeSpec.Circle(RANGE));
        }

        private static void AssertNear(Vector3 expected, Vector3 actual, string message = null)
        {
            Assert.AreEqual(expected.x, actual.x, TOLERANCE, message);
            Assert.AreEqual(expected.z, actual.z, TOLERANCE, message);
        }

        /****************************************
        *              Catalog
        ****************************************/

        // 요청서의 0.1초 표 값과 같아야 한다
        [TestCase(MucheolPatternFactory.P04_ID, 1.65f, 0.15f, 0.82f)]
        [TestCase(MucheolPatternFactory.P01_ID, 1.3f, -0.43f, 1.17f)]
        [TestCase(MucheolPatternFactory.P02_ID, 1.0f, 0.10f, 2.15f)]
        [TestCase(MucheolPatternFactory.P05_ID, 1.7f, 0.22f, 3.42f)]
        [TestCase(MucheolPhase2PatternFactory.P01_ID, 2.3f, -1.35f, 2.23f)]
        public void Catalog_Sample_MatchesRequestedPath(string patternId, float clip, float x, float z)
        {
            Assert.IsTrue(MucheolBodyShiftCatalog.TryGet(patternId, out MucheolBodyShiftSpec spec));

            Vector2 shift = spec.Sample(clip);

            Assert.AreEqual(x, shift.x, TOLERANCE);
            Assert.AreEqual(z, shift.y, TOLERANCE);
        }

        // 돌진은 서버 돌진 이동이 이미 옮기므로 몸 이동이 없다
        [Test]
        public void Catalog_Dash_HasNoBodyShift()
        {
            Assert.IsFalse(MucheolBodyShiftCatalog.TryGet(MucheolPatternFactory.P03_ID, out _));
            Assert.IsFalse(MucheolBodyShiftCatalog.TryGet(MucheolPatternFactory.P06_ID, out _));
            Assert.IsFalse(MucheolBodyShiftCatalog.TryGet(MucheolPhase2PatternFactory.P03_ID, out _));
            Assert.IsFalse(MucheolBodyShiftCatalog.TryGet(MucheolPhase2PatternFactory.P06_ID, out _));
        }

        /****************************************
        *            Stomp (circle)
        ****************************************/

        // 예고 원과 판정 중심이 둘 다 타격 순간의 보스 위치다. 시작 위치에서는 사거리 밖인 대상이 맞는다
        [Test]
        public void Stomp_TelegraphAndHit_AreAtStrikePosition()
        {
            var rig = new Rig();
            rig.AddPlayer(20, StompStrike + new Vector3(0f, 0f, 4.9f));
            PatternDefinition definition = MakeStomp();
            var pattern = (BossCircleBlastPattern)rig.PhaseOne(true).Create(definition);

            rig.Start(definition, pattern);
            AssertNear(BossStart + StompStrike, rig.Listener.TelegraphOrigin, "예고 원");

            rig.RunUntil(TELEGRAPH);
            rig.Fsm.Tick(TELEGRAPH);

            AssertNear(BossStart + StompStrike, pattern.Center, "판정 중심");
            Assert.AreEqual(1, rig.Listener.Hits);
            AssertNear(BossStart + StompStrike, rig.Resolver.Requests[0].SourcePosition);
        }

        // 한 틱이 타격 시각을 크게 넘어도 판정은 타격 순간의 위치에서 한다
        [Test]
        public void Stomp_CoarseTick_StillStrikesAtStrikePosition()
        {
            var rig = new Rig();
            PatternDefinition definition = MakeStomp();
            var pattern = (BossCircleBlastPattern)rig.PhaseOne(true).Create(definition);

            rig.Start(definition, pattern);
            rig.Fsm.Tick(TELEGRAPH + 0.5);

            AssertNear(BossStart + StompStrike, pattern.Center);
        }

        // 기본값(꺼짐)이면 기존처럼 제자리에서 친다
        [Test]
        public void Stomp_WithoutShiftsBody_BossStaysInPlace()
        {
            var rig = new Rig();
            PatternDefinition definition = MakeStomp();
            var pattern = (BossCircleBlastPattern)rig.PhaseOne(false).Create(definition);

            rig.Start(definition, pattern);
            rig.RunUntil(TELEGRAPH + RECOVERY + 1.0);

            AssertNear(BossStart, rig.Self.Position);
            AssertNear(BossStart, pattern.Center);
            Assert.AreEqual(0, rig.Movement.MoveCalls);
        }

        // 기믹 진입 같은 취소는 그 자리에 남고 시작 위치로 돌아가지 않는다
        [Test]
        public void Stomp_Cancel_LeavesBossWhereItWas()
        {
            var rig = new Rig();
            PatternDefinition definition = MakeStomp();
            rig.Start(definition, rig.PhaseOne(true).Create(definition));

            rig.RunUntil(1.6);
            rig.Fsm.Cancel(EBossInterruptReason.GimmickEntry);
            Vector3 stopped = rig.Self.Position;
            rig.Fsm.Tick(3.0);

            Assert.Greater((stopped - BossStart).magnitude, 0.1f);
            AssertNear(stopped, rig.Self.Position);
        }

        // 회복이 꼬리 구간보다 짧아 시전이 먼저 끝나도 경로 끝 자세로 맞춘다
        [Test]
        public void Stomp_ShortRecovery_FinishSnapsToPathEnd()
        {
            var rig = new Rig();
            PatternDefinition definition = MakeStomp(0.2f);
            rig.Start(definition, rig.PhaseOne(true).Create(definition));

            rig.RunUntil(TELEGRAPH + 1.0);

            Assert.AreEqual(EBossAttackState.Ready, rig.Fsm.State);
            AssertNear(BossStart + StompEnd, rig.Self.Position);
        }

        // 벽이 길을 막으면 그 앞에서 멈추고, 예고도 벽 앞의 위치에 그린다
        [Test]
        public void Stomp_Wall_StopsBeforeWallAndTelegraphMatches()
        {
            var rig = new Rig();
            rig.Movement.WallZ = BossStart.z + 0.5f;
            PatternDefinition definition = MakeStomp();
            var pattern = (BossCircleBlastPattern)rig.PhaseOne(true).Create(definition);

            rig.Start(definition, pattern);
            rig.RunUntil(TELEGRAPH);
            rig.Fsm.Tick(TELEGRAPH);

            Assert.AreEqual(rig.Movement.WallZ, rig.Listener.TelegraphOrigin.z, TOLERANCE);
            Assert.LessOrEqual(pattern.Center.z, rig.Movement.WallZ + TOLERANCE);
        }

        /****************************************
        *            Swing (fan)
        ****************************************/

        [Test]
        public void Swing_FanOrigin_IsPredictedStrikePosition()
        {
            var rig = new Rig();
            rig.PlaceTracked(12f);
            rig.AddPlayer(20, new Vector3(0f, 0f, 3f));
            PatternDefinition definition = Make(MucheolPatternFactory.P01_ID, 1.4f, 0.35f, 180f, 0f, 1.2f,
                EnemyCatalogSO.AttackShapeSpec.Fan(RANGE, 120f));

            rig.Start(definition, rig.PhaseOne(true).Create(definition));
            Vector3 telegraph = rig.Listener.TelegraphOrigin;
            rig.RunUntil(1.4);
            rig.Fsm.Tick(1.4);

            Assert.Greater((telegraph - BossStart).magnitude, 0.5f);
            Assert.AreEqual(1, rig.Resolver.Requests.Count);
            AssertNear(telegraph, rig.Resolver.Requests[0].SourcePosition);
        }

        /****************************************
        *        Multi-hit (combo, sequence)
        ****************************************/

        // 연타는 타마다 그 타 확정 순간의 보스 위치에서 판정한다. 1타 클립 0.84초, 2타 클립 2.4초이고 시작 자세는 (-0.05, 0.03)이다
        [Test]
        public void Combo_EachHit_IsJudgedAtItsStrikePosition()
        {
            var rig = new Rig();
            rig.PlaceTracked(12f);
            rig.AddPlayer(20, new Vector3(0.2f, 0f, 4.5f));
            PatternDefinition definition = Make(MucheolPatternFactory.P05_ID, 1.6f, 0.4f, 150f, 0.5f, 2.4f,
                EnemyCatalogSO.AttackShapeSpec.Fan(RANGE, 120f));

            rig.Start(definition, rig.PhaseOne(true).Create(definition));
            rig.RunUntil(1.6 + 0.5 + 0.1);

            var first  = new Vector3(0.454f, 0f, 1.984f);
            var second = new Vector3(-0.08f, 0f, 2.48f);
            Assert.AreEqual(2, rig.Resolver.Requests.Count);
            AssertNear(BossStart + first, rig.Listener.TelegraphOrigin, "예고는 첫 타 위치");
            AssertNear(BossStart + first, rig.Resolver.Requests[0].SourcePosition, "1타");
            AssertNear(BossStart + second, rig.Resolver.Requests[1].SourcePosition, "2타");
        }

        // 2페이즈 연속 팔 휘두르기는 1타 순간 클립이 1.2초로 건너뛰어 (-0.65, 1.36), 2타 클립 1.65초 (-1.47, 1.88)이다
        [Test]
        public void DoubleSwing_EachStep_IsJudgedAtItsStrikePosition()
        {
            var rig = new Rig();
            rig.PlaceTracked(12f);
            FakeCombatTarget left  = rig.AddPlayer(20, new Vector3(-2.0f, 0f, 3.5f));
            FakeCombatTarget right = rig.AddPlayer(21, new Vector3(0.5f, 0f, 3.5f));
            PatternDefinition definition = Make(MucheolPhase2PatternFactory.P01_ID, 1.8f, 0.4f, 150f, 0.45f, 1.4f,
                EnemyCatalogSO.AttackShapeSpec.Fan(RANGE, 120f));

            rig.Start(definition, rig.PhaseTwo().Create(definition));
            rig.RunUntil(1.8 + 0.45 + 0.1);

            Assert.AreEqual(2, rig.Resolver.Requests.Count);
            Assert.AreEqual(left.CombatId, rig.Resolver.Requests[0].HitTargetId);
            AssertNear(BossStart + new Vector3(-0.65f, 0f, 1.36f), rig.Resolver.Requests[0].SourcePosition, "1타");
            Assert.AreEqual(right.CombatId, rig.Resolver.Requests[1].HitTargetId);
            AssertNear(BossStart + new Vector3(-1.47f, 0f, 1.88f), rig.Resolver.Requests[1].SourcePosition, "2타");
        }
    }
}
