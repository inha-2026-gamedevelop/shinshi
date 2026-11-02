// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 선 렌더러에 넘길 점을 미리 만든 버퍼에 채운다. 매 프레임 불려도 할당하지 않는다
    /// <summary> 일반 적 예고 모양별 윤곽 점 계산 </summary>
    public static class EnemyTelegraphGeometry
    {
        /****************************************
        *              Constants
        ****************************************/

        public const int   ARC_SEGMENTS       = 24;
        public const int   RECT_POINTS        = 5;
        public const int   MAX_POINTS         = ARC_SEGMENTS + 3;
        public const float LIFT               = 0.05f; // m, 바닥에 묻히지 않게 띄우는 높이
        public const float FILL_LIFT          = 0.04f; // m, 채움 면이 윤곽선을 덮지 않게 조금 낮춘다
        public const int   RECT_AREA_VERTICES = 4;
        public const int   FAN_AREA_VERTICES  = ARC_SEGMENTS + 2;
        public const int   MAX_AREA_VERTICES  = FAN_AREA_VERTICES;
        public const int   RECT_AREA_INDICES  = 6;
        public const int   FAN_AREA_INDICES   = ARC_SEGMENTS * 3;
        public const int   MAX_AREA_INDICES   = FAN_AREA_INDICES;

        private const float HALF              = 0.5f;
        private const float FULL_CIRCLE       = 360f;
        private const float MIN_DIRECTION_SQR = 0.000001f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 모양에 맞는 윤곽을 buffer에 채우고 점 수를 돌려준다, 그릴 수 없으면 0이다 </summary>
        public static int Fill(Vector3[] buffer, in EnemyTelegraphCue cue)
        {
            if ((buffer == null) || (buffer.Length < MAX_POINTS) || (!(cue.Range > 0f)))
            {
                return 0;
            }

            Vector3 origin = cue.Origin + (Vector3.up * LIFT);
            Vector3 flat   = new Vector3(cue.Direction.x, 0f, cue.Direction.z);
            if (flat.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                flat = Vector3.forward;
            }

            flat.Normalize();

            switch (cue.Shape)
            {
                case EEnemyAttackShape.Fan:
                    return FillFan(buffer, origin, flat, cue.Range, cue.Width);
                case EEnemyAttackShape.Circle:
                case EEnemyAttackShape.TargetArea:
                    return FillCircle(buffer, origin, cue.Range);
                default:
                    return FillRectangle(buffer, origin, flat, cue.Range, cue.Width);
            }
        }

        // 진행도만큼 시작점에서 앞쪽(직선, 돌진)이나 바깥쪽(부채꼴, 원형)으로 자란 채움 면이다.
        // 직선은 [뒤 좌, 앞 좌, 앞 우, 뒤 우], 부채꼴과 원형은 [중심, 가장자리 ...] 순서로 채운다
        /// <summary> 진행도 0에서 1만큼 채워진 면의 꼭짓점을 buffer에 채우고 수를 돌려준다, 그릴 수 없으면 0이다 </summary>
        public static int FillArea(Vector3[] buffer, in EnemyTelegraphCue cue, float progress)
        {
            float amount = Mathf.Clamp01(progress);
            if ((buffer == null) || (buffer.Length < MAX_AREA_VERTICES) || (!(cue.Range > 0f)) || (!(amount > 0f)))
            {
                return 0;
            }

            Vector3 origin = cue.Origin + (Vector3.up * FILL_LIFT);
            Vector3 flat   = new Vector3(cue.Direction.x, 0f, cue.Direction.z);
            if (flat.sqrMagnitude < MIN_DIRECTION_SQR)
            {
                flat = Vector3.forward;
            }

            flat.Normalize();
            float reach = cue.Range * amount;

            switch (cue.Shape)
            {
                case EEnemyAttackShape.Fan:
                    return FillFanArea(buffer, origin, flat, reach, cue.Width);
                case EEnemyAttackShape.Circle:
                case EEnemyAttackShape.TargetArea:
                    return FillFanArea(buffer, origin, flat, reach, FULL_CIRCLE);
                default:
                    return FillRectangleArea(buffer, origin, flat, reach, cue.Width);
            }
        }

        /// <summary> 직선 모양 채움인지, 아니면 중심에서 퍼지는 부채꼴 채움인지 </summary>
        public static bool UsesRectangleArea(EEnemyAttackShape shape)
        {
            return (shape != EEnemyAttackShape.Fan) && (shape != EEnemyAttackShape.Circle) && (shape != EEnemyAttackShape.TargetArea);
        }

        /// <summary> FillArea 꼭짓점 순서에 맞는 삼각형 번호를 채우고 수를 돌려준다 </summary>
        public static int FillAreaIndices(int[] buffer, bool isRectangle)
        {
            if ((buffer == null) || (buffer.Length < MAX_AREA_INDICES))
            {
                return 0;
            }

            if (isRectangle)
            {
                buffer[0] = 0;
                buffer[1] = 1;
                buffer[2] = 2;
                buffer[3] = 0;
                buffer[4] = 2;
                buffer[5] = 3;
                return RECT_AREA_INDICES;
            }

            for (int i = 0; i < ARC_SEGMENTS; ++i)
            {
                int index = i * 3;
                buffer[index]     = 0;
                buffer[index + 1] = i + 1;
                buffer[index + 2] = i + 2;
            }

            return FAN_AREA_INDICES;
        }

        /// <summary> 채움 면에서 앞쪽(진행 끝) 꼭짓점인지, 앞쪽을 진하게 칠하는 데 쓴다 </summary>
        public static bool IsAreaFrontVertex(bool isRectangle, int index)
        {
            return isRectangle ? ((index == 1) || (index == 2)) : (index > 0);
        }

        private static int FillRectangleArea(Vector3[] buffer, Vector3 start, Vector3 direction, float length, float width)
        {
            Vector3 side = new Vector3(direction.z, 0f, -direction.x) * (width * HALF);
            Vector3 end  = start + (direction * length);

            buffer[0] = start + side;
            buffer[1] = end + side;
            buffer[2] = end - side;
            buffer[3] = start - side;
            return RECT_AREA_VERTICES;
        }

        private static int FillFanArea(Vector3[] buffer, Vector3 origin, Vector3 forward, float radius, float arcAngle)
        {
            float half = Mathf.Clamp(arcAngle, 0f, FULL_CIRCLE) * HALF;

            buffer[0] = origin;
            for (int i = 0; i <= ARC_SEGMENTS; ++i)
            {
                float angle = Mathf.Lerp(-half, half, (float)i / ARC_SEGMENTS);
                buffer[i + 1] = origin + (Quaternion.AngleAxis(angle, Vector3.up) * forward * radius);
            }

            return FAN_AREA_VERTICES;
        }

        public static int FillFan(Vector3[] buffer, Vector3 origin, Vector3 forward, float range, float arcAngle)
        {
            float half = Mathf.Clamp(arcAngle, 0f, FULL_CIRCLE) * HALF;

            buffer[0] = origin;
            for (int i = 0; i <= ARC_SEGMENTS; ++i)
            {
                float angle = Mathf.Lerp(-half, half, (float)i / ARC_SEGMENTS);
                buffer[i + 1] = origin + (Quaternion.AngleAxis(angle, Vector3.up) * forward * range);
            }

            buffer[ARC_SEGMENTS + 2] = origin;
            return ARC_SEGMENTS + 3;
        }

        public static int FillCircle(Vector3[] buffer, Vector3 center, float radius)
        {
            for (int i = 0; i <= ARC_SEGMENTS; ++i)
            {
                float angle = (FULL_CIRCLE * i / ARC_SEGMENTS) * Mathf.Deg2Rad;
                buffer[i] = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            return ARC_SEGMENTS + 1;
        }

        // 시작점에서 방향으로 length, 좌우 각각 width의 절반
        public static int FillRectangle(Vector3[] buffer, Vector3 start, Vector3 direction, float length, float width)
        {
            Vector3 side = new Vector3(direction.z, 0f, -direction.x) * (width * HALF);
            Vector3 end  = start + (direction * length);

            buffer[0] = start + side;
            buffer[1] = end + side;
            buffer[2] = end - side;
            buffer[3] = start - side;
            buffer[4] = start + side;
            return RECT_POINTS;
        }
    }
}
