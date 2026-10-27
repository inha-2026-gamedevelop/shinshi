// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 기존 OnHit 알림은 맞은 수만 넘겨서 임팩트를 어디에 터뜨릴지 알 수 없었다. 인터페이스를 바꾸면 기존 구현체가 모두 깨지므로 따로 둔다
    // 패턴은 자기 리스너가 이 인터페이스도 구현하면 피해 요청이 실제로 나간 대상마다 부른다. 구현하지 않으면 아무 일도 일어나지 않는다
    /// <summary> 보스 패턴에 실제로 맞은 대상 한 명의 위치와 방향 알림, 복제 계층이 클라이언트 임팩트로 전달하는 구성을 가정한다 </summary>
    public interface IBossTargetHitListener
    {
        /// <summary> 피해 요청이 나간 대상 한 명이다. position은 대상 위치, direction은 공격 기준점에서 대상으로 향하는 수평 단위 벡터이며 겹치면 0이다 </summary>
        void OnTargetHit(int castInstanceId, int targetId, Vector3 position, Vector3 direction);
    }
}
