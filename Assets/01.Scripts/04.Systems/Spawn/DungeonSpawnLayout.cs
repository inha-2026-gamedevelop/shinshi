// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Systems
{
    // 숲 던전 씬 하나에 하나만 둔다. 서버가 씬을 탐색하지 않고 읽도록 켜질 때 자신을 정적 자리에 올린다(BossChargeStructureSet과 같은 방식).
    // 판정 입력이라 서버 빌드에서도 켜져 있어야 하며 표현 컴포넌트가 아니다.
    // 값은 Dev_ForestPlateauHitFeedback의 DevHitFeedbackSpawner 설정과 구역 표식에서 Release 씬 빌더가 옮겨 적는다
    // TODO(기웅): 구역과 웨이브의 대응(6구역 = 6웨이브), 엘리트 구역, 구역 사이 결계 해제 방식은 기획 확정 전이다. 아래 값은 Dev 시험 값이다
    /// <summary> 숲 웨이브가 적을 세울 구역별 문 앞 지점, 안쪽/바깥 노드, 다음 웨이브 지점과 Dev 스포너의 배치 설정 </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonSpawnLayout : MonoBehaviour
    {
        /// <summary> 구역 하나의 표식 </summary>
        [Serializable]
        public struct Zone
        {
            public Transform   door;       // 문 앞 출현 중심, 반경 안에서 무작위로 고른다
            public Transform   nextWave;   // 이 구역을 마치고 향할 다음 지점
            public Transform[] innerNodes; // 문 앞 안쪽 고정 노드
            public Transform[] outerNodes; // 문 앞 바깥 고정 노드
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("구역")]
        [SerializeField] private Zone[] _zones = null;

        [Header("무리 (Dev 시험 값)")]
        [SerializeField, Min(1)] private int   _groupSize              = 24;
        [SerializeField, Min(0f)] private float _spawnRadius           = 6f;   // m, 문 앞 출현 중심 주위
        [SerializeField, Min(0f)] private float _maxSpawnRadius       = 12f;  // m, 자리가 모자랄 때 넓히는 상한
        [SerializeField, Min(0f)] private float _radiusStep           = 2f;   // m
        [SerializeField, Min(0f)] private float _minPlayerDistance     = 4f;   // m, 플레이어와 이 안에는 세우지 않는다
        [SerializeField, Min(0f)] private float _groundHeightTolerance = 1f;   // m, 문 앞 지점과 이 높이 차 안의 땅에만 세운다
        [SerializeField] private bool _spawnFirstZoneAtStart = true;
        [SerializeField] private bool _spawnOnPreviousClear  = true;

        [Header("엘리트 (Dev 시험 값)")]
        [SerializeField, Min(0)] private int   _eliteZoneIndex = 5; // 0부터 센 구역 번호
        [SerializeField, Min(0)] private int   _eliteCount     = 2;
        [SerializeField, Min(0.1f)] private float _eliteSizeScale = 2f;

        private static DungeonSpawnLayout _current;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 켜져 있는 배치, 없으면 null </summary>
        public static DungeonSpawnLayout Current => _current;

        public int   ZoneCount             => (_zones == null) ? 0 : _zones.Length;
        public int   GroupSize             => _groupSize;
        public float SpawnRadius           => _spawnRadius;
        public float MaxSpawnRadius        => _maxSpawnRadius;
        public float RadiusStep            => _radiusStep;
        public float MinPlayerDistance     => _minPlayerDistance;
        public float GroundHeightTolerance => _groundHeightTolerance;
        public bool  SpawnFirstZoneAtStart => _spawnFirstZoneAtStart;
        public bool  SpawnOnPreviousClear  => _spawnOnPreviousClear;
        public int   EliteZoneIndex        => _eliteZoneIndex;
        public int   EliteCount            => _eliteCount;
        public float EliteSizeScale        => _eliteSizeScale;

        /****************************************
        *              Unity Event
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 배치가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _current = null;
        }

        private void OnEnable()
        {
            if ((_current != null) && (_current != this))
            {
                Debug.LogError("[Spawn] 던전 출현 배치가 씬에 둘 이상이다. 먼저 켜진 배치만 쓴다", this);
                return;
            }

            _current = this;
        }

        private void OnDisable()
        {
            if (_current == this)
            {
                _current = null;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 구역의 문 앞 출현 중심, 없으면 거짓 </summary>
        public bool TryGetDoor(int zone, out Vector3 position)
        {
            if (IsZone(zone) && (_zones[zone].door != null))
            {
                position = _zones[zone].door.position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary> 구역을 마치고 향할 다음 지점, 없으면 거짓 </summary>
        public bool TryGetNextWave(int zone, out Vector3 position)
        {
            if (IsZone(zone) && (_zones[zone].nextWave != null))
            {
                position = _zones[zone].nextWave.position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        public int CountNodes(int zone, bool outer)
        {
            Transform[] nodes = GetNodes(zone, outer);
            return (nodes == null) ? 0 : nodes.Length;
        }

        /// <summary> 구역의 고정 노드 위치, 범위를 벗어나거나 비었으면 거짓 </summary>
        public bool TryGetNode(int zone, bool outer, int index, out Vector3 position)
        {
            Transform[] nodes = GetNodes(zone, outer);
            if ((nodes == null) || (index < 0) || (index >= nodes.Length) || (nodes[index] == null))
            {
                position = Vector3.zero;
                return false;
            }

            position = nodes[index].position;
            return true;
        }

        /// <summary> 모든 구역에 문 앞 지점이 있고 노드에 빈 칸이 없어야 참이다, 어긋나면 reason을 채운다 </summary>
        public bool TryValidate(out string reason)
        {
            if (ZoneCount == 0)
            {
                reason = "구역이 없다";
                return false;
            }

            for (int i = 0; i < _zones.Length; ++i)
            {
                if (_zones[i].door == null)
                {
                    reason = $"{i}번 구역의 문 앞 지점이 비어 있다";
                    return false;
                }

                if (HasEmpty(_zones[i].innerNodes) || HasEmpty(_zones[i].outerNodes))
                {
                    reason = $"{i}번 구역의 노드에 빈 칸이 있다";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private bool IsZone(int zone)
        {
            return (_zones != null) && (zone >= 0) && (zone < _zones.Length);
        }

        private Transform[] GetNodes(int zone, bool outer)
        {
            if (!IsZone(zone))
            {
                return null;
            }

            return outer ? _zones[zone].outerNodes : _zones[zone].innerNodes;
        }

        private static bool HasEmpty(Transform[] nodes)
        {
            if (nodes == null)
            {
                return false;
            }

            for (int i = 0; i < nodes.Length; ++i)
            {
                if (nodes[i] == null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
