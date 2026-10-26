// Unity
using UnityEngine;
using UnityEngine.Rendering;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    /// <summary> 툰 후처리 볼륨의 기본값이 꺼짐인지와 환경광 L0 계산이 구 전체 평균과 같은지 검증한다 </summary>
    public class ToonLookVolumeTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float TOLERANCE        = 0.02f;
        private const int   SPHERE_SAMPLES   = 4096;
        private const float GOLDEN_ANGLE_RAD = 2.39996323f;

        /****************************************
        *                Tests
        ****************************************/

        // 볼륨 프로필에 구성 요소를 넣기 전까지 화면이 바뀌지 않아야 한다
        [Test]
        public void NewVolume_IntensitiesAreZeroAndInactive()
        {
            ToonLookVolume volume = ScriptableObject.CreateInstance<ToonLookVolume>();

            try
            {
                Assert.AreEqual(0f, volume.LightingIntensity);
                Assert.AreEqual(0f, volume.EdgeIntensity);
                Assert.IsFalse(volume.IsActive());
                Assert.LessOrEqual(volume.RatioMin, volume.RatioMax);
            }
            finally
            {
                Object.DestroyImmediate(volume);
            }
        }

        [Test]
        public void ComputeAmbientL0_ConstantProbe_ReturnsSameColor()
        {
            Color ambient = new Color(0.2f, 0.4f, 0.6f, 1f);
            SphericalHarmonicsL2 probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(ambient);

            Color result = ToonLightingRendererFeature.ComputeAmbientL0(probe);

            AssertColorNear(ambient, result);
        }

        // 방향이 있는 SH에서도 L0는 구 전체에서 고르게 본 평균과 같아야 한다
        [Test]
        public void ComputeAmbientL0_DirectionalProbe_MatchesSphereAverage()
        {
            SphericalHarmonicsL2 probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(new Color(0.1f, 0.1f, 0.15f, 1f));
            probe.AddDirectionalLight(new Vector3(0.3f, 0.8f, -0.5f).normalized, new Color(1f, 0.8f, 0.6f, 1f), 1f);

            Color result   = ToonLightingRendererFeature.ComputeAmbientL0(probe);
            Color expected = SphereAverage(probe);

            AssertColorNear(expected, result);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static Color SphereAverage(SphericalHarmonicsL2 probe)
        {
            Vector3[] directions = new Vector3[SPHERE_SAMPLES];
            Color[]   results    = new Color[SPHERE_SAMPLES];

            for (int i = 0; i < SPHERE_SAMPLES; i++)
            {
                float y      = 1f - 2f * (i + 0.5f) / SPHERE_SAMPLES;
                float radius = Mathf.Sqrt(1f - y * y);
                float angle  = GOLDEN_ANGLE_RAD * i;
                directions[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            }

            probe.Evaluate(directions, results);

            Color sum = Color.black;
            for (int i = 0; i < SPHERE_SAMPLES; i++)
            {
                sum += results[i];
            }

            return sum / SPHERE_SAMPLES;
        }

        private static void AssertColorNear(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, TOLERANCE, "r");
            Assert.AreEqual(expected.g, actual.g, TOLERANCE, "g");
            Assert.AreEqual(expected.b, actual.b, TOLERANCE, "b");
        }
    }
}
