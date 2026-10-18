// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 미니맵 월드 좌표 변환, 원 가장자리 당김, 회전 변환 </summary>
    public sealed class MinimapRulesTests
    {
        private const float TOLERANCE = 0.0001f;

        [Test]
        public void UiPerMeter_ValidInputs_ReturnsDiameterOverTwiceRadius()
        {
            Assert.AreEqual(3.125f, MinimapRules.UiPerMeter(250f, 40f), TOLERANCE);
        }

        [Test]
        public void UiPerMeter_ZeroDiameterOrRadius_ReturnsZero()
        {
            Assert.AreEqual(0f, MinimapRules.UiPerMeter(0f, 40f), TOLERANCE);
            Assert.AreEqual(0f, MinimapRules.UiPerMeter(250f, 0f), TOLERANCE);
            Assert.AreEqual(0f, MinimapRules.UiPerMeter(-10f, 40f), TOLERANCE);
        }

        [Test]
        public void WorldToViewport_OffsetFromCenter_ScalesByUiPerMeter()
        {
            Vector2 position = MinimapRules.WorldToViewport(new Vector2(14f, 8f), new Vector2(10f, 10f), 2f);

            Assert.AreEqual(8f, position.x, TOLERANCE);
            Assert.AreEqual(-4f, position.y, TOLERANCE);
        }

        [Test]
        public void MapPosition_ViewCenterAtMapCenter_IsZero()
        {
            Vector2 position = MinimapRules.MapPosition(new Vector2(10f, 20f), new Vector2(10f, 20f), 2.5f);

            Assert.AreEqual(Vector2.zero, position);
        }

        [Test]
        public void MapPosition_ViewMovesEast_MapSlidesWest()
        {
            Vector2 position = MinimapRules.MapPosition(new Vector2(20f, 20f), new Vector2(10f, 20f), 2.5f);

            Assert.AreEqual(-25f, position.x, TOLERANCE);
            Assert.AreEqual(0f, position.y, TOLERANCE);
        }

        [Test]
        public void ClampToCircle_Inside_ReturnsSamePosition()
        {
            Vector2 position = MinimapRules.ClampToCircle(new Vector2(30f, 40f), 100f, out bool clamped);

            Assert.IsFalse(clamped);
            Assert.AreEqual(new Vector2(30f, 40f), position);
        }

        [Test]
        public void ClampToCircle_Outside_PullsToEdgeAlongSameDirection()
        {
            Vector2 position = MinimapRules.ClampToCircle(new Vector2(300f, 400f), 100f, out bool clamped);

            Assert.IsTrue(clamped);
            Assert.AreEqual(60f, position.x, TOLERANCE);
            Assert.AreEqual(80f, position.y, TOLERANCE);
        }

        [Test]
        public void ClampToCircle_ZeroRadius_ReturnsInputUnclamped()
        {
            Vector2 position = MinimapRules.ClampToCircle(new Vector2(5f, 5f), 0f, out bool clamped);

            Assert.IsFalse(clamped);
            Assert.AreEqual(new Vector2(5f, 5f), position);
        }

        [Test]
        public void YawToMarkerRotation_Yaw90_ReturnsMinus90()
        {
            Assert.AreEqual(-90f, MinimapRules.YawToMarkerRotation(90f), TOLERANCE);
            Assert.AreEqual(0f, MinimapRules.YawToMarkerRotation(0f), TOLERANCE);
        }

        [Test]
        public void MarkerRadius_InsetLargerThanRadius_ClampsToZero()
        {
            Assert.AreEqual(115f, MinimapRules.MarkerRadius(250f, 10f), TOLERANCE);
            Assert.AreEqual(0f, MinimapRules.MarkerRadius(10f, 20f), TOLERANCE);
        }

        [Test]
        public void ToXz_DropsHeight()
        {
            Assert.AreEqual(new Vector2(3f, 5f), MinimapRules.ToXz(new Vector3(3f, 99f, 5f)));
        }

        [Test]
        public void MapSide_HalfExtentAndScale_ReturnsFullSide()
        {
            Assert.AreEqual(300f, MinimapRules.MapSide(60f, 2.5f), TOLERANCE);
        }
    }
}
