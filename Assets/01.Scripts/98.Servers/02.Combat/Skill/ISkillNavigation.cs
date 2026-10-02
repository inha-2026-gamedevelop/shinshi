// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 기술의 이동 가능한 지점과 벽 앞 이동 판정, 테스트에서는 고정 지형을 주입한다 </summary>
    public interface ISkillNavigation
    {
        /****************************************
        *                Methods
        ****************************************/

        bool TrySample(Vector3 point, out Vector3 sampled);
        Vector3 Move(Vector3 origin, Vector3 destination);
    }
}
