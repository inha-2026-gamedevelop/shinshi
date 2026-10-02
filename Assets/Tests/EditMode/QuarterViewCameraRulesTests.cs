// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class QuarterViewCameraRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float PITCH         = 55f;
        private const float YAW           = 0f;
        private const float DISTANCE      = 17f;
        private const float FOCUS_HEIGHT  = 1f;
        private const float FIELD_OF_VIEW = 30f;
        private const int   LEVEL_COUNT   = 3;
        private const int   MIDDLE_LEVEL  = 1;
        private const float NOTCH         = 0.1f;
        private const float HALF          = 0.5f;
        private const float LARGE_SCROLL  = 10f;
        private const float TOLERANCE     = 0.0001f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void GetFollowOffset_CameraLooksAtFocus()
        {
            Vector3 offset  = QuarterViewCameraRules.GetFollowOffset(PITCH, YAW, DISTANCE, FOCUS_HEIGHT);
            Vector3 forward = QuarterViewCameraRules.GetRotation(PITCH, YAW) * Vector3.forward;
            Vector3 focus   = Vector3.up * FOCUS_HEIGHT;

            Vector3 lookedAt = offset + (forward * DISTANCE);

            Assert.AreEqual(0f, (lookedAt - focus).magnitude, TOLERANCE);
            Assert.AreEqual(DISTANCE, (offset - focus).magnitude, TOLERANCE);
        }

        [Test]
        public void GetFollowOffset_ZeroYaw_PlacesCameraAboveAndBehind()
        {
            Vector3 offset = QuarterViewCameraRules.GetFollowOffset(PITCH, YAW, DISTANCE, FOCUS_HEIGHT);

            Assert.Greater(offset.y, FOCUS_HEIGHT);
            Assert.Less(offset.z, 0f);
            Assert.AreEqual(0f, offset.x, TOLERANCE);
        }

        [Test]
        public void StepZoomLevel_ScrollUp_MovesOneLevelCloser()
        {
            Assert.AreEqual(MIDDLE_LEVEL, QuarterViewCameraRules.StepZoomLevel(0, LARGE_SCROLL, LEVEL_COUNT));
        }

        [Test]
        public void StepZoomLevel_ScrollDown_MovesOneLevelFarther()
        {
            Assert.AreEqual(MIDDLE_LEVEL, QuarterViewCameraRules.StepZoomLevel(LEVEL_COUNT - 1, -NOTCH, LEVEL_COUNT));
        }

        [Test]
        public void StepZoomLevel_AtEnds_Clamps()
        {
            Assert.AreEqual(LEVEL_COUNT - 1, QuarterViewCameraRules.StepZoomLevel(LEVEL_COUNT - 1, NOTCH, LEVEL_COUNT));
            Assert.AreEqual(0, QuarterViewCameraRules.StepZoomLevel(0, -NOTCH, LEVEL_COUNT));
        }

        [Test]
        public void StepZoomLevel_NoScroll_KeepsLevel()
        {
            Assert.AreEqual(MIDDLE_LEVEL, QuarterViewCameraRules.StepZoomLevel(MIDDLE_LEVEL, 0f, LEVEL_COUNT));
        }

        [Test]
        public void Lerp_Halfway_AveragesAllValues()
        {
            var far  = new QuarterViewPose(PITCH, FIELD_OF_VIEW, DISTANCE, 0f);
            var near = new QuarterViewPose(0f, FIELD_OF_VIEW, 0f, FOCUS_HEIGHT);

            QuarterViewPose half = QuarterViewPose.Lerp(far, near, HALF);

            Assert.AreEqual(PITCH * HALF, half.PitchAngle, TOLERANCE);
            Assert.AreEqual(FIELD_OF_VIEW, half.FieldOfView, TOLERANCE);
            Assert.AreEqual(DISTANCE * HALF, half.Distance, TOLERANCE);
            Assert.AreEqual(FOCUS_HEIGHT * HALF, half.FocusHeight, TOLERANCE);
        }
    }
}
