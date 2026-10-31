// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    // 서버 보스 몸체(BossServer)와 클라이언트 복제 상태(BossState)가 함께 구현한다. 서버 이동과 소유 클라이언트 예측이
    // 같은 규칙(BossBodyPushRules)으로 플레이어를 밀어내야 예측이 서버 위치로 끌려가지 않는다
    /// <summary> 플레이어를 몸통 밖으로 밀어내는 보스 몸체 </summary>
    public interface IBossBodyBlocker
    {
        /// <summary> 몸통 중심, 수평 위치만 쓴다 </summary>
        Vector3 Position { get; }

        /// <summary> 플레이어를 밀어내는 몸통 반경(m), 판정용 몸체 반경과 다르다 </summary>
        float BodyPushRadius { get; }

        /// <summary> 지금 플레이어를 막는가, 죽었거나 반경을 모르면 거짓이다 </summary>
        bool BlocksBodies { get; }
    }
}
