namespace Shinshi.Network
{
    /// <summary> 마을 생활 요청과 결과 종류, 값은 직렬화되므로 바꾸지 않는다 </summary>
    public enum ELifeAction
    {
        Snapshot        = 0,
        GatherNodes     = 1,
        GatherStart     = 2,
        GatherCancel    = 3,
        PotionCraft     = 4,

        // 서버만 보낸다. 채집 완료 저장의 최종 결과
        GatherCompleted = 5
    }
}
