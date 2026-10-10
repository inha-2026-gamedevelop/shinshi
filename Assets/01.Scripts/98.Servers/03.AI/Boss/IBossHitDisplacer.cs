// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // ICombatTarget은 읽기 전용이라 패턴이 대상 위치를 바꿀 수 없다. 플레이어와 동료와 소환물의 위치는 각자의 서버 이동이 갖고 있어
    // 패턴은 이 창구로만 부탁한다. 구현이 벽, 전장 경계, 충돌체를 책임지므로 패턴은 막힘을 판단하지 않는다.
    // 운영 서버의 구현은 아직 없다. 구현이 없으면 이 창구가 필요한 패턴은 팩토리가 만들지 않는다
    /// <summary> 보스 패턴에 맞은 대상을 서버 이동으로 밀어내는 창구, 구현은 대상의 서버 이동이 한다 </summary>
    public interface IBossHitDisplacer
    {
        /// <summary> 대상을 direction(수평 단위 벡터)으로 최대 maxDistance만큼 밀고 실제로 움직인 거리를 돌려준다, 막히면 maxDistance보다 짧고 대상을 못 찾으면 0이다 </summary>
        float TryPush(int combatId, Vector3 direction, float maxDistance);
    }
}
