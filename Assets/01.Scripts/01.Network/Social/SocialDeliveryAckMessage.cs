// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 서버는 이 연결에 보낸 기록이 있는 ID만 받아 API 확인 목록에 넣는다. 남의 ID를 넣어도 버려진다
    /// <summary> 받아서 처리한(표시했거나 중복으로 버린) 전달 ID 묶음, 한 메시지 최대 64개 </summary>
    public readonly struct SocialDeliveryAckMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly long[] _deliveryIds;

        /****************************************
        *              Properties
        ****************************************/

        public long[] DeliveryIds => _deliveryIds ?? Array.Empty<long>();

        /****************************************
        *                Methods
        ****************************************/

        public SocialDeliveryAckMessage(long[] deliveryIds)
        {
            _deliveryIds = deliveryIds;
        }
    }
}
