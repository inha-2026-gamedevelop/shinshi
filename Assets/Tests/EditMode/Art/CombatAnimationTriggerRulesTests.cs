// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class CombatAnimationTriggerRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float CLIP_LENGTH     = 2f;
        private const float RESET_SECONDS   = 1.5f;
        private const int   COMBO_COUNT     = 3;
        private const float FLOAT_TOLERANCE = 0.0001f;

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(true, true, true, CombatAnimationTriggerRules.EAction.Dodge)]
        [TestCase(true, false, false, CombatAnimationTriggerRules.EAction.Dodge)]
        [TestCase(true, true, false, CombatAnimationTriggerRules.EAction.Dodge)]
        [TestCase(false, true, true, CombatAnimationTriggerRules.EAction.Skill)]
        [TestCase(false, true, false, CombatAnimationTriggerRules.EAction.Skill)]
        [TestCase(false, false, true, CombatAnimationTriggerRules.EAction.Attack)]
        [TestCase(false, false, false, CombatAnimationTriggerRules.EAction.None)]
        public void SelectAction_PriorityIsDodgeThenSkillThenAttack(bool dodge, bool skill, bool attack,
            CombatAnimationTriggerRules.EAction expected)
        {
            Assert.AreEqual(expected, CombatAnimationTriggerRules.SelectAction(dodge, skill, attack));
        }

        [TestCase((byte)0, (byte)0, false)]
        [TestCase((byte)5, (byte)5, false)]
        [TestCase((byte)5, (byte)6, true)]
        [TestCase((byte)255, (byte)0, true)]
        [TestCase((byte)0, (byte)255, true)]
        [TestCase((byte)255, (byte)255, false)]
        public void IsNewSequence_OnlyComparesEquality_SoWrapIsNew(byte baseline, byte current, bool expected)
        {
            Assert.AreEqual(expected, CombatAnimationTriggerRules.IsNewSequence(baseline, current));
        }

        [TestCase(0, 0, false)]
        [TestCase(5, 0, false)]
        [TestCase(0, 3, true)]
        [TestCase(3, 3, false)]
        [TestCase(3, 4, true)]
        public void IsNewCast_ZeroIsNeverNew(int baseline, int current, bool expected)
        {
            Assert.AreEqual(expected, CombatAnimationTriggerRules.IsNewCast(baseline, current));
        }

        [Test]
        public void ComputeSpeed_WithinRange_ReturnsClipOverTarget()
        {
            Assert.AreEqual(2f, CombatAnimationTriggerRules.ComputeSpeed(1f, 0.5f, 0.5f, 3f), FLOAT_TOLERANCE);
        }

        [Test]
        public void ComputeSpeed_OutsideRange_IsClamped()
        {
            Assert.AreEqual(3f, CombatAnimationTriggerRules.ComputeSpeed(10f, 1f, 0.5f, 3f), FLOAT_TOLERANCE);
            Assert.AreEqual(0.5f, CombatAnimationTriggerRules.ComputeSpeed(0.1f, 1f, 0.5f, 3f), FLOAT_TOLERANCE);
        }

        [TestCase(0f, 1f)]
        [TestCase(-1f, 1f)]
        [TestCase(1f, 0f)]
        [TestCase(1f, -2f)]
        [TestCase(0f, 0f)]
        public void ComputeSpeed_NonPositiveInputs_ReturnsOne(float clipLength, float targetSeconds)
        {
            Assert.AreEqual(1f, CombatAnimationTriggerRules.ComputeSpeed(clipLength, targetSeconds, 0.5f, 3f), FLOAT_TOLERANCE);
        }

        [Test]
        public void TryGetResumePosition_NegativeElapsed_IsWaitingAndNotPlayable()
        {
            bool playable = CombatAnimationTriggerRules.TryGetResumePosition(-0.1, 1f, CLIP_LENGTH, out float normalized, out bool isWaiting);

            Assert.IsFalse(playable);
            Assert.IsTrue(isWaiting);
            Assert.AreEqual(0f, normalized);
        }

        [Test]
        public void TryGetResumePosition_PastEnd_IsNotPlayableAndNotWaiting()
        {
            bool playable = CombatAnimationTriggerRules.TryGetResumePosition(CLIP_LENGTH, 1f, CLIP_LENGTH, out _, out bool isWaiting);
            bool beyond   = CombatAnimationTriggerRules.TryGetResumePosition(CLIP_LENGTH + 5.0, 1f, CLIP_LENGTH, out _, out bool beyondWaiting);

            Assert.IsFalse(playable);
            Assert.IsFalse(isWaiting);
            Assert.IsFalse(beyond);
            Assert.IsFalse(beyondWaiting);
        }

        [Test]
        public void TryGetResumePosition_MidClip_ReturnsNormalizedTime()
        {
            bool playable = CombatAnimationTriggerRules.TryGetResumePosition(0.5, 1f, CLIP_LENGTH, out float normalized, out bool isWaiting);

            Assert.IsTrue(playable);
            Assert.IsFalse(isWaiting);
            Assert.AreEqual(0.25f, normalized, FLOAT_TOLERANCE);
        }

        [Test]
        public void TryGetResumePosition_SpeedScalesProgress()
        {
            bool playable = CombatAnimationTriggerRules.TryGetResumePosition(0.5, 2f, CLIP_LENGTH, out float normalized, out _);

            Assert.IsTrue(playable);
            Assert.AreEqual(0.5f, normalized, FLOAT_TOLERANCE);
        }

        [Test]
        public void TryGetResumePosition_ZeroElapsed_StartsFromBeginning()
        {
            bool playable = CombatAnimationTriggerRules.TryGetResumePosition(0.0, 1f, CLIP_LENGTH, out float normalized, out bool isWaiting);

            Assert.IsTrue(playable);
            Assert.IsFalse(isWaiting);
            Assert.AreEqual(0f, normalized);
        }

        [TestCase(0f, 1f)]
        [TestCase(-1f, 1f)]
        [TestCase(1f, 0f)]
        public void TryGetResumePosition_InvalidClipOrSpeed_IsNotPlayable(float clipLength, float speed)
        {
            bool playable = CombatAnimationTriggerRules.TryGetResumePosition(0.5, speed, clipLength, out float normalized, out bool isWaiting);

            Assert.IsFalse(playable);
            Assert.IsFalse(isWaiting);
            Assert.AreEqual(0f, normalized);
        }

        [Test]
        public void NextAttackIndex_WithinResetWindow_CyclesThroughCombo()
        {
            Assert.AreEqual(1, CombatAnimationTriggerRules.NextAttackIndex(0, 0.5f, RESET_SECONDS, COMBO_COUNT));
            Assert.AreEqual(2, CombatAnimationTriggerRules.NextAttackIndex(1, 0.5f, RESET_SECONDS, COMBO_COUNT));
            Assert.AreEqual(0, CombatAnimationTriggerRules.NextAttackIndex(2, 0.5f, RESET_SECONDS, COMBO_COUNT));
        }

        [Test]
        public void NextAttackIndex_ExactlyAtResetBoundary_StillContinuesCombo()
        {
            Assert.AreEqual(2, CombatAnimationTriggerRules.NextAttackIndex(1, RESET_SECONDS, RESET_SECONDS, COMBO_COUNT));
        }

        [Test]
        public void NextAttackIndex_AfterResetWindow_RestartsAtZero()
        {
            Assert.AreEqual(0, CombatAnimationTriggerRules.NextAttackIndex(1, RESET_SECONDS + 0.01f, RESET_SECONDS, COMBO_COUNT));
        }

        [Test]
        public void NextAttackIndex_NegativePreviousOrEmptyCombo_ReturnsZero()
        {
            Assert.AreEqual(0, CombatAnimationTriggerRules.NextAttackIndex(-1, 0f, RESET_SECONDS, COMBO_COUNT));
            Assert.AreEqual(0, CombatAnimationTriggerRules.NextAttackIndex(1, 0f, RESET_SECONDS, 0));
        }

        [Test]
        public void NextAttackIndex_SingleAttackCombo_AlwaysZero()
        {
            Assert.AreEqual(0, CombatAnimationTriggerRules.NextAttackIndex(0, 0.1f, RESET_SECONDS, 1));
        }
    }
}
