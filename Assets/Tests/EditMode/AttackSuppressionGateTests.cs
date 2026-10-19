// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public sealed class AttackSuppressionGateTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const uint   SEQUENCE       = 7;
        private const uint   STALE_SEQUENCE = 6;
        private const double START          = 50.0;
        private const double STEP           = 0.1;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Archer_HeldConfirm_AcceptedThenFinished_ResumesHeldAttack()
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);

            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START), "확정 직후에는 막는다");
            gate.OnSkillSent(SEQUENCE);
            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + STEP), "수락 전에는 막는다");
            gate.OnSkillResult(SEQUENCE, ERejectReason.None, START + (2 * STEP));
            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + (2 * STEP)), "수락 뒤 동작 복제가 오기 전에는 막는다");
            Assert.IsTrue(gate.Update(true, ESkillPhase.Windup, START + (3 * STEP)));
            Assert.IsTrue(gate.Update(true, ESkillPhase.Recovery, START + (8 * STEP)));

            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START + (12 * STEP)), "동작이 끝나면 누른 평타를 보낸다");
            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START + (13 * STEP)), "재개 뒤에는 계속 막지 않는다");
        }

        [Test]
        public void Archer_PhaseArrivesBeforeResult_StillResumesAfterFinish()
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);
            gate.OnSkillSent(SEQUENCE);

            Assert.IsTrue(gate.Update(true, ESkillPhase.Windup, START + STEP), "복제가 먼저 와도 수락 전에는 막는다");
            gate.OnSkillResult(SEQUENCE, ERejectReason.None, START + (2 * STEP));
            Assert.IsTrue(gate.Update(true, ESkillPhase.Windup, START + (2 * STEP)));

            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START + (10 * STEP)));
        }

        [Test]
        public void Archer_AcceptedButActionNeverSeen_ResumesAfterWaitLimit()
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);
            gate.OnSkillSent(SEQUENCE);
            gate.OnSkillResult(SEQUENCE, ERejectReason.None, START);

            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + (AttackSuppressionGate.ACCEPTED_ACTION_WAIT_SECONDS / 2)));
            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START + AttackSuppressionGate.ACCEPTED_ACTION_WAIT_SECONDS));
        }

        [Test]
        public void Archer_Rejected_StaysSuppressedUntilRelease()
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);
            gate.OnSkillSent(SEQUENCE);
            gate.OnSkillResult(SEQUENCE, ERejectReason.Cooldown, START + STEP);

            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + (20 * STEP)));
            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + 100.0));
            Assert.IsFalse(gate.Update(false, ESkillPhase.Idle, START + 101.0), "손을 떼면 풀린다");
            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START + 102.0), "다시 누른 평타는 막지 않는다");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Release_AtAnyStage_ClearsSuppression(int stage)
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);
            if (stage >= 1)
            {
                gate.OnSkillSent(SEQUENCE);
            }

            if (stage >= 2)
            {
                gate.OnSkillResult(SEQUENCE, ERejectReason.None, START);
            }

            if (stage >= 3)
            {
                Assert.IsTrue(gate.Update(true, ESkillPhase.Active, START + STEP));
            }

            Assert.IsFalse(gate.Update(false, ESkillPhase.Active, START + (2 * STEP)));
            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START + (3 * STEP)), "떼고 다시 누르면 막지 않는다");
        }

        [Test]
        public void StaleResult_BeforeAndAfterBinding_IsIgnored()
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);

            gate.OnSkillResult(SEQUENCE, ERejectReason.None, START);
            gate.OnSkillSent(SEQUENCE);
            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + 5.0), "묶이기 전에 온 결과는 이 요청의 수락이 아니다");

            gate.OnSkillResult(STALE_SEQUENCE, ERejectReason.None, START + 6.0);
            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + 20.0), "다른 순번의 수락은 재개 근거가 아니다");

            gate.OnSkillResult(STALE_SEQUENCE, ERejectReason.Cooldown, START + 21.0);
            gate.OnSkillResult(SEQUENCE, ERejectReason.None, START + 22.0);
            Assert.IsTrue(gate.Update(true, ESkillPhase.Recovery, START + 22.5));
            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START + 24.0), "다른 순번의 거절이 이 요청을 막지 않는다");
        }

        [Test]
        public void StaleResult_FromPreviousConfirm_DoesNotAffectNewConfirm()
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);
            gate.OnSkillSent(STALE_SEQUENCE);

            gate.Begin(true);
            gate.OnSkillSent(SEQUENCE);
            gate.OnSkillResult(STALE_SEQUENCE, ERejectReason.None, START);

            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + 5.0));
            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + 50.0), "이전 확정의 수락이 새 확정을 풀지 않는다");
        }

        [Test]
        public void NewerRequestAfterBinding_GivesUpResuming()
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);
            gate.OnSkillSent(SEQUENCE);
            gate.OnSkillSent(SEQUENCE + 1);
            gate.OnSkillResult(SEQUENCE, ERejectReason.None, START);

            Assert.IsTrue(gate.Update(true, ESkillPhase.Active, START + STEP));
            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + 5.0));
            Assert.IsFalse(gate.Update(false, ESkillPhase.Idle, START + 6.0));
        }

        [Test]
        public void NonResumableSkill_StaysSuppressedThroughActionUntilRelease()
        {
            AttackSuppressionGate gate = Confirmed(resumable: false);
            gate.OnSkillSent(SEQUENCE);
            gate.OnSkillResult(SEQUENCE, ERejectReason.None, START);

            Assert.IsTrue(gate.Update(true, ESkillPhase.Windup, START + STEP));
            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + 10.0));
            Assert.IsFalse(gate.Update(false, ESkillPhase.Idle, START + 11.0));
        }

        [Test]
        public void SentNeverArrives_StaysSuppressedUntilRelease()
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);

            Assert.IsTrue(gate.Update(true, ESkillPhase.Idle, START + 30.0));
            Assert.IsFalse(gate.Update(false, ESkillPhase.Idle, START + 31.0));
        }

        [Test]
        public void Reset_OnOwnershipLossOrDeath_ClearsEverything()
        {
            AttackSuppressionGate gate = Confirmed(resumable: true);
            gate.OnSkillSent(SEQUENCE);

            gate.Reset();

            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START));
            gate.OnSkillResult(SEQUENCE, ERejectReason.None, START);
            Assert.IsFalse(gate.Update(true, ESkillPhase.Windup, START + STEP), "초기화 뒤 늦은 결과는 아무것도 되살리지 않는다");
        }

        [Test]
        public void Inactive_NeverSuppresses()
        {
            AttackSuppressionGate gate = default;

            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START));
            gate.OnSkillSent(SEQUENCE);
            gate.OnSkillResult(SEQUENCE, ERejectReason.Cooldown, START);
            Assert.IsFalse(gate.Update(true, ESkillPhase.Idle, START + STEP));
        }

        [Test]
        public void ResumesAfterSkill_OnlyArcherRegularBurst()
        {
            byte r = Constants.Combat.SKILL_SLOT_R;
            byte q = Constants.Combat.SKILL_SLOT_Q;

            Assert.IsTrue(AttackSuppressionGate.ResumesAfterSkill(EPlayerClass.Archer, ESkillRequestKind.Regular, r));
            Assert.IsFalse(AttackSuppressionGate.ResumesAfterSkill(EPlayerClass.Archer, ESkillRequestKind.Regular, q));
            Assert.IsFalse(AttackSuppressionGate.ResumesAfterSkill(EPlayerClass.Warrior, ESkillRequestKind.Regular, r));
            Assert.IsFalse(AttackSuppressionGate.ResumesAfterSkill(EPlayerClass.Sorcerer,ESkillRequestKind.Regular, r));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static AttackSuppressionGate Confirmed(bool resumable)
        {
            var gate = new AttackSuppressionGate();
            gate.Begin(resumable);
            return gate;
        }
    }
}
