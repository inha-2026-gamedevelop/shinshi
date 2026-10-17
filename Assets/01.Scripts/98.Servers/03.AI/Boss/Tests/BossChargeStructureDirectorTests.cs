// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 무철 돌진 구조물 연결이 돌진 결과를 먼저 보내고 파괴를 확정하는지, 기믹이 Active가 아닐 때의 충돌을 무시하는지,
    // 지정 구조물 셋을 부수면 실제 기믹이 그로기 대기를 내고 중계가 파괴 사건 뒤에 보내는지 본다.
    // 발동 비율, 필요 수, 그로기 시간, 회복 시간은 이 시험이 정한 시작값이다
    public class BossChargeStructureDirectorTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  TRIGGER_RATE  = 0.70f;
        private const int    REQUIRED      = 3;
        private const float  GROGGY        = 3f;
        private const float  RECOVERY      = 1.5f;
        private const int    COMBAT_ID     = 41;
        private const int    NON_TARGET_ID = 4;
        private const double TIME_EPSILON  = 0.000001;

        private const string CHARGE_ENDED  = nameof(IBossMucheolEventListener.OnChargeEnded);
        private const string DESTROYED     = nameof(IBossMucheolEventListener.OnChargeStructureDestroyed);
        private const string PENDING       = nameof(IBossMucheolEventListener.OnChargeGroggyPending);

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void OnChargeEnded_ActiveStructureHit_SendsOutcomeBeforeDestroyAndConfirms()
        {
            Rig rig = new Rig();
            rig.Activate();
            rig.StartCast(11);

            rig.Collide(11, 1, 20.0);

            CollectionAssert.AreEqual(new[] { CHARGE_ENDED, DESTROYED }, rig.Sink.Names);
            Assert.AreEqual(11, rig.Sink.DestroyedCastId);
            Assert.AreEqual(1, rig.Sink.DestroyedStructureId);
            Assert.AreEqual(20.0, rig.Sink.DestroyedTime, TIME_EPSILON);
            Assert.IsTrue(rig.Field.IsDestroyed(1));
            Assert.AreEqual(1, rig.Gimmick.DestroyedCount);
        }

        // 기믹 발동 전의 충돌은 부수지 않는다. 지정 구조물이 미리 부서져 기믹을 끝낼 수 없게 되는 것을 막는다
        [Test]
        public void OnChargeEnded_GimmickDormant_IgnoresStructureHit()
        {
            Rig rig = new Rig();

            rig.Collide(11, 1, 20.0);

            CollectionAssert.AreEqual(new[] { CHARGE_ENDED }, rig.Sink.Names);
            Assert.IsFalse(rig.Field.IsDestroyed(1));
            Assert.AreEqual(0, rig.Gimmick.DestroyedCount);
        }

        [Test]
        public void OnChargeEnded_GimmickStopped_IgnoresStructureHit()
        {
            Rig rig = new Rig();
            rig.Activate();
            rig.Gimmick.Stop();

            rig.Collide(11, 1, 20.0);

            CollectionAssert.AreEqual(new[] { CHARGE_ENDED }, rig.Sink.Names);
            Assert.IsFalse(rig.Field.IsDestroyed(1));
        }

        [Test]
        public void OnChargeEnded_WallOrNormalEnd_DestroysNothing()
        {
            Rig rig = new Rig();
            rig.Activate();
            rig.StartCast(11);

            var wall = new BossChargeCollision(EBossChargeObstacleKind.Wall, 0, Vector3.zero, 3f);
            rig.Director.OnChargeEnded(11, EBossChargeEnd.Collision, wall, RECOVERY);
            rig.Director.OnChargeEnded(11, EBossChargeEnd.Normal, default, 0f);

            CollectionAssert.AreEqual(new[] { CHARGE_ENDED, CHARGE_ENDED }, rig.Sink.Names);
            Assert.AreEqual(0, rig.Gimmick.DestroyedCount);
        }

        // 지정이 아닌 구조물도 기믹 중에는 부서지지만 진행도에는 들어가지 않는다
        [Test]
        public void OnChargeEnded_NonTargetHit_DestroysWithoutProgress()
        {
            Rig rig = new Rig();
            rig.Activate();
            rig.StartCast(11);

            rig.Collide(11, NON_TARGET_ID, 20.0);

            CollectionAssert.AreEqual(new[] { CHARGE_ENDED, DESTROYED }, rig.Sink.Names);
            Assert.IsTrue(rig.Field.IsDestroyed(NON_TARGET_ID));
            Assert.AreEqual(0, rig.Gimmick.DestroyedCount);
        }

        // 조회는 부서진 구조물을 돌려주지 않지만 같은 식별자가 다시 와도 사건과 확인을 두 번 보내지 않는다
        [Test]
        public void OnChargeEnded_SameStructureTwice_DestroysOnce()
        {
            Rig rig = new Rig();
            rig.Activate();
            rig.StartCast(11);
            rig.Collide(11, 1, 20.0);
            rig.Gimmick.OnCastFinished(11, 22.0);
            rig.StartCast(12);

            rig.Collide(12, 1, 25.0);

            CollectionAssert.AreEqual(new[] { CHARGE_ENDED, DESTROYED, CHARGE_ENDED }, rig.Sink.Names);
            Assert.AreEqual(1, rig.Gimmick.DestroyedCount);
        }

        // 셋째 지정 구조물이 부서지는 시전에서 결과, 파괴, 그로기 대기 순으로 가고 기믹은 그 시전의 회복을 기다린다
        [Test]
        public void OnChargeEnded_ThirdDesignatedDestroyed_SendsGroggyPendingAfterDestroy()
        {
            Rig rig = new Rig();
            rig.Activate();

            rig.StartCast(11);
            rig.Collide(11, 1, 10.0);
            rig.Gimmick.OnCastFinished(11, 12.0);
            rig.StartCast(12);
            rig.Collide(12, 2, 20.0);
            rig.Gimmick.OnCastFinished(12, 22.0);
            rig.Sink.Names.Clear();

            rig.StartCast(13);
            rig.Collide(13, 3, 30.0);

            CollectionAssert.AreEqual(new[] { CHARGE_ENDED, DESTROYED, PENDING }, rig.Sink.Names);
            Assert.AreEqual(EMucheolChargeGimmickState.AwaitingFinalRecovery, rig.Gimmick.State);
            Assert.AreEqual(13, rig.Sink.Pending.CastId);
            Assert.AreEqual(3, rig.Sink.Pending.StructureId);
            Assert.AreEqual(30.0, rig.Sink.Pending.CollisionServerTime, TIME_EPSILON);

            rig.Gimmick.OnCastFinished(13, 31.5);
            Assert.AreEqual(EMucheolChargeGimmickState.Groggy, rig.Gimmick.State);
            Assert.AreEqual(31.5 + GROGGY, rig.Gimmick.GroggyEndTime, TIME_EPSILON);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private sealed class Rig
        {
            public FakeHost                    Host     { get; } = new FakeHost();
            public MucheolSink                 Sink     { get; } = new MucheolSink();
            public FakeBossVitals              Vitals   { get; } = new FakeBossVitals();
            public FakeBossCatalog             Catalog  { get; } = new FakeBossCatalog();
            public TrackerFactory              Factory  { get; } = new TrackerFactory();
            public BossMucheolEventRelay       Relay    { get; }
            public BossChargeStructureField    Field    { get; }
            public BossChargeStructureDirector Director { get; }
            public MucheolChargeGimmick        Gimmick  { get; }

            public Rig()
            {
                Catalog.Boss = BossTestFixtures.MakeBoss();
                Catalog.Patterns.Add(BossTestFixtures.MakePattern(MucheolPatternFactory.P06_ID));

                var structures = new[]
                {
                    new BossChargeStructureInfo(1, new Vector3(0f, 0f, 10f), 1f, true),
                    new BossChargeStructureInfo(2, new Vector3(10f, 0f, 0f), 1f, true),
                    new BossChargeStructureInfo(3, new Vector3(-10f, 0f, 0f), 1f, true),
                    new BossChargeStructureInfo(NON_TARGET_ID, new Vector3(0f, 0f, -10f), 1f, false)
                };
                Assert.IsTrue(BossChargeStructureField.TryCreate(structures, out BossChargeStructureField field, out string reason),
                    reason);
                Field = field;

                Relay = new BossMucheolEventRelay(Host);
                Relay.SetListener(Sink);

                Gimmick = new MucheolChargeGimmick(Vitals, Catalog, Factory, BossTestFixtures.BOSS_ID,
                    new MucheolChargeGimmickSpec(TRIGGER_RATE, REQUIRED, GROGGY), Relay);
                Factory.Tracker = Gimmick;
                Assert.IsTrue(Gimmick.SetDesignatedStructures(Field.GimmickTargetIds));

                Director = new BossChargeStructureDirector(Relay, Field, Host);
                Director.BindGimmick(Gimmick);
            }

            public void Activate()
            {
                Vitals.HealthRate = TRIGGER_RATE;
                Gimmick.Tick(0.0, 1);
                Assert.AreEqual(EMucheolChargeGimmickState.Active, Gimmick.State);
            }

            public void StartCast(int castId)
            {
                Assert.IsTrue(Gimmick.TrySelect(new BossBlackboard(), out PatternDefinition _));
                Gimmick.OnChargeCastStarted(castId);
            }

            public void Collide(int castId, int structureId, double tickTime)
            {
                Host.TickTime = tickTime;
                var collision = new BossChargeCollision(EBossChargeObstacleKind.Structure, structureId, Vector3.zero, 5f);
                Director.OnChargeEnded(castId, EBossChargeEnd.Collision, collision, RECOVERY);
            }
        }

        // 기믹이 자신이 시전 추적 수신자로 연결됐는지 확인하는 데만 쓴다. 패턴은 만들지 않는다
        private sealed class TrackerFactory : IBossPatternFactory, IBossPatternAvailability, IBossChargeCastTrackerSource
        {
            public IBossChargeCastTracker Tracker;

            public IBossChargeCastTracker ChargeCastTracker => Tracker;

            public bool CanCreate(PatternDefinition definition)
            {
                return true;
            }

            public IBossPattern Create(PatternDefinition definition)
            {
                return null;
            }
        }

        private sealed class FakeHost : IBossEventRelayHost
        {
            public int     CombatId        => COMBAT_ID;
            public Vector3 Position        => Vector3.zero;
            public double  TickTime        { get; set; }
            public string  ActivePatternId => MucheolPatternFactory.P06_ID;
            public ushort  PhaseGeneration => 0;

            public void Face(Vector3 direction)
            {
            }

            public void BeginPhaseCast(int castInstanceId)
            {
            }
        }

        // 돌진 결과, 구조물 파괴, 그로기 사건만 이름 순서로 적는다
        private sealed class MucheolSink : IBossMucheolEventListener
        {
            public List<string>     Names                { get; } = new List<string>();
            public int              DestroyedCastId      { get; private set; }
            public int              DestroyedStructureId { get; private set; }
            public double           DestroyedTime        { get; private set; }
            public BossChargeGroggy Pending              { get; private set; }

            public void OnChargeEnded(in BossChargeOutcome outcome)
            {
                Names.Add(CHARGE_ENDED);
            }

            public void OnChargeStructureDestroyed(int castId, int structureId, double serverTime)
            {
                Names.Add(DESTROYED);
                DestroyedCastId      = castId;
                DestroyedStructureId = structureId;
                DestroyedTime        = serverTime;
            }

            public void OnChargeGroggyPending(in BossChargeGroggy groggy)
            {
                Names.Add(PENDING);
                Pending = groggy;
            }

            public void OnChargeGroggyStarted(in BossChargeGroggy groggy)
            {
                Names.Add(nameof(OnChargeGroggyStarted));
            }

            public void OnChargeGroggyCancelled(int castId)
            {
                Names.Add(nameof(OnChargeGroggyCancelled));
            }

            public void OnStrikeChainStarted(in BossStrikeChainStart start)
            {
            }

            public void OnStrikeChainAimChanged(EBossStrikeChainKind kind, int castId, Vector3 direction, double serverTime)
            {
            }

            public void OnStrikeChainDirectionLocked(EBossStrikeChainKind kind, int castId, Vector3 direction)
            {
            }

            public void OnStrikeChainHit(EBossStrikeChainKind kind, int castId, int stepIndex, double serverTime)
            {
            }

            public void OnStrikeChainCancelled(EBossStrikeChainKind kind, int castId)
            {
            }

            public void OnMultiDashStarted(in BossMultiDashStart start)
            {
            }

            public void OnMultiDashAimChanged(int castId, int dashIndex, Vector3 direction, float length, double serverTime)
            {
            }

            public void OnMultiDashDirectionLocked(int castId, int dashIndex, Vector3 direction, float length)
            {
            }

            public void OnMultiDashReaimStarted(in BossMultiDashReaim reaim)
            {
            }

            public void OnMultiDashDashStarted(int castId, int dashIndex, float distance, float seconds, float speed,
                double startServerTime)
            {
            }

            public void OnMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall,
                double stopServerTime)
            {
            }

            public void OnMultiDashTruncated(int castId, int plannedDashCount, int completedDashCount)
            {
            }

            public void OnMultiDashCancelled(int castId)
            {
            }

            public void OnBattlefieldStarted(in BossBattlefieldStart start)
            {
            }

            public void OnBattlefieldHit(int castId, double serverTime)
            {
            }

            public void OnBattlefieldCancelled(int castId)
            {
            }
        }
    }
}
