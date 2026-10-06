// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // GatherStart가 None이면 선점했다는 뜻이고 약초는 GatherCompleted가 None으로 올 때 확정이다
    /// <summary> 생활 요청 한 건의 결과, 요청한 클라이언트에게만 온다 </summary>
    public readonly struct LifeResultMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid          _operationId;
        private readonly ELifeAction   _action;
        private readonly ERejectReason _reason;
        private readonly int           _zone;
        private readonly int           _node;

        /****************************************
        *              Properties
        ****************************************/

        public Guid          OperationId => _operationId;
        public ELifeAction   Action      => _action;
        public ERejectReason Reason      => _reason;
        public int           Zone        => _zone;
        public int           Node        => _node;
        public bool          IsAccepted  => _reason == ERejectReason.None;

        /****************************************
        *                Methods
        ****************************************/

        public LifeResultMessage(Guid operationId, ELifeAction action, ERejectReason reason, int zone, int node)
        {
            _operationId = operationId;
            _action      = action;
            _reason      = reason;
            _zone        = zone;
            _node        = node;
        }
    }
}
