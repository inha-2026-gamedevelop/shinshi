namespace Shinshi.AI
{
    /// <summary> 일러스트 화면에서 클릭 한 번이 일으킨 결과 </summary>
    public enum EIllustrationClick
    {
        // 이미 끝난 뒤의 클릭
        None         = 0,
        // 출력 중이던 글을 전부 보여 줬다
        RevealedText = 1,
        // 다음 장으로 넘어갔다
        NextPage     = 2,
        // 마지막 장을 닫아 일러스트 구간이 끝났다
        Finished     = 3
    }
}
