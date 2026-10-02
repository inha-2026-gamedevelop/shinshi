// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 보스 몸체의 이동과 투사체가 같은 정적 지형 검사를 쓴다. 구현은 서버 몸체가 하고 플레이어와 소환물은 검사 대상이 아니다
    /// <summary> 정적 지형(벽)에 막히기 전까지의 거리를 묻는 창구 </summary>
    public interface IBossWallQuery
    {
        /// <summary> origin에서 수평 단위 방향으로 반경만큼의 폭을 두고 벽에 닿기 전까지 갈 수 있는 거리, 막히지 않으면 maxDistance </summary>
        float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius);
    }
}
