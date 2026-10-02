// Unity
using UnityEngine;

namespace Shinshi.Common
{
    /// <summary> 카메라에서 초점까지 이어진 원뿔 안의 가림을 비우는 범위, 셰이더와 클릭 판정이 같은 식을 쓴다 </summary>
    public readonly struct OcclusionFadeShape
    {
        /****************************************
        *              Constants
        ****************************************/

        // 카메라와 초점이 겹치면 방향을 정할 수 없어 비우지 않는다
        private const float MIN_FOCUS_DISTANCE = 0.0001f;

        // 가장자리 흐림이 0이어도 두 경계가 같아지지 않게 둔다
        private const float MIN_EDGE_WIDTH = 0.0001f;

        /****************************************
        *                Fields
        ****************************************/

        public Vector3 CameraPosition { get; }
        public Vector3 FocusPoint     { get; }
        public float   Radius         { get; } // m, 초점 깊이에서 비우는 원의 반지름
        public float   EdgeSoftness   { get; } // 0에서 1 사이 비율
        public float   TargetMargin   { get; } // m, 초점 바로 앞 이 거리 안은 비우지 않는다
        public float   FloorHeight    { get; } // 월드 y, 이 높이 아래는 비우지 않는다
        public float   MinVisibility  { get; } // 0에서 1 사이 비율

        /****************************************
        *                Methods
        ****************************************/

        public OcclusionFadeShape(Vector3 cameraPosition, Vector3 focusPoint, float radius, float edgeSoftness,
            float targetMargin, float floorHeight, float minVisibility)
        {
            CameraPosition = cameraPosition;
            FocusPoint     = focusPoint;
            Radius         = radius;
            EdgeSoftness   = edgeSoftness;
            TargetMargin   = targetMargin;
            FloorHeight    = floorHeight;
            MinVisibility  = minVisibility;
        }

        // 셰이더 ShinshiOcclusionFade.hlsl의 ShinshiOcclusionVisibility와 같은 식이다. 한쪽만 고치면
        // 화면에서 비어 보이는 곳과 클릭이 통과하는 곳이 어긋난다
        /// <summary> 이 지점에 남는 픽셀 비율, 1이면 비우지 않는다 </summary>
        public float GetVisibility(Vector3 point)
        {
            if (point.y < FloorHeight)
            {
                return 1f;
            }

            Vector3 toFocus       = FocusPoint - CameraPosition;
            float   focusDistance = toFocus.magnitude;
            if (focusDistance <= MIN_FOCUS_DISTANCE)
            {
                return 1f;
            }

            Vector3 direction = toFocus / focusDistance;
            Vector3 toPoint   = point - CameraPosition;
            float   along     = Vector3.Dot(toPoint, direction);
            if ((along <= 0f) || (along >= (focusDistance - TargetMargin)))
            {
                return 1f;
            }

            float coneRadius  = Radius * (along / focusDistance);
            float innerRadius = Mathf.Min(coneRadius * (1f - EdgeSoftness), coneRadius - MIN_EDGE_WIDTH);
            float offAxis     = (toPoint - (direction * along)).magnitude;
            float edge        = SmoothStep(innerRadius, coneRadius, offAxis);

            return MinVisibility + ((1f - MinVisibility) * edge);
        }

        // HLSL smoothstep과 같은 에르미트 곡선이다. Mathf.SmoothStep은 인자 뜻이 달라 쓰지 않는다
        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - (2f * t));
        }
    }
}
