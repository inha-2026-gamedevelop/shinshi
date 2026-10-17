// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

namespace Shinshi.Combat
{
    // 무철 돌진의 구조물 조회를 물리 엔진 없이 계산한다. 구조물은 높이가 무한한 수직 원기둥, 보스 몸체는 같은 높이의 수평 원판이라
    // 수평면에서 원과 원의 접촉으로 줄어든다. 몸체 중심이 반경 합(구조물 반경 + 몸체 반경)인 원에 처음 닿는 거리를 2차식으로 푼다.
    // 물리 조회(SphereCast)를 쓰지 않는 이유: 서버 판정이 콜라이더 배치, 레이어, 물리 갱신 시점에 기대지 않아야 하고
    // 파괴된 구조물이 같은 틱 안에서 바로 빠져야 하기 때문이다. 조회는 호출마다 할당하지 않는다.
    //
    // 이미 반경 합 안에 있으면(멈춘 자리의 부동소수 오차 포함) 중심 쪽으로 향할 때만 거리 0으로 막고, 바깥이나 옆으로 향하면 막지 않는다.
    // 그래야 구조물 앞에 멈춘 보스가 다음 돌진에서 그 구조물에 갇히지 않는다. 같은 거리에서 둘이 닿으면 식별자가 작은 쪽이다.
    //
    // 씬 표식 목록에서 만들면 파괴를 확정할 때 그 표식의 모습과 플레이어 차단도 함께 끈다. 판정은 표식을 읽지 않는다
    /// <summary> 무철 돌진 구조물의 서버 배치와 파괴 상태, 돌진 경로의 첫 구조물을 해석적으로 찾는다 </summary>
    public sealed class BossChargeStructureField : IBossChargeStructureQuery
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float DIRECTION_EPSILON = 0.0001f;
        private const int   NO_STRUCTURE_ID   = 0;
        private const int   NOT_FOUND         = -1;

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossChargeStructureInfo[] _structures;
        private readonly bool[]                    _destroyed;
        private readonly int[]                     _gimmickTargetIds;
        private readonly BossChargeStructureSet    _scene; // 씬 표식에서 만들었을 때만 있다

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _structures.Length;

        /// <summary> 돌진 유도 기믹이 부숴야 하는 지정 구조물 식별자, 배치 순서다 </summary>
        public IReadOnlyList<int> GimmickTargetIds => _gimmickTargetIds;

        /****************************************
        *                Methods
        ****************************************/

        private BossChargeStructureField(BossChargeStructureInfo[] structures, int[] gimmickTargetIds, BossChargeStructureSet scene)
        {
            _structures       = structures;
            _destroyed        = new bool[structures.Length];
            _gimmickTargetIds = gimmickTargetIds;
            _scene            = scene;
        }

        // 하나라도 어긋나면 만들지 않는다. 일부만 쓰면 기믹 지정 수가 맵 의도와 달라진다.
        // 복원 기록에 실을 수 있는 수(BOSS_CHARGE_STRUCTURE_MAX_COUNT)를 넘으면 클라이언트가 볼 수 없어 거절한다
        /// <summary> 구조물 값으로 만든다, 식별자 0 이하, 중복, 잘못된 위치나 반경, 상한 초과면 reason과 함께 거짓이다 </summary>
        public static bool TryCreate(IReadOnlyList<BossChargeStructureInfo> structures, out BossChargeStructureField field,
            out string reason, BossChargeStructureSet scene = null)
        {
            field  = null;
            reason = string.Empty;
            if (structures == null)
            {
                reason = "구조물 목록이 없다";
                return false;
            }

            if (structures.Count > Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT)
            {
                reason = $"구조물 수가 상한을 넘는다, count={structures.Count}, max={Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT}";
                return false;
            }

            var copy    = new BossChargeStructureInfo[structures.Count];
            var seen    = new HashSet<int>();
            var targets = new List<int>(structures.Count);
            for (int i = 0; i < structures.Count; ++i)
            {
                BossChargeStructureInfo info = structures[i];
                if ((!info.IsValid) || (!seen.Add(info.Id)))
                {
                    reason = $"{i}번 구조물의 식별자나 위치, 반경이 잘못됐거나 식별자가 겹친다, id={info.Id}, radius={info.Radius}";
                    return false;
                }

                copy[i] = info;
                if (info.IsGimmickTarget)
                {
                    targets.Add(info.Id);
                }
            }

            field = new BossChargeStructureField(copy, targets.ToArray(), scene);
            return true;
        }

        /// <summary> 맵 담당이 둔 씬 표식 목록으로 만든다, 표식이 어긋나면 reason과 함께 거짓이다 </summary>
        public static bool TryCreateFromScene(BossChargeStructureSet set, out BossChargeStructureField field, out string reason)
        {
            field = null;
            if (set == null)
            {
                reason = "씬 구조물 목록이 없다";
                return false;
            }

            var structures = new List<BossChargeStructureInfo>(set.Count);
            if (!set.TryCollect(structures, out reason))
            {
                return false;
            }

            return TryCreate(structures, out field, out reason, set);
        }

        /// <summary> 맵 배치가 없을 때 쓰는 임시 개발 지점으로 만든다, 전장 중심 기준이다 </summary>
        public static BossChargeStructureField CreateDevSites(Vector3 arenaCenter)
        {
            var structures = new List<BossChargeStructureInfo>(MucheolChargeStructureDevSites.COUNT);
            new MucheolChargeStructureDevSites(arenaCenter).Collect(structures);
            if (!TryCreate(structures, out BossChargeStructureField field, out string reason))
            {
                Debug.LogError($"[Boss] 임시 돌진 구조물 지점을 만들지 못했다, {reason}");
                return null;
            }

            return field;
        }

        // 반경 합 원과 직선의 교점 2차식 |m + t d|^2 = R^2 에서 d가 단위 벡터라 t = -b - sqrt(b^2 - c)이다(b = m.d, c = |m|^2 - R^2)
        public bool TryGetFirstStructure(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius,
            out BossChargeStructureHit hit)
        {
            hit = default;

            float length = Mathf.Sqrt((direction.x * direction.x) + (direction.z * direction.z));
            if ((!(length > DIRECTION_EPSILON)) || (!(maxDistance >= 0f)) || (float.IsNaN(bodyRadius)))
            {
                return false;
            }

            float dirX = direction.x / length;
            float dirZ = direction.z / length;
            float body = Mathf.Max(0f, bodyRadius);

            float bestDistance = float.PositiveInfinity;
            int   bestId       = NO_STRUCTURE_ID;
            for (int i = 0; i < _structures.Length; ++i)
            {
                if (_destroyed[i])
                {
                    continue;
                }

                BossChargeStructureInfo structure = _structures[i];
                if (!TryGetContactDistance(origin, dirX, dirZ, structure.Center, structure.Radius + body, out float distance))
                {
                    continue;
                }

                if (distance > maxDistance)
                {
                    continue;
                }

                if ((distance < bestDistance) || ((distance == bestDistance) && (structure.Id < bestId)))
                {
                    bestDistance = distance;
                    bestId       = structure.Id;
                }
            }

            if (bestId == NO_STRUCTURE_ID)
            {
                return false;
            }

            hit = new BossChargeStructureHit(bestId, bestDistance);
            return true;
        }

        // 닿기만 한 것은 겹침이 아니다. 돌진이 멈춘 자리처럼 반경 합 경계에 선 몸체를 막지 않는다
        /// <summary> 반경 bodyRadius 몸체가 부서지지 않은 구조물과 수평으로 겹치는가 </summary>
        public bool OverlapsIntact(Vector3 point, float bodyRadius)
        {
            float body = Mathf.Max(0f, bodyRadius);
            for (int i = 0; i < _structures.Length; ++i)
            {
                if (_destroyed[i])
                {
                    continue;
                }

                BossChargeStructureInfo structure = _structures[i];
                float offsetX = point.x - structure.Center.x;
                float offsetZ = point.z - structure.Center.z;
                float reach   = structure.Radius + body;
                if (((offsetX * offsetX) + (offsetZ * offsetZ)) < (reach * reach))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 구조물을 부서진 것으로 확정한다, 배치에 없거나 이미 부서졌으면 거짓이다. 이후 조회는 이 구조물을 돌려주지 않는다 </summary>
        public bool TryDestroy(int structureId)
        {
            int index = IndexOf(structureId);
            if ((index == NOT_FOUND) || (_destroyed[index]))
            {
                return false;
            }

            _destroyed[index] = true;

            if ((_scene != null) && (_scene.TryGetMarker(structureId, out BossChargeStructureMarker marker)))
            {
                marker.SetDestroyed(true);
            }

            return true;
        }

        public bool IsDestroyed(int structureId)
        {
            int index = IndexOf(structureId);
            return (index != NOT_FOUND) && (_destroyed[index]);
        }

        public bool Contains(int structureId)
        {
            return IndexOf(structureId) != NOT_FOUND;
        }

        /// <summary> 지금 배치와 파괴 여부의 복원 값, 복제 상태에 스폰 전에 넣는다 </summary>
        public BossChargeStructureSnapshot CreateSnapshot()
        {
            var structures = new BossChargeStructureInfo[_structures.Length];
            var destroyed  = new bool[_structures.Length];
            for (int i = 0; i < _structures.Length; ++i)
            {
                structures[i] = _structures[i];
                destroyed[i]  = _destroyed[i];
            }

            return new BossChargeStructureSnapshot(structures, destroyed, _structures.Length);
        }

        // 몸체 중심이 반경 합 원에 처음 닿는 거리. 원 밖에서 멀어지거나 비껴가면 거짓이고, 이미 안이면 중심 쪽으로 향할 때만 0이다
        private static bool TryGetContactDistance(Vector3 origin, float dirX, float dirZ, Vector3 center, float reach,
            out float distance)
        {
            distance = 0f;

            float offsetX = origin.x - center.x;
            float offsetZ = origin.z - center.z;
            float along   = (offsetX * dirX) + (offsetZ * dirZ);
            float excess  = ((offsetX * offsetX) + (offsetZ * offsetZ)) - (reach * reach);

            // 멀어지거나 옆으로 지나가는 방향은 안에 있어도 막지 않는다
            if (!(along < 0f))
            {
                return false;
            }

            if (excess <= 0f)
            {
                return true;
            }

            float discriminant = (along * along) - excess;
            if (discriminant < 0f)
            {
                return false;
            }

            distance = Mathf.Max(0f, -along - Mathf.Sqrt(discriminant));
            return true;
        }

        private int IndexOf(int structureId)
        {
            for (int i = 0; i < _structures.Length; ++i)
            {
                if (_structures[i].Id == structureId)
                {
                    return i;
                }
            }

            return NOT_FOUND;
        }
    }
}
