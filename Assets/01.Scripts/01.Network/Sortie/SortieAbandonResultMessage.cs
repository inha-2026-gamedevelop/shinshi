// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 출정 포기 요청의 결과, None이면 개인 이탈이 확정됐고 귀환 가능 여부는 출정 상태 메시지로 온다 </summary>
    public readonly struct SortieAbandonResultMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid          _operationId;
        private readonly ERejectReason _reason;

        /****************************************
        *              Properties
        ****************************************/

        public Guid          OperationId => _operationId;

        // WrongPhase(시작 전), SessionTerminal(이미 끝남), AlreadySettled(이미 이탈), PendingCommit(다른 요청 처리 중)
        public ERejectReason Reason      => _reason;

        /****************************************
        *                Methods
        ****************************************/

        public SortieAbandonResultMessage(Guid operationId, ERejectReason reason)
        {
            _operationId = operationId;
            _reason      = reason;
        }
    }
}
