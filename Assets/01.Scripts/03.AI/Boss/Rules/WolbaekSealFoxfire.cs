namespace Shinshi.AI
{
    // 추적 대상은 차례 시작 순간의 생존자다. 대상이 쓰러지거나 접속이 끊겨도 다른 사람에게 넘기지 않는다
    /// <summary> 추적 여우불 하나의 입력, 한 차례 안의 번호와 추적 대상 </summary>
    public readonly struct WolbaekSealFoxfire
    {
        /****************************************
        *              Properties
        ****************************************/

        public int Index { get; }

        public int TargetCombatId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekSealFoxfire(int index, int targetCombatId)
        {
            Index          = index;
            TargetCombatId = targetCombatId;
        }
    }
}
