// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Systems
{
    // 웨이브 번호 N은 레이아웃의 N번 구역(문 앞, 안쪽/바깥 노드, 반경)에서만 나온다. WaveConfigSO가 몇 마리를 언제 내는지의 정본이고
    // 레이아웃은 좌표와 거리만 준다(무리 크기, 엘리트 수는 읽지 않는다). 레이아웃은 씬 컴포넌트라 서버 부팅 시점에 숲 씬이 없을 수 있어
    // 생성자에 레이아웃을 넘기지 않고 호출 때마다 공급자에서 얻는다. 웨이브마다 후보 지점은 처음 요청 때 한 번 만들어 보관한다
    /// <summary> DungeonSpawnLayout의 구역 좌표를 웨이브 지점으로 바꾸는 어댑터, 웨이브 N = 구역 N </summary>
    public sealed class DungeonLayoutWaveSpawnPoints : IWaveSpawnPointSource
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   CANDIDATES_PER_WAVE = 48;    // 웨이브가 돌려 쓸 후보 지점 상한
        private const int   POINTS_PER_RING     = 24;
        private const int   RAYCAST_MAX_HITS    = 16;
        private const float GROUND_PROBE_HEIGHT = 30f;   // m, 후보를 만들 때 문 앞 위에서 아래로 땅을 찾는 높이
        private const float GROUND_PROBE_DEPTH  = 60f;   // m
        private const float SNAP_PROBE_HEIGHT   = 2f;    // m, 매 틱 보정은 지붕과 나뭇가지에 가리지 않게 낮게 쏜다
        private const float SNAP_PROBE_DEPTH    = 6f;    // m
        private const float DEFAULT_TOLERANCE   = 1f;    // m, 레이아웃이 없을 때의 지면 높이 허용 차
        private const float OBSTACLE_PADDING    = 0.25f; // m, 장애물 검사 구의 바닥 여유
        private const float GOLDEN_ANGLE_RAD    = 2.3999632f;
        private const float RADIUS_EPSILON      = 0.001f;
        private const float RING_PHASE          = 0.5f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly Func<DungeonSpawnLayout>       _layoutProvider;
        private readonly int                            _groundLayerMask;
        private readonly int                            _obstacleLayerMask;
        private readonly float                          _bodyRadius;
        private readonly RaycastHit[]                   _rayHits    = new RaycastHit[RAYCAST_MAX_HITS];
        private readonly Dictionary<int, List<Vector3>> _candidates = new Dictionary<int, List<Vector3>>();

        private DungeonSpawnLayout _cachedLayout;

        /****************************************
        *                Methods
        ****************************************/

        // layoutProvider가 null이면 DungeonSpawnLayout.Current를 쓴다. groundLayerMask는 걸어 다닐 지면 레이어이고 적 몸체 충돌체를 넣지 않는다.
        // obstacleLayerMask가 0이면 나무, 바위 겹침 검사를 하지 않는다. bodyRadius는 그 검사에 쓰는 적 몸체 반경이다
        public DungeonLayoutWaveSpawnPoints(Func<DungeonSpawnLayout> layoutProvider, int groundLayerMask, int obstacleLayerMask,
            float bodyRadius)
        {
            _layoutProvider    = layoutProvider ?? (() => DungeonSpawnLayout.Current);
            _groundLayerMask   = groundLayerMask;
            _obstacleLayerMask = obstacleLayerMask;
            _bodyRadius        = Mathf.Max(0f, bodyRadius);
        }

        public int GetPointCount(int waveIndex)
        {
            List<Vector3> points = GetCandidates(waveIndex);
            return (points == null) ? 0 : points.Count;
        }

        public bool TryGetPoint(int waveIndex, int index, out Vector3 position)
        {
            List<Vector3> points = GetCandidates(waveIndex);
            if ((points == null) || (index < 0) || (index >= points.Count))
            {
                position = default;
                return false;
            }

            position = points[index];
            return true;
        }

        public bool TrySnapToGround(int waveIndex, Vector3 position, out Vector3 grounded)
        {
            DungeonSpawnLayout layout    = _layoutProvider();
            float              tolerance = (layout != null) ? layout.GroundHeightTolerance : DEFAULT_TOLERANCE;
            return TryFindGround(position + (Vector3.up * SNAP_PROBE_HEIGHT), position.y, tolerance, SNAP_PROBE_DEPTH, out grounded);
        }

        public bool TryGetNextWavePoint(int waveIndex, out Vector3 position)
        {
            DungeonSpawnLayout layout = _layoutProvider();
            if (layout == null)
            {
                position = default;
                return false;
            }

            return layout.TryGetNextWave(waveIndex, out position);
        }

        // 레이아웃이 바뀌었으면(씬 재로드) 이전 후보를 버린다
        private List<Vector3> GetCandidates(int waveIndex)
        {
            DungeonSpawnLayout layout = _layoutProvider();
            if (layout == null)
            {
                return null;
            }

            if (!ReferenceEquals(layout, _cachedLayout))
            {
                _candidates.Clear();
                _cachedLayout = layout;
            }

            if (_candidates.TryGetValue(waveIndex, out List<Vector3> cached))
            {
                return cached;
            }

            List<Vector3> built = BuildCandidates(layout, waveIndex);
            _candidates.Add(waveIndex, built);
            return built;
        }

        // 고정 노드(안쪽, 바깥)를 먼저 두고 문 앞 반경 안에 황금각 나선으로 지면 후보를 채운다. 무작위를 쓰지 않아 같은 레이아웃은 같은 후보를 낸다
        private List<Vector3> BuildCandidates(DungeonSpawnLayout layout, int waveIndex)
        {
            var points = new List<Vector3>(CANDIDATES_PER_WAVE);
            if (!layout.TryGetDoor(waveIndex, out Vector3 door))
            {
                return points;
            }

            AddNodes(layout, waveIndex, false, points);
            AddNodes(layout, waveIndex, true, points);

            float step    = layout.RadiusStep;
            float maxRing = Mathf.Max(layout.SpawnRadius, layout.MaxSpawnRadius);
            int   ring    = 0;
            for (float radius = layout.SpawnRadius; radius <= (maxRing + RADIUS_EPSILON); radius += step)
            {
                for (int k = 0; (k < POINTS_PER_RING) && (points.Count < CANDIDATES_PER_WAVE); ++k)
                {
                    float angle  = (k + (ring * RING_PHASE)) * GOLDEN_ANGLE_RAD;
                    float spread = radius * Mathf.Sqrt((k + RING_PHASE) / POINTS_PER_RING);
                    var   probe  = new Vector3(door.x + (Mathf.Cos(angle) * spread), door.y, door.z + (Mathf.Sin(angle) * spread));

                    if ((TryFindGround(probe + (Vector3.up * GROUND_PROBE_HEIGHT), door.y, layout.GroundHeightTolerance,
                            GROUND_PROBE_DEPTH, out Vector3 ground)) && (IsFree(ground)))
                    {
                        points.Add(ground);
                    }
                }

                ++ring;
                if ((step <= 0f) || (points.Count >= CANDIDATES_PER_WAVE))
                {
                    break;
                }
            }

            return points;
        }

        private static void AddNodes(DungeonSpawnLayout layout, int waveIndex, bool outer, List<Vector3> points)
        {
            int count = layout.CountNodes(waveIndex, outer);
            for (int i = 0; i < count; ++i)
            {
                if (layout.TryGetNode(waveIndex, outer, i, out Vector3 node))
                {
                    points.Add(node);
                }
            }
        }

        // 위에서 쏜 광선이 지붕, 나무, 위층 바닥에 먼저 맞아도 기준 높이에 가장 가까운 면을 고르고 허용 높이 차를 넘으면 버린다
        private bool TryFindGround(Vector3 from, float referenceY, float tolerance, float depth, out Vector3 ground)
        {
            ground = default;
            int count = Physics.RaycastNonAlloc(new Ray(from, Vector3.down), _rayHits, depth, _groundLayerMask,
                QueryTriggerInteraction.Ignore);

            float closest = float.MaxValue;
            bool  found   = false;
            for (int i = 0; i < count; ++i)
            {
                float gap = Mathf.Abs(_rayHits[i].point.y - referenceY);
                if ((gap >= closest) || (gap > tolerance))
                {
                    continue;
                }

                closest = gap;
                ground  = _rayHits[i].point;
                found   = true;
            }

            return found;
        }

        private bool IsFree(Vector3 ground)
        {
            if (_obstacleLayerMask == 0)
            {
                return true;
            }

            Vector3 center = ground + (Vector3.up * (_bodyRadius + OBSTACLE_PADDING));
            return !Physics.CheckSphere(center, _bodyRadius, _obstacleLayerMask, QueryTriggerInteraction.Ignore);
        }
    }
}
