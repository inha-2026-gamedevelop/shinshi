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

        public const int   ARC_SEGMENTS = 24;
        public const int   RECT_POINTS  = 5;
        public const int   MAX_POINTS   = ARC_SEGMENTS + 3;
        public const float LIFT         = 0.05f; // m, 바닥에 묻히지 않게 띄우는 높이

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
