namespace Shinshi.AI
{
    /// <summary> 돌진이 무엇에 막혔는가, 막히지 않았으면 None이다 </summary>
    public enum EBossChargeObstacleKind
    {
        None,

        // 정적 지형 벽, 이동 창구(IBossMovement)가 아는 장애물이다
        Wall,

        // 식별자가 있는 구조물, 구조물 조회(IBossChargeStructureQuery)가 아는 장애물이다
        Structure
    }
}
