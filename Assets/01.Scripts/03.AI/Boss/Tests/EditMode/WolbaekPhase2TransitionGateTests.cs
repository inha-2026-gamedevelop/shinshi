// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 2페이즈 전환 사건이 중복, 지연, 지난 세션, 끝난 구간으로 와도 한 번만, 서버 시각대로 재생 판정되는지 본다
    public class WolbaekPhase2TransitionGateTests
    {
        private const int    COMBAT  = 7;
        private const double START   = 100d;
        private const double END     = 109.6d;

        [Test]
        public void FirstCueAtStart_Plays()
        {
            var gate = NewGate();

            Assert.That(gate.Evaluate(Cue(1), START), Is.EqualTo(EWolbaekPhase2TransitionDecision.Play));
            Assert.That(gate.LastTransitionId, Is.EqualTo(1));
        }

        [Test]
        public void SameCueTwice_PlaysOnce()
        {
            var gate = NewGate();

            gate.Evaluate(Cue(1), START);

            Assert.That(gate.Evaluate(Cue(1), START + 0.1d), Is.EqualTo(EWolbaekPhase2TransitionDecision.Duplicate));
        }

        [Test]
        public void OlderCueAfterNewer_IsIgnored()
        {
            var gate = NewGate();

            gate.Evaluate(Cue(2), START);

            Assert.That(gate.Evaluate(Cue(1), START), Is.EqualTo(EWolbaekPhase2TransitionDecision.Duplicate));
        }

        [Test]
        public void LateCue_PlaysFromServerElapsed()
        {
            var gate = NewGate();

            Assert.That(gate.Evaluate(Cue(1), START + 3d), Is.EqualTo(EWolbaekPhase2TransitionDecision.Play));
        }

        [Test]
        public void CueAfterServerEnd_DoesNotPlayAndConsumesId()
        {
            var gate = NewGate();

            Assert.That(gate.Evaluate(Cue(1), END), Is.EqualTo(EWolbaekPhase2TransitionDecision.Ended));
            Assert.That(gate.Evaluate(Cue(1), END), Is.EqualTo(EWolbaekPhase2TransitionDecision.Duplicate));
        }

        [Test]
        public void SlightlyFutureCue_IsScheduled_FarFutureIsRejectedWithoutConsuming()
        {
            var gate = NewGate();

            Assert.That(gate.Evaluate(Cue(1), START - 1d), Is.EqualTo(EWolbaekPhase2TransitionDecision.TooEarly));
            Assert.That(gate.Evaluate(Cue(1), START - 0.2d), Is.EqualTo(EWolbaekPhase2TransitionDecision.Scheduled));
        }

        [Test]
        public void CueFromOtherCombatOrWithoutSession_IsIgnored()
        {
            var gate = new WolbaekPhase2TransitionGate();

            Assert.That(gate.Evaluate(Cue(1), START), Is.EqualTo(EWolbaekPhase2TransitionDecision.OtherSession));

            gate.ResetSession(COMBAT);
            var other = new WolbaekPhase2TransitionCue(COMBAT + 1, 1, START, END);
            Assert.That(gate.Evaluate(other, START), Is.EqualTo(EWolbaekPhase2TransitionDecision.OtherSession));

            gate.EndSession();
            Assert.That(gate.Evaluate(Cue(1), START), Is.EqualTo(EWolbaekPhase2TransitionDecision.OtherSession));
        }

        [Test]
        public void NewSession_AcceptsSameIdAgain()
        {
            var gate = NewGate();

            gate.Evaluate(Cue(1), START);
            gate.ResetSession(COMBAT);

            Assert.That(gate.Evaluate(Cue(1), START), Is.EqualTo(EWolbaekPhase2TransitionDecision.Play));
        }

        [Test]
        public void InvalidCue_IsRejected()
        {
            var gate = NewGate();

            Assert.That(gate.Evaluate(new WolbaekPhase2TransitionCue(COMBAT, 0, START, END), START), Is.EqualTo(EWolbaekPhase2TransitionDecision.Invalid));
            Assert.That(gate.Evaluate(new WolbaekPhase2TransitionCue(COMBAT, 1, START, START), START), Is.EqualTo(EWolbaekPhase2TransitionDecision.Invalid));
        }

        private static WolbaekPhase2TransitionGate NewGate()
        {
            var gate = new WolbaekPhase2TransitionGate();
            gate.ResetSession(COMBAT);
            return gate;
        }

        private static WolbaekPhase2TransitionCue Cue(int transitionId)
        {
            return new WolbaekPhase2TransitionCue(COMBAT, transitionId, START, END);
        }
    }
}
