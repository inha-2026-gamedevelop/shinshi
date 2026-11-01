// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class CameraCompositionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float PITCH         = 55f;
        private const float YAW           = 0f;
        private const float DISTANCE      = 17f;
        private const float FOCUS_HEIGHT  = 1f;
        private const float TOLERANCE     = 0.0001f;
        private const float SLIDER_MIN    = 0f;
        private const float SLIDER_MAX    = 1f;
        private const float SAVED_PITCH   = 0.2f;
        private const float SAVED_DIST    = 0.9f;
        private const float HIGH_PITCH    = 85f;
        private const float LOW_PITCH     = 5f;
        private const float PITCH_OFFSET  = 10f;
        private const float PITCH_CEILING = 89f;
        private const float PITCH_FLOOR   = 3f;
        private const float DEFAULT_SCALE = 1f;

        /****************************************
        *                Setup
        ****************************************/

        [TearDown]
        public void ResetComposition()
        {
            CameraComposition.Preview(CameraComposition.DEFAULT_RATE, CameraComposition.DEFAULT_RATE);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void DefaultRate_KeepsZoomLevelPose()
        {
            CameraComposition.Preview(CameraComposition.DEFAULT_RATE, CameraComposition.DEFAULT_RATE);

            Assert.AreEqual(SLIDER_MIN, CameraComposition.PitchOffset, TOLERANCE);
            Assert.AreEqual(DEFAULT_SCALE, CameraComposition.DistanceScale, TOLERANCE);
        }

        [Test]
        public void Preview_SliderEnds_MapToRange()
        {
            CameraComposition.Preview(SLIDER_MIN, SLIDER_MIN);
            Assert.AreEqual(-CameraComposition.MAX_PITCH_OFFSET, CameraComposition.PitchOffset, TOLERANCE);
            Assert.AreEqual(CameraComposition.MIN_DISTANCE_SCALE, CameraComposition.DistanceScale, TOLERANCE);

            CameraComposition.Preview(SLIDER_MAX, SLIDER_MAX);
            Assert.AreEqual(CameraComposition.MAX_PITCH_OFFSET, CameraComposition.PitchOffset, TOLERANCE);
            Assert.AreEqual(CameraComposition.MAX_DISTANCE_SCALE, CameraComposition.DistanceScale, TOLERANCE);
        }

        [Test]
        public void Preview_Change_RaisesChangedOnce_SameValueDoesNot()
        {
            int count = 0;
            System.Action handler = () => count++;
            CameraComposition.Changed += handler;
            try
            {
                CameraComposition.Preview(SLIDER_MAX, CameraComposition.DEFAULT_RATE);
                CameraComposition.Preview(SLIDER_MAX, CameraComposition.DEFAULT_RATE);
                Assert.AreEqual(1, count);
            }
            finally
            {
                CameraComposition.Changed -= handler;
            }
        }

        [Test]
        public void SaveAndLoad_RoundTripsAndRepairsInvalid()
        {
            float savedPitch    = CameraComposition.LoadPitch();
            float savedDistance = CameraComposition.LoadDistance();
            try
            {
                CameraComposition.Save(SAVED_PITCH, SAVED_DIST);
                Assert.AreEqual(SAVED_PITCH, CameraComposition.LoadPitch(), TOLERANCE);
                Assert.AreEqual(SAVED_DIST, CameraComposition.LoadDistance(), TOLERANCE);

                PlayerPrefs.SetFloat(CameraComposition.PITCH_PREFS_KEY, float.NaN);
                Assert.AreEqual(CameraComposition.DEFAULT_RATE, CameraComposition.LoadPitch(), TOLERANCE);
            }
            finally
            {
                CameraComposition.Save(savedPitch, savedDistance);
            }
        }

        [Test]
        public void ApplyToRig_HigherPitchAndLongerDistance_MovesCameraUpAndBack()
        {
            Vector3 baseOffset = QuarterViewCameraRules.GetFollowOffset(PITCH, YAW, DISTANCE, FOCUS_HEIGHT);

            CameraComposition.Preview(SLIDER_MAX, SLIDER_MAX);
            float pitch    = QuarterViewCameraRules.GetAdjustedPitch(PITCH, CameraComposition.PitchOffset);
            float distance = DISTANCE * CameraComposition.DistanceScale;
            Vector3 offset = QuarterViewCameraRules.GetFollowOffset(pitch, YAW, distance, FOCUS_HEIGHT);

            Assert.Greater(pitch, PITCH);
            Assert.Greater(offset.magnitude, baseOffset.magnitude);
            Assert.Greater(offset.y, baseOffset.y);
        }

        [Test]
        public void GetAdjustedPitch_ClampsToSafeRange()
        {
            Assert.AreEqual(PITCH_CEILING, QuarterViewCameraRules.GetAdjustedPitch(HIGH_PITCH, PITCH_OFFSET), TOLERANCE);
            Assert.AreEqual(PITCH_FLOOR, QuarterViewCameraRules.GetAdjustedPitch(LOW_PITCH, -PITCH_OFFSET), TOLERANCE);
        }
    }
}
