namespace Shinshi.AI
{
    /// <summary> 돌진이 어떻게 끝났는가, 취소된 시전은 끝 종류가 없어 None이다 </summary>
    public enum EBossChargeEnd
    {
        None,

        // 정해진 거리까지 막힘 없이 갔다
        Normal,

        // 벽이나 구조물에 막혀 거리를 채우지 못했다
        Collision
    }
}
