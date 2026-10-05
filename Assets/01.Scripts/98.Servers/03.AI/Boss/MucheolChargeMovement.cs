// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 기존 돌진 실행(BossDashPattern)은 벽 앞 거리와 이동 결과만 보고 멈춘다. 구조물도 같은 경로로 알리도록 이동 창구를 감싸
    // 구조물 앞 거리를 벽 앞 거리와 합친다. 이렇게 하면 돌진 실행을 고치지 않고 구조물에서도 즉시 멈추고 이후 이동과 타격이 없다.
    // 구조물 조회가 없으면 안쪽 이동 창구와 똑같이 동작한다.
    //
    // 무엇이 이동을 제한했는지는 제한한 순간에 기록한다. 멈춘 뒤에 구조물을 다시 조회하면 구조물이 그 사이 사라졌을 때
    // 구조물 앞에서 멈췄는데도 벽으로 보고된다. 기록은 두 곳에서 갱신한다.
    //  - 거리 측정(GetClearDistance): 새 경로를 재는 것이므로 기록을 통째로 바꾼다. 돌진 실행이 실행 시작 때 마지막으로 잰 값이 돌진 길이다.
    //  - 이동(Move): 실행 중에 새로 생긴 구조물이나 측정 때 몰랐던 벽이 더 일찍 막으면 그 원인으로 바꾼다.
    //    이동은 측정한 길이 안에서만 일어나므로 이동 중에 막은 것은 언제나 기록된 원인보다 앞선다
    /// <summary> 안쪽 이동 창구의 벽 검사에 구조물 검사를 더한 이동 창구, 이동을 제한한 원인과 구조물 식별자를 남긴다 </summary>
    public sealed class MucheolChargeMovement : IBossMovement
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float STOP_EPSILON    = 0.001f; // m, 도착과 막힘을 가르는 오차
        private const int   NO_STRUCTURE_ID = 0;      // 구조물이 아닌 제한의 구조물 식별자

        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossMovement             _inner;
        private readonly IBossChargeStructureQuery _structures;

        private EBossChargeObstacleKind _limitKind        = EBossChargeObstacleKind.None;
        private int                     _limitStructureId = NO_STRUCTURE_ID;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 잰 경로를 제한한 원인, 제한이 없으면 None이다 </summary>
        public EBossChargeObstacleKind LimitKind => _limitKind;

        /// <summary> 구조물이 제한했으면 그 식별자, 아니면 0이다 </summary>
        public int LimitStructureId => _limitStructureId;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolChargeMovement(IBossMovement inner, IBossChargeStructureQuery structures = null)
        {
            if (inner == null)
            {
                throw new ArgumentNullException(nameof(inner));
            }

            _inner      = inner;
            _structures = structures;
        }

        /// <summary> 기록한 제한 원인을 지운다, 새 시전이 시작될 때 부른다 </summary>
        public void ResetLimit()
        {
            _limitKind        = EBossChargeObstacleKind.None;
            _limitStructureId = NO_STRUCTURE_ID;
        }

        // 벽 앞 거리와 구조물 앞 거리 중 짧은 쪽이다. 구조물은 벽 너머를 볼 필요가 없어 벽 앞 거리까지만 묻는다.
        // 구조물이 벽 앞 거리 안에 있으면(같은 거리 포함) 구조물이 제한한 것이고, 구조물이 없고 벽이 최대 거리보다 가까우면 벽이다
        public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
        {
            float clear = _inner.GetClearDistance(origin, direction, maxDistance, bodyRadius);

            if ((_structures != null) && (_structures.TryGetFirstStructure(origin, direction, clear, bodyRadius, out BossChargeStructureHit hit)))
            {
                _limitKind        = EBossChargeObstacleKind.Structure;
                _limitStructureId = hit.StructureId;
                return Mathf.Clamp(hit.Distance, 0f, clear);
            }

            if (clear < (maxDistance - STOP_EPSILON))
            {
                _limitKind        = EBossChargeObstacleKind.Wall;
                _limitStructureId = NO_STRUCTURE_ID;
            }
            else
            {
                ResetLimit();
            }

            return clear;
        }

        // 구조물이 이동 도중에 생긴 경우(예고 때는 없었던 경우)도 여기서 막는다. 구조물 앞까지만 안쪽 창구에 이동을 부탁한다.
        // 안쪽 창구가 그보다 일찍 멈추면(측정 때 몰랐던 벽) 더 앞선 원인인 벽으로 기록한다
        public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
        {
            bool clampedByStructure = false;
            int  structureId        = NO_STRUCTURE_ID;

            if (_structures != null)
            {
                Vector3 delta    = to - from;
                float   distance = delta.magnitude;
                if (distance > 0f)
                {
                    Vector3 direction = delta / distance;
                    if (_structures.TryGetFirstStructure(from, direction, distance, bodyRadius, out BossChargeStructureHit hit))
                    {
                        to                 = from + (direction * Mathf.Clamp(hit.Distance, 0f, distance));
                        clampedByStructure = true;
                        structureId        = hit.StructureId;
                    }
                }
            }

            Vector3 reached = _inner.Move(from, to, bodyRadius);

            if ((reached - to).sqrMagnitude > (STOP_EPSILON * STOP_EPSILON))
            {
                _limitKind        = EBossChargeObstacleKind.Wall;
                _limitStructureId = NO_STRUCTURE_ID;
            }
            else if (clampedByStructure)
            {
                _limitKind        = EBossChargeObstacleKind.Structure;
                _limitStructureId = structureId;
            }

            return reached;
        }

        /// <summary> 정의의 거리를 채웠는가, 오차 안이면 막히지 않은 것으로 본다 </summary>
        public static bool ReachedFullRange(float travelDistance, float range)
        {
            return travelDistance >= (range - STOP_EPSILON);
        }
    }
}
