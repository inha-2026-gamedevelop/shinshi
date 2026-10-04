// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // None이면 API가 기록을 확정했다(이미 완료한 계정도 None). BackendUnavailable이면 결과를 모르므로 같은 operationId로 다시 보낸다
    /// <summary> 신시 안내 완료 기록 결과, 요청한 클라이언트에게만 온다 </summary>
    public readonly struct IntroCompleteResultMessage : NetworkMessage
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
        public ERejectReason Reason      => _reason;
        public bool          IsAccepted  => _reason == ERejectReason.None;

        /****************************************
        *                Methods
        ****************************************/

        public IntroCompleteResultMessage(Guid operationId, ERejectReason reason)
        {
            _operationId = operationId;
            _reason      = reason;
        }
    }
}
