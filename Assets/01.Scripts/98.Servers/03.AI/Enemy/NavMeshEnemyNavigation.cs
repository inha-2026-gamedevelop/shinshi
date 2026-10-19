// Unity
using UnityEngine;
using UnityEngine.AI;

namespace Shinshi.AI
{
    // 서버 씬에 구워진 NavMesh를 쓴다. 경로 계산 한 번이 비싸므로 호출자가 주기를 제한한다(선택기 0.5초, 조향 0.25초).
    // 시작과 끝은 반경 안의 가장 가까운 NavMesh 점으로 옮겨 계산하고, 옮길 점이 없으면 닿을 수 없는 것으로 본다
    /// <summary> NavMesh 경로로 답하는 일반 적 길찾기 </summary>
    public sealed class NavMeshEnemyNavigation : IEnemyNavigation
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float DEFAULT_SAMPLE_RADIUS = 2f; // m, 몸체가 NavMesh 가장자리에서 조금 벗어나도 같은 지면으로 본다

        private const int   CORNER_CAPACITY = 64;
        private const float WALKABLE_RADIUS = 0.5f;    // m
        private const float REACHED_EPSILON = 0.3f;    // m, 이 안의 꺾임 지점은 이미 지난 것으로 본다

        /****************************************
        *                Fields
        ****************************************/

        private readonly int         _areaMask;
        private readonly float       _sampleRadius;
        private readonly NavMeshPath _path    = new NavMeshPath();
        private readonly Vector3[]   _corners = new Vector3[CORNER_CAPACITY];

        /****************************************
        *                Methods
        ****************************************/

        public NavMeshEnemyNavigation(int areaMask = NavMesh.AllAreas, float sampleRadius = DEFAULT_SAMPLE_RADIUS)
        {
            _areaMask     = areaMask;
            _sampleRadius = Mathf.Max(0.1f, sampleRadius);
        }

        public bool TryMeasurePath(Vector3 from, Vector3 to, out float length)
        {
            length = 0f;
            if (!Calculate(from, to, out int count))
            {
                return false;
            }

            for (int i = 1; i < count; ++i)
            {
                length += Vector3.Distance(_corners[i - 1], _corners[i]);
            }

            // 모서리 버퍼를 넘는 긴 경로는 마지막 점에서 끝까지 직선으로 이어 붙여 길이가 모자라지 않게 한다
            if (count == CORNER_CAPACITY)
            {
                length += Vector3.Distance(_corners[count - 1], to);
            }

            return true;
        }

        public bool TryGetSteerPoint(Vector3 from, Vector3 to, out Vector3 point)
        {
            point = to;
            if (!Calculate(from, to, out int count))
            {
                return false;
            }

            for (int i = 1; i < count; ++i)
            {
                float x = _corners[i].x - from.x;
                float z = _corners[i].z - from.z;
                if (((x * x) + (z * z)) > (REACHED_EPSILON * REACHED_EPSILON))
                {
                    point = _corners[i];
                    return true;
                }
            }

            return true;
        }

        public bool IsWalkable(Vector3 point)
        {
            return NavMesh.SamplePosition(point, out _, WALKABLE_RADIUS, _areaMask);
        }

        private bool Calculate(Vector3 from, Vector3 to, out int count)
        {
            count = 0;
            if ((!NavMesh.SamplePosition(from, out NavMeshHit start, _sampleRadius, _areaMask))
                || (!NavMesh.SamplePosition(to, out NavMeshHit end, _sampleRadius, _areaMask)))
            {
                return false;
            }

            if ((!NavMesh.CalculatePath(start.position, end.position, _areaMask, _path))
                || (_path.status != NavMeshPathStatus.PathComplete))
            {
                return false;
            }

            count = _path.GetCornersNonAlloc(_corners);
            return count > 0;
        }
    }
}
