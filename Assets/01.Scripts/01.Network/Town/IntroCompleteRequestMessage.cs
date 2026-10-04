// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 계정은 서버가 인증된 연결의 결합에서 정한다. 클라이언트는 operationId만 보낸다(응답을 잃었을 때만 같은 값을 다시 쓴다).
    // 안내를 실제로 끝냈는지의 판정 기준은 U18(프롤로그, 튜토리얼) 구현 때 정한다
    /// <summary> 클라이언트가 신시 안내를 끝냈다고 알리는 요청, 결과는 IntroCompleteResultMessage로 온다 </summary>
    public readonly struct IntroCompleteRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid _operationId;

        /****************************************
        *              Properties
        ****************************************/

        public Guid OperationId => _operationId;

        /****************************************
        *                Methods
        ****************************************/

        public IntroCompleteRequestMessage(Guid operationId)
        {
            _operationId = operationId;
        }
    }
}
