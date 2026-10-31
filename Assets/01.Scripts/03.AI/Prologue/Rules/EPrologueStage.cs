namespace Shinshi.AI
{
    /// <summary> 프롤로그의 큰 단계, 재개를 위해 저장하는 값은 이것 하나다. 저장되는 번호라 바꾸지 않는다 </summary>
    public enum EPrologueStage
    {
        // 세계관 도입 일러스트
        Intro   = 0,
        // 부적 조사와 산귀 전투 두 번
        Forest  = 1,
        // 월백 등장 일러스트와 최대 30초 교전
        Wolbaek = 2,
        // 마무리 일러스트와 신시 귀환
        Ending  = 3,
        // 신시 도착으로 끝난 상태
        Done    = 4
    }
}
