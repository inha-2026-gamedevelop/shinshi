// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 시전 시작에 고정하는 잔류 부채꼴 연쇄의 배치다. 중심은 보스의 시전 시작 서버 위치이고 이후 보스가 움직여도 바뀌지 않는다
    // 피난 방향은 안전 방향 가운데를 기준으로 좌우 절반씩 비워 두고, 나머지 각도를 같은 크기의 구역으로 나눈다
    // 구역 0은 피난 방향의 시계 쪽 끝(위에서 볼 때 오른쪽)에 붙어 시계 방향으로 이어지고 마지막 구역이 반대쪽 끝에 붙는다
    // 사거리는 최소 사거리 이상이면서 중심에서 먼 쪽 전장 경계까지 닿는 값이다. 보스가 중심에서 벗어날수록 늘어나고, 전장 원 밖은 판정에서 자른다
    // 부채꼴 판정은 BossFanShape 하나를 재사용한다. 이웃한 구역과 피난 구역은 경계를 공유하고 경계 위의 대상은 위험 쪽이다
    /// <summary> 잔류 부채꼴 연쇄의 시전 시작 고정 배치, 구역 기하와 전장 안팎 판정을 만든다 </summary>
    public readonly struct BossChainFanLayout
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float FULL_TURN      = 360f;
        private const float HALF           = 0.5f;
        private const float ARENA_EPSILON  = 0.001f; // m, 전장 경계 위의 대상을 안으로 보는 오차

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _origin;
        private readonly Vector3 _safeDirection;
        private readonly float   _range;
        private readonly int     _zoneCount;
        private readonly float   _zoneArc;
        private readonly Vector3 _arenaCenter;
        private readonly float   _arenaRadius;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시전 시작 순간 보스의 서버 위치, 모든 구역의 중심이다 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 피난 방향 가운데를 향하는 수평 단위 벡터 </summary>
        public Vector3 SafeDirection => _safeDirection;

        /// <summary> 실제 판정 사거리(m), 최소 사거리와 먼 쪽 전장 경계까지 거리 중 큰 값이다 </summary>
        public float Range => _range;

        public int ZoneCount => _zoneCount;

        /// <summary> 구역 하나의 각도(도) </summary>
        public float ZoneArc => _zoneArc;

        /// <summary> 피난 방향의 전체 각도(도), 360도에서 모든 구역을 뺀 값이다 </summary>
        public float SafeArc => FULL_TURN - (_zoneCount * _zoneArc);

        public Vector3 ArenaCenter => _arenaCenter;

        public float ArenaRadius => _arenaRadius;

        /****************************************
        *                Methods
        ****************************************/

        public BossChainFanLayout(Vector3 origin, Vector3 safeDirection, float minRange, int zoneCount, float zoneArc,
            Vector3 arenaCenter, float arenaRadius)
        {
            Vector3 toCenter = arenaCenter - origin;
            toCenter.y = 0f;

            _origin        = origin;
            _safeDirection = safeDirection;
            _zoneCount     = zoneCount;
            _zoneArc       = zoneArc;
            _arenaCenter   = arenaCenter;
            _arenaRadius   = arenaRadius;
            _range         = Mathf.Max(minRange, toCenter.magnitude + arenaRadius);
        }

        /// <summary> 구역 k의 가운데 방향(수평 단위 벡터) </summary>
        public Vector3 GetZoneDirection(int zoneIndex)
        {
            float offset = (SafeArc * HALF) + (_zoneArc * (zoneIndex + HALF));
            return (Quaternion.AngleAxis(offset, Vector3.up) * _safeDirection).normalized;
        }

        /// <summary> 위치가 전장 원 안인가, 경계 위는 안이다 </summary>
        public bool IsInsideArena(Vector3 position)
        {
            Vector3 offset = position - _arenaCenter;
            offset.y = 0f;
            return offset.magnitude <= (_arenaRadius + ARENA_EPSILON);
        }

        /// <summary> 위치가 구역 k 안인가, 전장 밖이면 거짓이다 </summary>
        public bool Contains(int zoneIndex, Vector3 position)
        {
            if (!IsInsideArena(position))
            {
                return false;
            }

            return BossFanShape.IsInsideSector(_origin, GetZoneDirection(zoneIndex), position, _range, _zoneArc);
        }

        /// <summary> 위치가 구역 0부터 activeCount - 1까지 중 하나 안인가, 이미 폭발한 구역은 계속 위험하다 </summary>
        public bool ContainsAny(int activeCount, Vector3 position)
        {
            if (!IsInsideArena(position))
            {
                return false;
            }

            int count = Mathf.Min(activeCount, _zoneCount);
            for (int i = 0; i < count; ++i)
            {
                if (BossFanShape.IsInsideSector(_origin, GetZoneDirection(i), position, _range, _zoneArc))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
