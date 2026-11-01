// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 참가자는 서버가 연결 결합으로 정한다. 열린 투표가 없을 때 찬성을 보내면 새 투표를 연다
    /// <summary> 보스전 토벌 중단 투표의 한 표, 결과와 진행은 SortieStopVoteStateMessage로 온다 </summary>
    public readonly struct SortieStopVoteRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly bool _agree;

        /****************************************
        *              Properties
        ****************************************/

        public bool Agree => _agree;

        /****************************************
        *                Methods
        ****************************************/

        public SortieStopVoteRequestMessage(bool agree)
        {
            _agree = agree;
        }
    }
}
