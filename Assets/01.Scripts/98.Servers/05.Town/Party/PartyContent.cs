namespace Shinshi.Network
{
    /// <summary> 방이 고른 전장의 출정 계약 값, ReadyRules가 GameDB에서 확인해 만든다 </summary>
    public readonly struct PartyContent
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly sbyte  _difficulty;
        private readonly string _bossId;
        private readonly int    _ticketCost;

        /****************************************
        *              Properties
        ****************************************/

        // GameSessionCodes.Difficulty, 일반전은 NONE
        public sbyte  Difficulty => _difficulty;

        // 보스전의 보스 ID, 일반전은 null
        public string BossId     => _bossId;

        // 보스전 참가자 한 명이 쓰는 티켓 수, 일반전은 0
        public int    TicketCost => _ticketCost;

        /****************************************
        *                Methods
        ****************************************/

        public PartyContent(sbyte difficulty, string bossId, int ticketCost)
        {
            _difficulty = difficulty;
            _bossId     = bossId;
            _ticketCost = ticketCost;
        }
    }
}
