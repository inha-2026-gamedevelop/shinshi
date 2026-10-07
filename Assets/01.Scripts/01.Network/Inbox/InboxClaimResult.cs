// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 우편 수령 결과, IsOutcomeUnknown이면 같은 요청을 다시 보내 확정할 수 있다 </summary>
    public readonly struct InboxClaimResult
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string        _inboxId;
        private readonly bool          _succeeded;
        private readonly ERejectReason _reason;
        private readonly bool          _isOutcomeUnknown;

        /****************************************
        *              Properties
        ****************************************/

        public string        InboxId          => _inboxId;
        public bool          Succeeded        => _succeeded;
        public ERejectReason Reason           => _reason;
        public bool          IsOutcomeUnknown => _isOutcomeUnknown;

        /****************************************
        *                Methods
        ****************************************/

        public InboxClaimResult(string inboxId, bool succeeded, ERejectReason reason, bool isOutcomeUnknown)
        {
            _inboxId          = inboxId;
            _succeeded        = succeeded;
            _reason           = reason;
            _isOutcomeUnknown = isOutcomeUnknown;
        }
    }
}
