// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 월드 좌표를 미니맵 화면 좌표로 바꾸는 순수 계산 </summary>
    public static class MinimapRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_VIEW_RADIUS = 0.01f;
        private const float HALF            = 0.5f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 월드 1미터가 화면에서 차지하는 UI 단위, 반경이 0 이하면 0이다 </summary>
        public static float UiPerMeter(float viewportDiameter, float viewRadiusMeters)
        {
            if ((viewportDiameter <= 0f) || (viewRadiusMeters < MIN_VIEW_RADIUS))
            {
                return 0f;
            }

            return viewportDiameter / (viewRadiusMeters * 2f);
        }

        /// <summary> 월드 X, Z를 보는 중심 기준의 창 좌표로 옮긴다 </summary>
        public static Vector2 WorldToViewport(Vector2 worldXz, Vector2 centerXz, float uiPerMeter)
        {
            return (worldXz - centerXz) * uiPerMeter;
        }

        /// <summary> 지도 그림의 중심이 놓일 창 좌표, 보는 중심이 창 가운데에 오도록 그림을 반대로 민다 </summary>
        public static Vector2 MapPosition(Vector2 centerXz, Vector2 mapCenterXz, float uiPerMeter)
        {
            return (mapCenterXz - centerXz) * uiPerMeter;
        }

        /// <summary> 창 밖으로 나간 표시를 원 가장자리로 당긴다, 당겼으면 clamped가 참이다 </summary>
        public static Vector2 ClampToCircle(Vector2 viewportPosition, float radius, out bool clamped)
        {
            float length = viewportPosition.magnitude;
            if ((radius <= 0f) || (length <= radius))
            {
                clamped = false;
                return viewportPosition;
            }

            clamped = true;
            return viewportPosition * (radius / length);
        }

        /// <summary> 월드 Y 회전(위에서 볼 때 시계 방향 양수)을 UI Z 회전(반시계 양수)으로 바꾼다 </summary>
        public static float YawToMarkerRotation(float yawDegrees)
        {
            return -yawDegrees;
        }

        /// <summary> 수평 바라보는 방향을 월드 Y 회전 각도(+Z가 0도, 위에서 볼 때 시계 방향 양수)로 바꾼다 </summary>
        public static float DirectionToYaw(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        public static Vector2 ToXz(Vector3 world)
        {
            return new Vector2(world.x, world.z);
        }

        /// <summary> 그림 한 변의 UI 길이 </summary>
        public static float MapSide(float worldHalfExtent, float uiPerMeter)
        {
            return worldHalfExtent * 2f * uiPerMeter;
        }

        /// <summary> 창 반지름에서 표시 안쪽 여백을 뺀 길이, 0 아래로 내려가지 않는다 </summary>
        public static float MarkerRadius(float viewportDiameter, float inset)
        {
            return Mathf.Max(0f, (viewportDiameter * HALF) - inset);
        }
    }
}
