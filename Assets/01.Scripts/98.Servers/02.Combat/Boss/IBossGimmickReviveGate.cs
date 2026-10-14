// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 보스 기믹이 공용 부활을 막거나 부활 지점을 정하는 창구 </summary>
    public interface IBossGimmickReviveGate
    {
        /// <summary> 거짓이면 지금은 부활시키지 않는다, 부활 횟수를 쓰지 않고 미룬다 </summary>
        bool IsReviveWindowOpen { get; }

        /// <summary> 부활 지점을 묻는다, Available일 때만 point가 뜻이 있고 Unavailable이면 기존 지점으로 되돌아가지 않고 미룬다 </summary>
        EBossRevivePoint TryGetRevivePoint(int combatId, out Vector3 point);
    }
}
