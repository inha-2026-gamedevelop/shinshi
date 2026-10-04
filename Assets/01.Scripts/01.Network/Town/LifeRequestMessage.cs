// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 계정과 위치는 서버가 인증된 연결과 서버 캐릭터 위치로 정한다. 클라이언트는 의도만 보낸다.
    // 종류별 인자: 채집 시작 zone, node / 물약 제작 operationId, potionDefinitionId, count. 나머지는 인자가 없다
    /// <summary> 클라이언트의 마을 생활 요청, 결과는 LifeResultMessage, LifeSnapshotMessage, GatherNodeStateMessage로 온다 </summary>
    public readonly struct LifeRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid        _operationId;
        private readonly ELifeAction _action;
        private readonly int         _zone;
        private readonly int         _node;
        private readonly string      _potionDefinitionId;
        private readonly int         _count;

        /****************************************
        *              Properties
        ****************************************/

        // 물약 제작만 쓴다. 응답을 잃어 다시 보낼 때만 같은 값을 쓴다
        public Guid        OperationId        => _operationId;
        public ELifeAction Action             => _action;
        public int         Zone               => _zone;
        public int         Node               => _node;
        public string      PotionDefinitionId => _potionDefinitionId;
        public int         Count              => _count;

        /****************************************
        *                Methods
        ****************************************/

        public LifeRequestMessage(Guid operationId, ELifeAction action, int zone, int node, string potionDefinitionId, int count)
        {
            _operationId        = operationId;
            _action             = action;
            _zone               = zone;
            _node               = node;
            _potionDefinitionId = potionDefinitionId;
            _count              = count;
        }
    }
}
