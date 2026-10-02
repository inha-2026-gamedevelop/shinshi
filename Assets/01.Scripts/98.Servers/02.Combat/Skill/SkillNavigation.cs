// Unity
using UnityEngine;
using UnityEngine.AI;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    public sealed class SkillNavigation : ISkillNavigation
    {
        /****************************************
        *                Methods
        ****************************************/

        public bool TrySample(Vector3 point, out Vector3 sampled)
        {
            sampled = point;
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, Constants.Player.MOVE_DESTINATION_SAMPLE_RADIUS, NavMesh.AllAreas))
            {
                return false;
            }
            sampled = hit.position;
            return true;
        }

        public Vector3 Move(Vector3 origin, Vector3 destination)
        {
            if (NavMesh.Raycast(origin, destination, out NavMeshHit hit, NavMesh.AllAreas))
            {
                return hit.position;
            }
            if (!TrySample(destination, out Vector3 sampled))
            {
                return origin;
            }
            return NavMesh.Raycast(origin, sampled, out hit, NavMesh.AllAreas) ? hit.position : sampled;
        }
    }
}
