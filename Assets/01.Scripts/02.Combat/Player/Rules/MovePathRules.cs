// Unity
using UnityEngine;
using UnityEngine.AI;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 클릭 이동 경로 만들기, 서버 권위 이동과 소유 클라이언트 예측이 같은 경로를 얻도록 한곳에 둔다 </summary>
    public static class MovePathRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 목적지를 내비 메시 위로 찾아 경로를 계산하고 follower를 시작한다, 실패하면 follower를 건드리지 않는다 </summary>
        public static bool TryBeginPath(Vector3 from, Vector3 destination, NavMeshPath navPath, PathFollower follower)
        {
            if (!NavMesh.SamplePosition(destination, out NavMeshHit destinationHit, Constants.Player.MOVE_DESTINATION_SAMPLE_RADIUS,
                NavMesh.AllAreas))
            {
                return false;
            }

            // 예측 보정으로 옮겨진 클라이언트 위치는 메시에서 조금 떠 있을 수 있다. 출발점도 메시 위로 찾아야 경로 계산이 실패하지 않는다
            if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, Constants.Player.MOVE_DESTINATION_SAMPLE_RADIUS, NavMesh.AllAreas))
            {
                return false;
            }

            if (!NavMesh.CalculatePath(fromHit.position, destinationHit.position, NavMesh.AllAreas, navPath))
            {
                return false;
            }

            // 막힌 곳을 누르면 부분 경로로 갈 수 있는 데까지 간다
            if (navPath.status == NavMeshPathStatus.PathInvalid)
            {
                return false;
            }

            int count = navPath.GetCornersNonAlloc(follower.CornerBuffer);
            if (count == 0)
            {
                return false;
            }

            follower.Begin(count);
            return true;
        }
    }
}
