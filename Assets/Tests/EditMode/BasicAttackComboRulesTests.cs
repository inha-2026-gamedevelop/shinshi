// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class BasicAttackComboRulesTests
    {
        // 서버 콤보 기본 공격의 단계 선택 규칙과 궁수 단계 명세

        /****************************************
        *                Fields
        ****************************************/

        private const float RESET_SECONDS = 1.2f;
        private const int   STEP_COUNT    = 3;
        private const float TOLERANCE     = 0.001f;

        /****************************************
        *            Step Selection
        ****************************************/

        [Test]
        public void SelectStep_NoPreviousStep_StartsAtFirst()
        {
            Assert.AreEqual(0, BasicAttackComboRules.SelectStep(BasicAttackComboRules.NO_STEP, 0.0, RESET_SECONDS, STEP_COUNT));
        }

        [Test]
        public void SelectStep_WithinReset_AdvancesAndWraps()
        {
            Assert.AreEqual(1, BasicAttackComboRules.SelectStep(0, 0.1, RESET_SECONDS, STEP_COUNT));
            Assert.AreEqual(2, BasicAttackComboRules.SelectStep(1, RESET_SECONDS, RESET_SECONDS, STEP_COUNT));
            Assert.AreEqual(0, BasicAttackComboRules.SelectStep(2, 0.0, RESET_SECONDS, STEP_COUNT));
        }

        [Test]
        public void SelectStep_IdleLongerThanReset_ReturnsToFirst()
        {
            Assert.AreEqual(0, BasicAttackComboRules.SelectStep(1, RESET_SECONDS + 0.01, RESET_SECONDS, STEP_COUNT));
        }

        [Test]
        public void SelectStep_SingleStepClass_AlwaysFirst()
        {
            Assert.AreEqual(0, BasicAttackComboRules.SelectStep(0, 0.0, RESET_SECONDS, 1));
        }

        /****************************************
        *              Archer Spec
        ****************************************/

        [Test]
        public void ArcherSpec_Default_HasThreeStepsAndOnlyLastIsHitImmune()
        {
            PlayerConfigSO config = ScriptableObject.CreateInstance<PlayerConfigSO>();
            try
            {
                PlayerConfigSO.BasicAttackSpec bow = config.GetBasicAttack(EPlayerClass.Archer);
                Assert.IsTrue(bow.HasCombo);
                Assert.AreEqual(STEP_COUNT, bow.ComboStepCount);
                Assert.AreEqual(RESET_SECONDS, bow.ComboResetSeconds, TOLERANCE);

                float[] coefficients = { 0.65f, 0.75f, 1.1f };
                float[] intervals    = { 0.6f, 0.75f, 1.3f };
                float[] windups      = { 0.25f, 0.22f, 0.33f };
                for (int i = 0; i < STEP_COUNT; ++i)
                {
                    Assert.AreEqual(coefficients[i], bow.GetDamageCoefficient(i), TOLERANCE);
                    Assert.AreEqual(intervals[i], bow.GetInterval(i), TOLERANCE);
                    Assert.AreEqual(windups[i], bow.GetWindup(i), TOLERANCE);
                    Assert.AreEqual(i == STEP_COUNT - 1, bow.IsHitImmune(i));
                }

                // 한 바퀴의 초당 계수가 기존 단발(0.9A / 0.95초)과 크게 다르지 않아야 한다
                float cycleDps = (0.65f + 0.75f + 1.1f) / (0.6f + 0.75f + 1.3f);
                Assert.AreEqual(0.9f / 0.95f, cycleDps, 0.02f);

                var errors = new List<string>();
                config.Validate(errors);
                Assert.IsEmpty(errors, string.Join("\n", errors));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void NonComboClass_StepQuery_FallsBackToSingleSpec()
        {
            PlayerConfigSO config = ScriptableObject.CreateInstance<PlayerConfigSO>();
            try
            {
                PlayerConfigSO.BasicAttackSpec sword = config.GetBasicAttack(EPlayerClass.Swordsman);
                Assert.IsFalse(sword.HasCombo);
                Assert.AreEqual(1, sword.ComboStepCount);
                Assert.AreEqual(sword.Interval, sword.GetInterval(2), TOLERANCE);
                Assert.AreEqual(sword.DamageCoefficient, sword.GetDamageCoefficient(1), TOLERANCE);
                Assert.IsFalse(sword.IsHitImmune(2));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
