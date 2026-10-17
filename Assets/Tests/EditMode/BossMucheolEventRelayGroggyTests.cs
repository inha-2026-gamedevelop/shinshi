// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;

namespace Shinshi.Tests
{
    // 무철 중계가 돌진 유도 기믹의 그로기 알림을 표현 사건으로 바꿀 때 순서(같은 시전의 돌진 결과 뒤), 충돌 시각, 취소 처리를 지키는지 본다
    public class BossMucheolEventRelayGroggyTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    COMBAT_ID     = 9;
        private const int    CAST_ID       = 21;
        private const int    STRUCTURE_ID  = 7;
        private const double COLLIDE_TIME  = 30.0;
        private const double CONFIRM_TIME  = 30.1;
        private const double GROGGY_START  = 33.0;
        private const double GROGGY_END    = 36.0;
        private const float  RECOVERY      = 3f;
        private const double TIME_EPSILON  = 0.000001;

        /****************************************
        *               Tests
        ****************************************/

        [Test]
        public void Pending_AfterSameCastCharge_UsesCollisionTimeAndStartedKeepsIt()
        {
            Rig rig = new Rig();
            rig.ChargeEnded(CAST_ID, COLLIDE_TIME);
            rig.Gimmick.OnGroggyPending(CAST_ID, STRUCTURE_ID, CONFIRM_TIME);
            rig.Gimmick.OnGroggyStarted(CAST_ID, STRUCTURE_ID, GROGGY_START, GROGGY_END);

            AssertOrder(rig.Events, nameof(IBossMucheolEventListener.OnChargeEnded),
                nameof(IBossMucheolEventListener.OnChargeGroggyPending), nameof(IBossMucheolEventListener.OnChargeGroggyStarted));

            BossChargeGroggy pending = rig.Events.Groggies[0];
            Assert.AreEqual(COMBAT_ID, pending.BossId);
            Assert.AreEqual(CAST_ID, pending.CastId);
            Assert.AreEqual(STRUCTURE_ID, pending.StructureId);
            Assert.AreEqual(COLLIDE_TIME, pending.CollisionServerTime, TIME_EPSILON);
            Assert.IsFalse(pending.HasStarted);

            BossChargeGroggy started = rig.Events.Groggies[1];
            Assert.AreEqual(COLLIDE_TIME, started.CollisionServerTime, TIME_EPSILON);
            Assert.AreEqual(GROGGY_START, started.GroggyStartServerTime, TIME_EPSILON);
            Assert.AreEqual(GROGGY_END, started.GroggyEndServerTime, TIME_EPSILON);
            Assert.IsTrue(rig.Events.PhaseLedger.HasGroggy);
        }

        // 파괴 확인이 돌진 결과보다 먼저 와도 표현은 결과 뒤에 대기를 받는다
        [Test]
        public void Pending_BeforeSameCastCharge_IsHeldUntilChargeEnded()
        {
            Rig rig = new Rig();
            rig.Gimmick.OnGroggyPending(CAST_ID, STRUCTURE_ID, CONFIRM_TIME);
            Assert.AreEqual(0, rig.Events.Count(nameof(IBossMucheolEventListener.OnChargeGroggyPending)));

            rig.ChargeEnded(CAST_ID, COLLIDE_TIME);

            AssertOrder(rig.Events, nameof(IBossMucheolEventListener.OnChargeEnded),
                nameof(IBossMucheolEventListener.OnChargeGroggyPending));
            Assert.AreEqual(COLLIDE_TIME, rig.Events.Groggies[0].CollisionServerTime, TIME_EPSILON);
        }

        // 부순 시전을 모르면(0) 바로 보내고, 같은 구조물에 막힌 마지막 충돌 시각을 쓴다
        [Test]
        public void Pending_UnknownCast_SendsAtOnceWithMatchingStructureCollision()
        {
            Rig rig = new Rig();
            rig.ChargeEnded(CAST_ID, COLLIDE_TIME);
            rig.Gimmick.OnGroggyPending(CombatIds.NONE, STRUCTURE_ID, CONFIRM_TIME);

            Assert.AreEqual(1, rig.Events.Groggies.Count);
            Assert.AreEqual(CombatIds.NONE, rig.Events.Groggies[0].CastId);
            Assert.AreEqual(COLLIDE_TIME, rig.Events.Groggies[0].CollisionServerTime, TIME_EPSILON);
        }

        // 결과가 오지 않은 채 시작되면(시전 취소) 미뤄 둔 대기를 먼저 보낸다. 충돌을 모르니 확인 시각이다
        [Test]
        public void Started_WithHeldPending_FlushesPendingFirst()
        {
            Rig rig = new Rig();
            rig.Gimmick.OnGroggyPending(CAST_ID, STRUCTURE_ID, CONFIRM_TIME);
            rig.Gimmick.OnGroggyStarted(CAST_ID, STRUCTURE_ID, GROGGY_START, GROGGY_END);

            AssertOrder(rig.Events, nameof(IBossMucheolEventListener.OnChargeGroggyPending),
                nameof(IBossMucheolEventListener.OnChargeGroggyStarted));
            Assert.AreEqual(CONFIRM_TIME, rig.Events.Groggies[0].CollisionServerTime, TIME_EPSILON);
            Assert.AreEqual(CONFIRM_TIME, rig.Events.Groggies[1].CollisionServerTime, TIME_EPSILON);
        }

        [Test]
        public void Cancelled_AfterSentPending_SendsCancelAndClearsRecord()
        {
            Rig rig = new Rig();
            rig.ChargeEnded(CAST_ID, COLLIDE_TIME);
            rig.Gimmick.OnGroggyPending(CAST_ID, STRUCTURE_ID, CONFIRM_TIME);
            rig.Gimmick.OnGroggyCancelled(CAST_ID);

            Assert.AreEqual(1, rig.Events.Count(nameof(IBossMucheolEventListener.OnChargeGroggyCancelled)));
            Assert.IsFalse(rig.Events.PhaseLedger.HasGroggy);
        }

        // 보내지 않은 대기는 클라이언트가 모르므로 취소도 보내지 않는다
        [Test]
        public void Cancelled_WhileHeld_SendsNothing()
        {
            Rig rig = new Rig();
            rig.Gimmick.OnGroggyPending(CAST_ID, STRUCTURE_ID, CONFIRM_TIME);
            rig.Gimmick.OnGroggyCancelled(CAST_ID);
            rig.ChargeEnded(CAST_ID, COLLIDE_TIME);

            Assert.AreEqual(0, rig.Events.Count(nameof(IBossMucheolEventListener.OnChargeGroggyPending)));
            Assert.AreEqual(0, rig.Events.Count(nameof(IBossMucheolEventListener.OnChargeGroggyCancelled)));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static void AssertOrder(BossEventRecorder events, params string[] names)
        {
            int from = 0;
            for (int i = 0; i < names.Length; ++i)
            {
                int found = -1;
                for (int j = from; j < events.Records.Count; ++j)
                {
                    if (events.Records[j].Name == names[i])
                    {
                        found = j;
                        break;
                    }
                }

                Assert.GreaterOrEqual(found, 0, $"{names[i]}가 앞 사건 뒤에 와야 한다");
                from = found + 1;
            }
        }

        private sealed class Rig
        {
            public FakeHost                      Host    { get; } = new FakeHost();
            public BossEventRecorder             Events  { get; } = new BossEventRecorder();
            public BossMucheolEventRelay         Relay   { get; }
            public IMucheolChargeGimmickListener Gimmick => Relay;

            public Rig()
            {
                Relay = new BossMucheolEventRelay(Host);
                Relay.SetListener(Events);
            }

            public void ChargeEnded(int castId, double tickTime)
            {
                Host.TickTime = tickTime;
                var collision = new BossChargeCollision(EBossChargeObstacleKind.Structure, STRUCTURE_ID, Vector3.forward, 1f);
                ((IBossChargeOutcomeListener)Relay).OnChargeEnded(castId, EBossChargeEnd.Collision, collision, RECOVERY);
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
    }
}
