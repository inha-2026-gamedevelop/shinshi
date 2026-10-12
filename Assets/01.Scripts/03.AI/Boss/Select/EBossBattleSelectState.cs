namespace Shinshi.AI
{
    /// <summary> 보스와 난이도 선택이 보스전 ID로 이어졌는지 </summary>
    public enum EBossBattleSelectState
    {
        // 아직 고르지 않았다
        None,

        // 진행 카탈로그의 보스전 하나에 정확히 대응한다
        Resolved,

        // 진행 카탈로그나 보스 카탈로그를 읽을 수 없다
        MissingCatalog,

        // 보스 카탈로그에 없는 보스 ID다
        UnknownBoss,

        // 보스는 있지만 그 난이도의 보스전이 진행 카탈로그에 없다, 다른 보스전으로 바꾸지 않는다
        NoBattleForDifficulty
    }
}
