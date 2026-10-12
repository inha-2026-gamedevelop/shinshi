// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    // 몸체 반경의 구를 수평으로 쓸어 정적 지형(장애물 레이어)에 닿기 전까지의 거리를 잰다. 플레이어와 소환물은 보지 않는다.
    //
    // 물리 스윕은 시작 위치에서 이미 겹친 콜라이더를 그 콜라이더 전체로 무시한다. 벽 여러 개가 한 메시 콜라이더로 묶여 있으면
    // 겹친 벽 하나 때문에 앞쪽 벽까지 통과하므로 시작이 겹치지 않게 하는 쪽으로 막는다. 닿은 곳에서 벽 법선 방향으로 여유(SKIN)만큼
    // 떨어진 지점에 멈춰, 비스듬히 닿아도 다음 스윕이 겹친 채 시작하지 않게 한다. 처음부터 벽 안에 놓인 몸체는 막지 못하므로
    // 보스 생성 지점은 벽에서 몸체 반경 이상 떨어뜨린다.
    //
    // 구의 중심은 발 위로 반경과 바닥 여유만큼 올려 평평한 바닥과 겹치지 않게 한다. 바닥은 장애물 레이어에 두지 않는 것을 전제로 하되,
    // 실수로 두어도 평평한 바닥은 막지 않는다. 실제 전장 지형에서의 동작은 지형 배치 뒤에 다시 확인해야 한다
    /// <summary> 보스 몸체와 투사체가 쓰는 정적 지형 벽 검사, 할당 없는 구 스윕 하나로 잰다 </summary>
    public sealed class BossTerrainProbe : IBossWallQuery, IBossStandQuery
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float SKIN              = 0.05f; // m, 벽 앞에 남기는 틈
        private const float FOOT_CLEARANCE    = 0.25f; // m, 구의 아래 끝을 발보다 올리는 높이
        private const float MIN_PROBE_RADIUS  = 0.05f; // m, 반경이 0이어도 선이 아니라 작은 구로 잰다
        private const float MIN_APPROACH_COS  = 0.1f;  // 스치듯 닿을 때 법선 여유를 진행 거리로 바꾸는 배율의 상한(10배)
        private const float MIN_DIRECTION_SQR = 0.000001f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly int _layerMask;

        /****************************************
        *              Properties
        ****************************************/

        // 거짓이면 장애물 레이어가 없어 벽을 검사하지 않는다
        public bool IsEnabled => _layerMask != 0;

        /****************************************
        *                Methods
        ****************************************/

        public BossTerrainProbe(int layerMask)
        {
            _layerMask = layerMask;
        }

        public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
        {
            if ((_layerMask == 0) || (maxDistance <= 0f))
            {
                return Mathf.Max(0f, maxDistance);
            }

            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            float   sqr  = flat.sqrMagnitude;
            if (sqr <= MIN_DIRECTION_SQR)
            {
                return 0f;
            }

            flat /= Mathf.Sqrt(sqr);

            float   radius = Mathf.Max(MIN_PROBE_RADIUS, bodyRadius - SKIN);
            Vector3 center = origin + (Vector3.up * (radius + FOOT_CLEARANCE));

            if (!Physics.SphereCast(center, radius, flat, out RaycastHit hit, maxDistance + SKIN, _layerMask,
                QueryTriggerInteraction.Ignore))
            {
                return maxDistance;
            }

            float approachCos = Mathf.Max(MIN_APPROACH_COS, -Vector3.Dot(flat, hit.normal));
            return Mathf.Clamp(hit.distance - (SKIN / approachCos), 0f, maxDistance);
        }

        // 순간 이동의 도착 지점 검사다. 스윕과 같은 구(반경, 중심 높이)로 겹침만 본다. 스윕은 시작부터 겹친 콜라이더를 무시하므로
        // 이 검사가 있어야 벽 안으로 건너뛰지 않는다. 장애물 레이어가 없으면 막을 벽이 없다
        public bool CanStand(Vector3 point, float bodyRadius)
        {
            if (_layerMask == 0)
            {
                return true;
            }

            float   radius = Mathf.Max(MIN_PROBE_RADIUS, bodyRadius - SKIN);
            Vector3 center = point + (Vector3.up * (radius + FOOT_CLEARANCE));

            return !Physics.CheckSphere(center, radius, _layerMask, QueryTriggerInteraction.Ignore);
        }
    }
}
