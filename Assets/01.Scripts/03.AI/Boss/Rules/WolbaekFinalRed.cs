namespace Shinshi.AI
{
    // 추적 대상은 묶음 시작 순간의 생존자다. 대상이 쓰러지거나 접속이 끊겨도 다른 사람에게 넘기지 않고, 부활자는 다음 묶음부터 대상이 된다
    /// <summary> 빨간 여우불 하나의 입력, 한 묶음 안의 번호와 추적 대상 </summary>
    public readonly struct WolbaekFinalRed
    {
        /****************************************
        *              Properties
        ****************************************/

        public int Index { get; }

        public int TargetCombatId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekFinalRed(int index, int targetCombatId)
        {
            Index          = index;
            TargetCombatId = targetCombatId;
        }
    }
}
