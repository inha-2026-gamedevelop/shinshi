// System
using System;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    /// <summary> 검사, 마검사, 궁수 모션 배정의 시간 예산 규칙, 분절 재생 위치, 프로필 조회를 시험한다 </summary>
    public class ClassCombatAnimationTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_SPEED       = 0.5f;
        private const float MAX_ASSIGN      = 1.5f;
        private const float FLOAT_TOLERANCE = 0.0001f;

        private PlayerCombatAnimationProfileSO _profile;

        /****************************************
        *               Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<PlayerCombatAnimationProfileSO>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_profile);
        }

        /****************************************
        *       Speed Budget Rules
        ****************************************/

        [TestCase(2f, 1f, 2f)]
        [TestCase(1f, 2f, 0.5f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(1f, 0f, 0f)]
        [TestCase(-1f, 1f, 0f)]
        public void RequiredSpeed_ReturnsRatioOrZeroForBadInput(float length, float target, float expected)
        {
            Assert.AreEqual(expected, CombatAnimationTriggerRules.RequiredSpeed(length, target), FLOAT_TOLERANCE);
        }

        [TestCase(1.23f, 1f, true)]
        [TestCase(1.5f, 1f, true)]
        [TestCase(1.81f, 1f, false)]
        [TestCase(0.5f, 1f, true)]
        [TestCase(0.4f, 1f, false)]
        [TestCase(0f, 1f, false)]
        [TestCase(1f, 0f, false)]
        public void IsWithinSpeedBudget_BoundariesAreInclusive(float length, float target, bool expected)
        {
            Assert.AreEqual(expected, CombatAnimationTriggerRules.IsWithinSpeedBudget(length, target, MIN_SPEED, MAX_ASSIGN));
        }

        [Test]
        public void IsWithinSpeedBudget_DoesNotClampOverBudgetSpeed()
        {
            Assert.AreEqual(3.33f, CombatAnimationTriggerRules.RequiredSpeed(3.33f, 1f), FLOAT_TOLERANCE);
            Assert.IsFalse(CombatAnimationTriggerRules.IsWithinSpeedBudget(3.33f, 1f, MIN_SPEED, MAX_ASSIGN));
        }

        /****************************************
        *       Segment Playback Position
        ****************************************/

        [Test]
        public void SumLengths_AddsSegmentsAndHandlesNull()
        {
            Assert.AreEqual(0f, CombatAnimationTriggerRules.SumLengths(null), FLOAT_TOLERANCE);
            Assert.AreEqual(0f, CombatAnimationTriggerRules.SumLengths(Array.Empty<float>()), FLOAT_TOLERANCE);
            Assert.AreEqual(3.5f, CombatAnimationTriggerRules.SumLengths(new[] { 1f, 0.5f, 2f }), FLOAT_TOLERANCE);
        }

        [Test]
        public void TryGetSegmentPosition_NegativeElapsedWaits()
        {
            bool found = CombatAnimationTriggerRules.TryGetSegmentPosition(-0.1, 1f, new[] { 1f, 1f }, out int segment, out float normalized,
                out bool isWaiting);

            Assert.IsFalse(found);
            Assert.IsTrue(isWaiting);
            Assert.AreEqual(0, segment);
            Assert.AreEqual(0f, normalized, FLOAT_TOLERANCE);
        }

        [Test]
        public void TryGetSegmentPosition_FindsSegmentAndNormalizedTime()
        {
            float[] lengths = { 1f, 2f, 1f };

            Assert.IsTrue(CombatAnimationTriggerRules.TryGetSegmentPosition(0.5, 1f, lengths, out int segment, out float normalized, out bool isWaiting));
            Assert.AreEqual(0, segment);
            Assert.AreEqual(0.5f, normalized, FLOAT_TOLERANCE);
            Assert.IsFalse(isWaiting);

            Assert.IsTrue(CombatAnimationTriggerRules.TryGetSegmentPosition(2.0, 1f, lengths, out segment, out normalized, out isWaiting));
            Assert.AreEqual(1, segment);
            Assert.AreEqual(0.5f, normalized, FLOAT_TOLERANCE);

            // 경과 1.5초 x 속도 2 = 클립 시간 3.0초이고, 구간 {1, 2, 1}에서 3.0은 세 번째 구간(인덱스 2)의 시작이다
            Assert.IsTrue(CombatAnimationTriggerRules.TryGetSegmentPosition(1.5, 2f, lengths, out segment, out normalized, out isWaiting));
            Assert.AreEqual(2, segment);
            Assert.AreEqual(0.0f, normalized, FLOAT_TOLERANCE);
        }

        // 구간 {1, 2, 1}: [0, 1), [1, 3), [3, 4). 경계 값은 다음 구간의 시작으로 보고, 합계 끝(4.0)부터는 재생하지 않는다
        [TestCase(0.999, 1f, 0, 0.999f)]
        [TestCase(1.0, 1f, 1, 0.0f)]
        [TestCase(1.001, 1f, 1, 0.0005f)]
        [TestCase(2.999, 1f, 1, 0.9995f)]
        [TestCase(3.0, 1f, 2, 0.0f)]
        [TestCase(3.001, 1f, 2, 0.001f)]
        [TestCase(3.999, 1f, 2, 0.999f)]
        [TestCase(1.49, 2f, 1, 0.99f)]
        [TestCase(1.5, 2f, 2, 0.0f)]
        [TestCase(1.51, 2f, 2, 0.02f)]
        public void TryGetSegmentPosition_AroundBoundaries_PicksExpectedSegment(double elapsed, float speed, int expectedSegment,
            float expectedNormalized)
        {
            Assert.IsTrue(CombatAnimationTriggerRules.TryGetSegmentPosition(elapsed, speed, new[] { 1f, 2f, 1f }, out int segment,
                out float normalized, out bool isWaiting));
            Assert.AreEqual(expectedSegment, segment);
            Assert.AreEqual(expectedNormalized, normalized, FLOAT_TOLERANCE);
            Assert.IsFalse(isWaiting);
        }

        [Test]
        public void TryGetSegmentPosition_AtTotalLength_IsFinished()
        {
            float[] lengths = { 1f, 2f, 1f };

            Assert.IsFalse(CombatAnimationTriggerRules.TryGetSegmentPosition(4.0, 1f, lengths, out _, out _, out bool isWaiting));
            Assert.IsFalse(isWaiting);
            Assert.IsFalse(CombatAnimationTriggerRules.TryGetSegmentPosition(2.0, 2f, lengths, out _, out _, out isWaiting));
            Assert.IsFalse(isWaiting);
        }

        [Test]
        public void TryGetSegmentPosition_BoundaryMovesToNextSegment()
        {
            Assert.IsTrue(CombatAnimationTriggerRules.TryGetSegmentPosition(1.0, 1f, new[] { 1f, 1f }, out int segment, out float normalized, out _));
            Assert.AreEqual(1, segment);
            Assert.AreEqual(0f, normalized, FLOAT_TOLERANCE);
        }

        [Test]
        public void TryGetSegmentPosition_AfterTotalLengthIsFinished()
        {
            bool found = CombatAnimationTriggerRules.TryGetSegmentPosition(2.0, 1f, new[] { 1f, 1f }, out _, out _, out bool isWaiting);

            Assert.IsFalse(found);
            Assert.IsFalse(isWaiting);
        }

        [Test]
        public void TryGetSegmentPosition_RejectsBadInput()
        {
            Assert.IsFalse(CombatAnimationTriggerRules.TryGetSegmentPosition(0.1, 1f, null, out _, out _, out _));
            Assert.IsFalse(CombatAnimationTriggerRules.TryGetSegmentPosition(0.1, 1f, Array.Empty<float>(), out _, out _, out _));
            Assert.IsFalse(CombatAnimationTriggerRules.TryGetSegmentPosition(0.1, 0f, new[] { 1f }, out _, out _, out _));
            Assert.IsFalse(CombatAnimationTriggerRules.TryGetSegmentPosition(0.1, 1f, new[] { 0f, 1f }, out _, out _, out _));
        }

        /****************************************
        *            Profile Lookup
        ****************************************/

        [TestCase(EPlayerClass.Warrior, true)]
        [TestCase(EPlayerClass.Sorcerer, true)]
        [TestCase(EPlayerClass.Summoner, true)]
        [TestCase(EPlayerClass.Swordsman, false)]
        [TestCase(EPlayerClass.Mageknight, false)]
        [TestCase(EPlayerClass.Archer, false)]
        public void IsLegacyClass_SplitsOldAndNewClasses(EPlayerClass playerClass, bool expected)
        {
            Assert.AreEqual(expected, PlayerCombatAnimationProfileSO.IsLegacyClass(playerClass));
        }

        [Test]
        public void NewClass_WithoutEntries_HasNoStepsAndNoSkill()
        {
            Assert.AreEqual(0, _profile.GetAttackStepCount(EPlayerClass.Archer));
            Assert.IsFalse(_profile.TryGetAttackIndex(EPlayerClass.Archer, 0, out _));
            Assert.IsFalse(_profile.TryGetSkillIndex(EPlayerClass.Archer, ESkillRequestKind.Regular, 0, out _));
            Assert.IsFalse(_profile.TryGetFallbackAttackInterval(EPlayerClass.Archer, out _));
        }

        [Test]
        public void EditorSetupClasses_StoresAttackStepsPerClass()
        {
            _profile.EditorSetupClasses(
                new[]
                {
                    new PlayerCombatAnimationProfileSO.AttackAnimation(EPlayerClass.Mageknight, 0, 3),
                    new PlayerCombatAnimationProfileSO.AttackAnimation(EPlayerClass.Mageknight, 1, 4),
                    new PlayerCombatAnimationProfileSO.AttackAnimation(EPlayerClass.Archer, 0, 6)
                },
                Array.Empty<PlayerCombatAnimationProfileSO.ClassAttackInterval>(),
                Array.Empty<PlayerCombatAnimationProfileSO.SegmentSet>(),
                Array.Empty<PlayerCombatAnimationProfileSO.SegmentSet>());

            Assert.AreEqual(2, _profile.GetAttackStepCount(EPlayerClass.Mageknight));
            Assert.AreEqual(1, _profile.GetAttackStepCount(EPlayerClass.Archer));
            Assert.AreEqual(0, _profile.GetAttackStepCount(EPlayerClass.Swordsman));

            Assert.IsTrue(_profile.TryGetAttackIndex(EPlayerClass.Mageknight, 1, out int index));
            Assert.AreEqual(4, index);
            Assert.IsFalse(_profile.TryGetAttackIndex(EPlayerClass.Mageknight, 2, out _));
        }

        [Test]
        public void EditorSetup_KeepsLegacySkillLookup()
        {
            _profile.EditorSetup(
                new[] { new PlayerCombatAnimationProfileSO.SkillAnimation(EPlayerClass.Warrior, ESkillRequestKind.Regular, 1, 4) },
                new float[PlayerCombatAnimationProfileSO.ATTACK_COUNT], new float[PlayerCombatAnimationProfileSO.SKILL_COUNT],
                new float[PlayerCombatAnimationProfileSO.HIT_COUNT], 1f);

            Assert.IsTrue(_profile.TryGetSkillIndex(EPlayerClass.Warrior, ESkillRequestKind.Regular, 1, out int index));
            Assert.AreEqual(4, index);
            Assert.IsFalse(_profile.TryGetSkillIndex(EPlayerClass.Warrior, ESkillRequestKind.Regular, 2, out _));
            Assert.IsFalse(_profile.TryGetSkillIndex(EPlayerClass.Swordsman, ESkillRequestKind.Regular, 1, out _));
        }

        [Test]
        public void FallbackIntervalAndComboReset_UseClassValueOrGlobalDefault()
        {
            _profile.EditorSetupClasses(
                Array.Empty<PlayerCombatAnimationProfileSO.AttackAnimation>(),
                new[]
                {
                    new PlayerCombatAnimationProfileSO.ClassAttackInterval(EPlayerClass.Swordsman, 0.9f, 1.2f),
                    new PlayerCombatAnimationProfileSO.ClassAttackInterval(EPlayerClass.Archer, 0.95f, 0f)
                },
                Array.Empty<PlayerCombatAnimationProfileSO.SegmentSet>(),
                Array.Empty<PlayerCombatAnimationProfileSO.SegmentSet>());

            Assert.IsTrue(_profile.TryGetFallbackAttackInterval(EPlayerClass.Swordsman, out float seconds));
            Assert.AreEqual(0.9f, seconds, FLOAT_TOLERANCE);
            Assert.IsTrue(_profile.TryGetFallbackAttackInterval(EPlayerClass.Archer, out seconds));
            Assert.AreEqual(0.95f, seconds, FLOAT_TOLERANCE);
            Assert.IsFalse(_profile.TryGetFallbackAttackInterval(EPlayerClass.Mageknight, out _));

            Assert.AreEqual(1.2f, _profile.GetComboResetSeconds(EPlayerClass.Swordsman), FLOAT_TOLERANCE);
            Assert.AreEqual(_profile.ComboResetSeconds, _profile.GetComboResetSeconds(EPlayerClass.Archer), FLOAT_TOLERANCE);
            Assert.AreEqual(_profile.ComboResetSeconds, _profile.GetComboResetSeconds(EPlayerClass.Warrior), FLOAT_TOLERANCE);
        }

        [Test]
        public void Segments_OnlyReturnedWhenMoreThanOneAndMatchIndex()
        {
            _profile.EditorSetupClasses(
                Array.Empty<PlayerCombatAnimationProfileSO.AttackAnimation>(),
                Array.Empty<PlayerCombatAnimationProfileSO.ClassAttackInterval>(),
                new[]
                {
                    new PlayerCombatAnimationProfileSO.SegmentSet(6, new[] { 0.5f, 0.6f, 0.4f }),
                    new PlayerCombatAnimationProfileSO.SegmentSet(7, new[] { 1f })
                },
                new[] { new PlayerCombatAnimationProfileSO.SegmentSet(8, new[] { 0.3f, 0.2f }) });

            Assert.IsTrue(_profile.TryGetAttackSegments(6, out float[] lengths));
            Assert.AreEqual(3, lengths.Length);
            Assert.IsFalse(_profile.TryGetAttackSegments(7, out _));
            Assert.IsFalse(_profile.TryGetAttackSegments(8, out _));
            Assert.IsTrue(_profile.TryGetSkillSegments(8, out lengths));
            Assert.AreEqual(2, lengths.Length);
            Assert.IsFalse(_profile.TryGetSkillSegments(6, out _));
        }

        [Test]
        public void ClipLengthGetters_FallBackToOneForOutOfRange()
        {
            _profile.EditorSetup(Array.Empty<PlayerCombatAnimationProfileSO.SkillAnimation>(), new[] { 1.2f }, new[] { 0f }, new[] { 0.8f }, 1f);

            Assert.AreEqual(1.2f, _profile.GetAttackClipLength(0), FLOAT_TOLERANCE);
            Assert.AreEqual(1f, _profile.GetAttackClipLength(5), FLOAT_TOLERANCE);
            Assert.AreEqual(1f, _profile.GetSkillClipLength(0), FLOAT_TOLERANCE);
            Assert.AreEqual(0.8f, _profile.GetHitClipLength(0), FLOAT_TOLERANCE);
            Assert.AreEqual(1f, _profile.GetHitClipLength(-1), FLOAT_TOLERANCE);
        }
    }
}
