namespace Shinshi.Network
{
    /// <summary> 정산안을 API 상한과 지급 금지 목록에 맞추며 고친 횟수, 고친 것이 있으면 호출자가 한 번 경고를 남긴다 </summary>
    public struct SettlementSanityReport
    {
        /****************************************
        *                Fields
        ****************************************/

        private int _clampedEssence;
        private int _droppedForbidden;
        private int _droppedInvalidId;
        private int _clampedQuantity;
        private int _droppedOverflow;
        private int _clampedWallet;
        private int _droppedInvalidTalisman;
        private int _clampedTalisman;
        private int _droppedTalismanOverflow;

        /****************************************
        *              Properties
        ****************************************/

        // 정기 상한으로 자른 참가자 수
        public int ClampedEssence   => _clampedEssence;

        // 아이템 줄에 들어온 지갑 자산 ID라 뺀 줄 수. 정상 경로는 지갑 칸으로 보내므로 0이어야 한다
        public int DroppedForbidden => _droppedForbidden;

        // 아이템 ID 형식이 아니라 뺀 줄 수
        public int DroppedInvalidId => _droppedInvalidId;
        public int ClampedQuantity  => _clampedQuantity;

        // 참가자 한 명의 줄 수 상한을 넘어 뺀 줄 수
        public int DroppedOverflow  => _droppedOverflow;

        // 정련편, 티켓, 엽전 칸을 상한으로 자른 횟수
        public int ClampedWallet    => _clampedWallet;

        // 부적 효과 ID 형식이 아니라 뺀 줄 수
        public int DroppedInvalidTalisman  => _droppedInvalidTalisman;
        public int ClampedTalisman         => _clampedTalisman;
        public int DroppedTalismanOverflow => _droppedTalismanOverflow;

        public bool HasCorrections =>
            (_clampedEssence + _droppedForbidden + _droppedInvalidId + _clampedQuantity + _droppedOverflow
                + _clampedWallet + _droppedInvalidTalisman + _clampedTalisman + _droppedTalismanOverflow) > 0;

        /****************************************
        *                Methods
        ****************************************/

        public void AddClampedEssence()      { ++_clampedEssence; }
        public void AddDroppedForbidden()    { ++_droppedForbidden; }
        public void AddDroppedInvalidId()    { ++_droppedInvalidId; }
        public void AddClampedQuantity()     { ++_clampedQuantity; }
        public void AddDroppedOverflow(int count) { _droppedOverflow += count; }
        public void AddClampedWallet()       { ++_clampedWallet; }
        public void AddDroppedInvalidTalisman() { ++_droppedInvalidTalisman; }
        public void AddClampedTalisman()     { ++_clampedTalisman; }
        public void AddDroppedTalismanOverflow(int count) { _droppedTalismanOverflow += count; }
    }
}
