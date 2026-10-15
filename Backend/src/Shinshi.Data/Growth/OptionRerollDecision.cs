namespace Shinshi.Data.Growth
{
    /// <summary> 옵션 재설정 한 번의 판정 결과. 새 옵션 두 줄과 이 캐릭터, 부위에 저장할 다음 누계 </summary>
    public sealed class OptionRerollDecision
    {
        /****************************************
        *              Properties
        ****************************************/

        public EquipmentOptionLine Line1       { get; }
        public EquipmentOptionLine Line2       { get; }

        // 상급 줄이 하나도 없던 재설정의 연속 횟수. 이번 결과에 상급 줄이 있으면 0이다
        public int                 NextMisses  { get; }

        // 천장으로 줄1의 값을 최댓값으로 확정했는지
        public bool                PityApplied { get; }

        /****************************************
        *                Methods
        ****************************************/

        public OptionRerollDecision(EquipmentOptionLine line1, EquipmentOptionLine line2, int nextMisses, bool pityApplied)
        {
            Line1       = line1;
            Line2       = line2;
            NextMisses  = nextMisses;
            PityApplied = pityApplied;
        }
    }
}
