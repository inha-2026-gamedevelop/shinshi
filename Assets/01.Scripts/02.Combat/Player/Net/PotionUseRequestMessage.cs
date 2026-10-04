// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    // 참가자와 지참 수량, 회복량은 서버가 결합과 출정 예약으로 정한다. 메시지에는 입력 머리와 물약 종류만 싣는다
    /// <summary> 소유 클라이언트의 물약 사용 요청, 결과는 PotionStateMessage로 온다 </summary>
    public readonly struct PotionUseRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PlayerInputHeader _header;
        private readonly EPotionSize       _size;

        /****************************************
        *              Properties
        ****************************************/

        // 다른 전투 입력과 같은 순번 축이다. 서버 입력 게이트가 같은 기준으로 검증한다
        public PlayerInputHeader Header => _header;
        public EPotionSize       Size   => _size;

        /****************************************
        *                Methods
        ****************************************/

        public PotionUseRequestMessage(in PlayerInputHeader header, EPotionSize size)
        {
            _header = header;
            _size   = size;
        }
    }
}
