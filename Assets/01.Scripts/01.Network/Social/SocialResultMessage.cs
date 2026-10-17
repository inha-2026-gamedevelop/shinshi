// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // BackendUnavailable은 결과 불명이다. 서버가 이어서 스냅샷을 다시 조회해 보내고, 조회도 실패하면
    // Action이 Snapshot이고 RequestId가 비어 있는 BackendUnavailable 결과가 한 번 더 온다
    /// <summary> 소셜 관리 요청 한 건의 결과, 요청한 클라이언트에게만 온다. 바뀐 목록은 SocialSnapshotMessage로 따로 온다 </summary>
    public readonly struct SocialResultMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ESocialAction _action;
        private readonly Guid          _requestId;
        private readonly ERejectReason _reason;

        /****************************************
        *              Properties
        ****************************************/

        public ESocialAction Action     => _action;
        public Guid          RequestId  => _requestId;
        public ERejectReason Reason     => _reason;
        public bool          IsAccepted => _reason == ERejectReason.None;

        /****************************************
        *                Methods
        ****************************************/

        public SocialResultMessage(ESocialAction action, Guid requestId, ERejectReason reason)
        {
            _action    = action;
            _requestId = requestId;
            _reason    = reason;
        }
    }
}
