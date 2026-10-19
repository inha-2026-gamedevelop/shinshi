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
        private readonly uint          _requestId;

        /****************************************
        *              Properties
        ****************************************/

        public EPartyAction  Action     => _action;
        public ERejectReason Reason     => _reason;
        public bool          IsAccepted => _reason == ERejectReason.None;

        // 요청에 실어 보낸 상관 ID를 그대로 돌려준 값. 0이면 상관 ID 없이 보낸 요청의 결과다
        public uint          RequestId  => _requestId;

        /****************************************
        *                Methods
        ****************************************/

        public PartyResultMessage(EPartyAction action, ERejectReason reason, uint requestId = 0)
        {
            _requestId = requestId;
            _action = action;
            _reason = reason;
        }
    }
}
