// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 참가자는 서버가 연결 결합으로 정한다. 메시지에는 재전송을 한 번만 처리하기 위한 작업 ID만 싣는다
    /// <summary> 소유 클라이언트의 출정 포기 요청, 결과는 SortieAbandonResultMessage로 온다 </summary>
    public readonly struct SortieAbandonRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid _operationId;

        /****************************************
        *              Properties
        ****************************************/

        // 같은 값으로 다시 보내면 서버는 처음 결과를 다시 보낸다
        public Guid OperationId => _operationId;

        /****************************************
        *                Methods
        ****************************************/

        public SortieAbandonRequestMessage(Guid operationId)
        {
            _operationId = operationId;
        }
    }
}
