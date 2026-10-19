// 서드파티
using NUnit.Framework;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class CombatCameraEffectsTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float AMOUNT        = 0.2f;
        private const float HOLD_SECONDS  = 0.6f;
        private const float IN_SMOOTH     = 0.12f;
        private const float OUT_SMOOTH    = 0.2f;
        private const float FRAME         = 1f / 60f;
        private const float TOLERANCE     = 0.001f;
        private const float SHAKE_SECONDS = 0.2f;

        /****************************************
        *                Methods
        ****************************************/

        [Test]
        public void Tick_WithoutEffects_KeepsNeutralValues()
        {
            var effects = new CombatCameraEffects();

            effects.Tick(0.0, FRAME);

            Assert.AreEqual(1f, effects.ZoomScale, TOLERANCE);
            Assert.AreEqual(Vector3.zero, effects.ShakeOffset);
            Assert.IsFalse(effects.IsActive);
        }

        [Test]
        public void ZoomHold_StartsGentlyInsteadOfJumpingToTarget()
        {
            var effects = new CombatCameraEffects();
            effects.ZoomHold(AMOUNT, HOLD_SECONDS, IN_SMOOTH, OUT_SMOOTH, 0.0);

            effects.Tick(FRAME, FRAME);

            // 첫 프레임에 목표 줌의 10퍼센트도 따라가지 않는다
            Assert.Greater(effects.ZoomScale, 1f - (AMOUNT * 0.1f));
        }

        [Test]
        public void ZoomHold_ReachesTargetWithoutOvershootAndReturnsToNeutral()
        {
            var effects = new CombatCameraEffects();
            effects.ZoomHold(AMOUNT, HOLD_SECONDS, IN_SMOOTH, OUT_SMOOTH, 0.0);

            float  lowest = 1f;
            double time   = 0.0;
            for (int i = 0; i < 60 * 3; ++i)
            {
                time += FRAME;
                effects.Tick(time, FRAME);
                lowest = Mathf.Min(lowest, effects.ZoomScale);
            }

            Assert.AreEqual(1f - AMOUNT, lowest, 0.01f, "목표 줌까지 당겨져야 한다");
            Assert.GreaterOrEqual(lowest, 1f - AMOUNT - TOLERANCE, "목표를 넘어 더 당기면 안 된다");
            Assert.AreEqual(1f, effects.ZoomScale, TOLERANCE);
            Assert.IsFalse(effects.IsActive);
        }

        [Test]
        public void ZoomHold_ZeroAmount_IsIgnored()
        {
            var effects = new CombatCameraEffects();

            effects.ZoomHold(0f, HOLD_SECONDS, IN_SMOOTH, OUT_SMOOTH, 0.0);
            effects.Tick(FRAME, FRAME);

            Assert.AreEqual(1f, effects.ZoomScale, TOLERANCE);
            Assert.IsFalse(effects.IsActive);
        }

        [Test]
        public void Kick_OffsetStaysWithinStrengthAndFadesOut()
        {
            var effects = new CombatCameraEffects();
            effects.Kick(Vector3.right, 0.2f, SHAKE_SECONDS, 0.0);

            float  largest = 0f;
            double time    = 0.0;
            for (int i = 0; i < 60; ++i)
            {
                time += FRAME;
                effects.Tick(time, FRAME);
                largest = Mathf.Max(largest, effects.ShakeOffset.magnitude);
            }

            Assert.LessOrEqual(largest, 0.2f + TOLERANCE);
            Assert.Greater(largest, 0f);
            Assert.AreEqual(Vector3.zero, effects.ShakeOffset);
        }

        [Test]
        public void Kick_WeakerThanRunningShake_IsIgnored()
        {
            var effects = new CombatCameraEffects();
            effects.Kick(Vector3.right, 0.3f, SHAKE_SECONDS, 0.0);

            effects.Kick(Vector3.up, 0.05f, SHAKE_SECONDS, 0.01);
            effects.Tick(0.02, FRAME);

            // 약한 흔들림이 방향을 덮었다면 위쪽 성분이 생긴다
            Assert.AreEqual(0f, effects.ShakeOffset.y, TOLERANCE);
        }

        [Test]
        public void Reset_ClearsRunningEffects()
        {
            var effects = new CombatCameraEffects();
            effects.Kick(Vector3.right, 0.3f, SHAKE_SECONDS, 0.0);
            effects.ZoomHold(AMOUNT, HOLD_SECONDS, IN_SMOOTH, OUT_SMOOTH, 0.0);
            effects.Tick(0.05, FRAME);

            effects.Reset();

            Assert.AreEqual(1f, effects.ZoomScale, TOLERANCE);
            Assert.AreEqual(Vector3.zero, effects.ShakeOffset);
            Assert.IsFalse(effects.IsActive);
        }
    }
}
