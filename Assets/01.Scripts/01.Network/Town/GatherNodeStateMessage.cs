// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 노드 상태가 바뀔 때 마을 접속자 전원에게 보낸다. 노드 하나는 몇 초에 한 번 바뀌고 한 구역 노드가 12개라
    // 관심 범위 없이 보내도 대역이 작다. 처음 들어온 클라이언트는 GatherNodes 요청으로 사용 중인 노드만 받는다
    /// <summary> 채집 노드 하나의 공용 상태 </summary>
    public readonly struct GatherNodeStateMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int              _zone;
        private readonly int              _node;
        private readonly EGatherNodeState _state;
        private readonly int              _claimParticipantId;
        private readonly float            _regrowRemaining;

        /****************************************
        *              Properties
        ****************************************/

        public int              Zone               => _zone;
        public int              Node               => _node;
        public EGatherNodeState State              => _state;

        // 선점, 완료 중인 참가자 번호, 아니면 0. 자기 번호와 같으면 내 채집이다
        public int              ClaimParticipantId => _claimParticipantId;

        // 재생까지 남은 초, Depleted가 아니면 0
        public float            RegrowRemaining    => _regrowRemaining;

        /****************************************
        *                Methods
        ****************************************/

        public GatherNodeStateMessage(int zone, int node, EGatherNodeState state, int claimParticipantId, float regrowRemaining)
        {
            _zone               = zone;
            _node               = node;
            _state              = state;
            _claimParticipantId = claimParticipantId;
            _regrowRemaining    = regrowRemaining;
        }
    }
}
