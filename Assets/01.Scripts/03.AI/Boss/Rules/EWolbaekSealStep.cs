namespace Shinshi.AI
{
    /// <summary> 한 차례 안의 단계, 진행 중인 차례에서만 의미가 있다 </summary>
    public enum EWolbaekSealStep
    {
        // 핵과 여우불이 나타났고 아직 추적하지 않는다
        Prepare,

        // 여우불의 낙하 위치가 대상을 따라간다
        Track,

        // 낙하 위치가 고정됐고 대상이 빠져나갈 시간이다
        Escape,

        // 낙하와 핵 파괴가 판정된 뒤 다음 차례까지의 정리 시간이다
        Cleanup
    }
}
