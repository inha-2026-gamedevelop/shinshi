// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    // 보스 패턴의 밀어내기(IBossHitDisplacer)는 벽과 전장 경계까지의 거리만 계산하고 위치는 직접 쓰지 않는다.
    // 위치는 대상의 서버 이동이 갖고 있어 경로, 회피, 기술 이동과 겹치지 않게 대상이 스스로 옮겨야 하기 때문이다.
    // 그래서 계산한 쪽은 CanBeDisplaced를 먼저 묻고, 참일 때만 막힘을 반영한 최종 위치를 Displace로 넘긴다
    /// <summary> 보스 패턴에 맞아 서버가 밀어낼 수 있는 대상, 위치 변경은 대상의 서버 이동이 한다 </summary>
    public interface IBossDisplaceTarget
    {
        int     CombatId   { get; }
        Vector3 Position   { get; }
        float   BodyRadius { get; }

        /// <summary> 지금 밀어도 되는가. 죽었거나 대상의 다른 이동이 위치를 정하는 중이면 거짓이다 </summary>
        bool CanBeDisplaced { get; }

        /// <summary> position으로 옮긴다, 벽과 전장 경계는 부르는 쪽이 이미 반영했다. CanBeDisplaced가 거짓이거나 좌표가 유한하지 않으면 아무것도 하지 않는다 </summary>
        void Displace(Vector3 position, double serverTime);
    }
}
