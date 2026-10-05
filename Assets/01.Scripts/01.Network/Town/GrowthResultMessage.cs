// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 확정된 작업은 Reason이 None이고 Outcome이 확률 판정 결과다(GrowthCodes.Outcome). 확정 뒤 바뀐 성장 상태는
    // GrowthSnapshotMessage로 따로 온다. BackendUnavailable이면 같은 OperationId로 다시 보내도 효과는 한 번이다
    /// <summary> 성장 요청 한 건의 결과, 요청한 클라이언트에게만 온다 </summary>
    public readonly struct GrowthResultMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid          _operationId;
        private readonly EGrowthAction _action;
        private readonly ERejectReason _reason;
        private readonly int           _outcome;
        private readonly string        _itemId;

        /****************************************
        *              Properties
        ****************************************/

        public Guid          OperationId => _operationId;
        public EGrowthAction Action      => _action;
        public ERejectReason Reason      => _reason;
        public int           Outcome     => _outcome;

        // 제작, 계승, 강화, 착용으로 영향을 받은 장비, 없으면 null
        public string        ItemId      => _itemId;
        public bool          IsAccepted  => _reason == ERejectReason.None;

        /****************************************
        *                Methods
        ****************************************/

        public GrowthResultMessage(Guid operationId, EGrowthAction action, ERejectReason reason, int outcome, string itemId)
        {
            _operationId = operationId;
            _action      = action;
            _reason      = reason;
            _outcome     = outcome;
            _itemId      = itemId;
        }
    }
}
