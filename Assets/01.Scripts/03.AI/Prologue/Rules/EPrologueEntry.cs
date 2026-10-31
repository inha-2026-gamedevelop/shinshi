namespace Shinshi.AI
{
    /// <summary> 캐릭터를 만든 직후 프롤로그를 어떻게 시작하는가 </summary>
    public enum EPrologueEntry
    {
        // 계정에서 처음이다. 플레이 튜토리얼은 반드시 하고 이야기 일러스트만 건너뛸 수 있다
        PlayRequired = 0,
        // 계정에서 이미 한 번 마쳤다. 진행할지 건너뛸지 고르게 한다
        AskPlayOrSkip = 1
    }
}
