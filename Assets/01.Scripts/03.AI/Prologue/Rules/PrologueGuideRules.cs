namespace Shinshi.AI
{
    /// <summary> 화면 위쪽 목표와 아래쪽 설명 두 줄 </summary>
    public readonly struct PrologueGuideLines
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _objective;
        private readonly string _instruction;

        /****************************************
        *              Properties
        ****************************************/

        public string Objective   => _objective;
        public string Instruction => _instruction;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueGuideLines(string objective, string instruction)
        {
            _objective   = objective ?? string.Empty;
            _instruction = instruction ?? string.Empty;
        }
    }

    /// <summary> 지금 상태에서 화면에 보일 목표와 설명을 정하는 순수 규칙, 시험 장치와 실제 클라이언트가 같은 규칙을 쓴다 </summary>
    public static class PrologueGuideRules
    {
        /// <summary> 체력이 줄었고 임시 물약이 남은 숲일 때만 참이다, 물약 사용은 진행 조건이 아니다 </summary>
        public static bool PotionHintActive(EPrologueStage stage, int potionCount, bool isDamaged)
        {
            return (stage == EPrologueStage.Forest) && (potionCount > 0) && (isDamaged);
        }

        /// <summary> 월백이 열리면 월백 안내가 우선이다. 숲에서는 위험 범위 안내가 가장 급하고 그다음이 물약이며, 물약 안내는 아래 설명만 바꾸고 목표는 그대로 둔다 </summary>
        public static PrologueGuideLines Resolve(EPrologueStage stage, EForestHint hint, bool isWolbaekOpen, bool potionHintActive)
        {
            if ((stage == EPrologueStage.Wolbaek) && (isWolbaekOpen))
            {
                return new PrologueGuideLines(PrologueHintTexts.WOLBAEK_OBJECTIVE, PrologueHintTexts.WOLBAEK_INSTRUCTION);
            }

            if (stage != EPrologueStage.Forest)
            {
                return new PrologueGuideLines(string.Empty, string.Empty);
            }

            string objective   = PrologueHintTexts.ObjectiveFor(hint);
            string instruction = PrologueHintTexts.InstructionFor(hint);

            if ((potionHintActive) && (hint != EForestHint.LeaveDangerZone))
            {
                instruction = PrologueHintTexts.POTION_INSTRUCTION;
            }

            return new PrologueGuideLines(objective, instruction);
        }
    }
}
