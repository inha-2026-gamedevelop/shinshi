namespace Shinshi.AI
{
    /// <summary> 캐릭터 생성 직후 프롤로그 진입 규칙, 엔진 비의존 순수 규칙이다 </summary>
    public static class PrologueEntryRules
    {
        /// <summary> 계정이 프롤로그를 한 번 마쳤는지로 진입 방식을 정한다 </summary>
        public static EPrologueEntry Decide(bool accountCompleted)
        {
            return accountCompleted ? EPrologueEntry.AskPlayOrSkip : EPrologueEntry.PlayRequired;
        }

        /// <summary> 선택에 따라 시작할 단계, 건너뛰면 바로 완료 단계다. 완료 처리와 지급품은 서버가 맡는다 </summary>
        public static EPrologueStage StartStage(EPrologueChoice choice)
        {
            return (choice == EPrologueChoice.Play) ? EPrologueStage.Intro : EPrologueStage.Done;
        }
    }
}
