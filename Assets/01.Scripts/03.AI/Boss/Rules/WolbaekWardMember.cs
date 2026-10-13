namespace Shinshi.AI
{
    // 시작 명단의 한 사람이다. 색과 문양은 사망, 부활, 재접속을 거쳐도 바뀌지 않는다
    /// <summary> 색 결계 기믹의 필수 참가자 한 명, 전투 번호와 배정된 색과 문양 </summary>
    public readonly struct WolbaekWardMember
    {
        /****************************************
        *              Properties
        ****************************************/

        public int CombatId { get; }

        public EWolbaekWardColor Color { get; }

        /// <summary> 문양 번호, 색마다 다르다. 실제 그림은 아트 작업이 정한다 </summary>
        public int SymbolId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekWardMember(int combatId, EWolbaekWardColor color, int symbolId)
        {
            CombatId = combatId;
            Color    = color;
            SymbolId = symbolId;
        }
    }
}
