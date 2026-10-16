namespace Shinshi.Combat
{
    /// <summary> 서버 콤보 기본 공격의 단계 선택, 서버 판정과 클라이언트 표현이 같은 규칙을 쓴다 </summary>
    public static class BasicAttackComboRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int NO_STEP = -1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이번에 쓸 단계, 끊겼거나 다음 공격이 가능해진 뒤 초기화 시간보다 오래 쉬었으면 1단(0)이다 </summary>
        public static int SelectStep(int lastStep, double idleSeconds, float resetSeconds, int stepCount)
        {
            if ((stepCount <= 1) || (lastStep < 0) || (idleSeconds > resetSeconds))
            {
                return 0;
            }

            return (lastStep + 1) % stepCount;
        }
    }
}
