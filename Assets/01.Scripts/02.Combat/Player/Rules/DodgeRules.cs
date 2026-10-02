// Unity
using UnityEngine;
using UnityEngine.AI;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 회피 도착점 계산, 서버 판정과 소유 클라이언트 예측이 같은 벽 판정을 쓰도록 한곳에 둔다 </summary>
    public static class DodgeRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 방향으로 거리만큼 가되 내비 메시 경계(벽) 직전에서 멈춘 도착점, 출발점이 메시 밖이면 거짓 </summary>
        public static bool TryGetWallClampedEnd(Vector3 origin, Vector3 direction, float distance, out Vector3 start,
            out Vector3 end)
        {
            start = origin;
            end   = origin;
            if (!NavMesh.SamplePosition(origin, out NavMeshHit originHit, Constants.Player.MOVE_DESTINATION_SAMPLE_RADIUS,
                NavMesh.AllAreas))
            {
                return false;
            }

            start = originHit.position;
            Vector3 target = start + (direction * distance);
            if (NavMesh.Raycast(start, target, out NavMeshHit wallHit, NavMesh.AllAreas))
            {
                end = wallHit.position;
                return true;
            }

            // 막히지 않았으면 경사면 높이를 메시에서 다시 읽는다
            end = NavMesh.SamplePosition(target, out NavMeshHit endHit, Constants.Player.MOVE_DESTINATION_SAMPLE_RADIUS, NavMesh.AllAreas)
                ? endHit.position
                : target;
            return true;
        }

        /// <summary> 수평 방향으로 나아가며 원형 몸체에 닿기 직전까지 갈 수 있는 거리, 뒤나 옆으로 비껴가는 몸체는 막지 않는다 </summary>
        public static float GetClearDistance(Vector3 origin, Vector3 direction, float distance, Vector3 center, float combinedRadius)
        {
            Vector3 toCenter = center - origin;
            toCenter.y = 0f;

            float along = Vector3.Dot(toCenter, direction);
            if (along <= 0f)
            {
                return distance;
            }

            float lateralSqr = toCenter.sqrMagnitude - (along * along);
            float radiusSqr  = combinedRadius * combinedRadius;
            if (lateralSqr >= radiusSqr)
            {
                return distance;
            }

            // 이미 겹쳐 있어도 앞으로 파고들지는 않는다
            float stop = along - Mathf.Sqrt(radiusSqr - lateralSqr);
            return Mathf.Clamp(stop, 0f, distance);
        }

        /// <summary> 회피 시작 뒤 경과 시간의 위치, 일정 속도로 직선 이동한다 </summary>
        public static Vector3 GetPosition(Vector3 start, Vector3 end, double elapsed, float duration)
        {
            if ((duration <= 0f) || (elapsed >= duration))
            {
                return end;
            }

            float t = (elapsed <= 0.0) ? 0f : (float)(elapsed / duration);
            return Vector3.Lerp(start, end, t);
        }
    }
}
