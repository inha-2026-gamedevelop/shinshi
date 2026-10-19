// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Dev
{
    /// <summary> 내비 메시가 없는 시험 씬에서 기술의 착지 지점과 벽 앞 이동을 물리 충돌체로 대신 판정한다 </summary>
    public sealed class DevSkillNavigation : ISkillNavigation
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float PROBE_HEIGHT   = 50f;   // m, 지면을 찾으려고 위에서 쏘는 높이
        private const float PROBE_DISTANCE = 120f;  // m
        private const float WALL_HEIGHT    = 0.6f;  // m, 벽을 보는 높이
        private const float BODY_RADIUS    = 0.4f;  // m, 벽 앞에서 멈출 여유
        private const float WALKABLE_NORMAL_Y = 0.7f; // 이보다 가파르면 벽으로 본다
        private const float MIN_DISTANCE_SQR  = 0.0001f;

        private readonly Transform _self;

        /****************************************
        *                Methods
        ****************************************/

        public DevSkillNavigation(Transform self)
        {
            _self = self;
        }

        public bool TrySample(Vector3 point, out Vector3 sampled)
        {
            sampled = point;
            RaycastHit[] hits = Physics.RaycastAll(point + (Vector3.up * PROBE_HEIGHT), Vector3.down, PROBE_DISTANCE, ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; ++i)
            {
                if ((IsIgnored(hits[i].collider)) || (hits[i].normal.y < WALKABLE_NORMAL_Y))
                {
                    continue;
                }

                sampled = hits[i].point;
                return true;
            }

            return false;
        }

        public Vector3 Move(Vector3 origin, Vector3 destination)
        {
            Vector3 flat = destination - origin;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance * distance < MIN_DISTANCE_SQR)
            {
                return origin;
            }

            Vector3 direction = flat / distance;
            float   reach     = distance;

            RaycastHit[] hits = Physics.RaycastAll(origin + (Vector3.up * WALL_HEIGHT), direction, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; ++i)
            {
                if ((IsIgnored(hits[i].collider)) || (hits[i].normal.y >= WALKABLE_NORMAL_Y))
                {
                    continue;
                }

                reach = Mathf.Min(reach, Mathf.Max(0f, hits[i].distance - BODY_RADIUS));
            }

            Vector3 target = origin + (direction * reach);
            return TrySample(target, out Vector3 grounded) ? grounded : origin;
        }

        // 자기 몸과 몬스터는 벽도 바닥도 아니다
        private bool IsIgnored(Collider collider)
        {
            return (collider.transform.IsChildOf(_self)) || (collider.GetComponentInParent<DevHitFeedbackMonster>() != null);
        }
    }
}
