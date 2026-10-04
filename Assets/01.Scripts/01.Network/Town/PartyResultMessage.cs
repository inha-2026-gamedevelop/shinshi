// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 파티 요청 한 건의 결과, 요청한 클라이언트에게만 온다. None이면 받아들였다 </summary>
    public readonly struct PartyResultMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EPartyAction  _action;
        private readonly ERejectReason _reason;

        /****************************************
        *              Properties
        ****************************************/

        public EPartyAction  Action     => _action;
        public ERejectReason Reason     => _reason;
        public bool          IsAccepted => _reason == ERejectReason.None;

        /****************************************
        *                Methods
        ****************************************/

        public PartyResultMessage(EPartyAction action, ERejectReason reason)
        {
            _action = action;
            _reason = reason;
        }
    }
}
