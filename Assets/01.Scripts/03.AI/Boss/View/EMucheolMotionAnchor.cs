namespace Shinshi.AI
{
    // 프리뷰의 패턴 경과 시간 대신 서버 사건이 알려 주는 시각을 기준으로 시간 키를 정한다.
    // 서버 예고 시간이나 타 지연이 바뀌어도 타격 순간이 서버 확정 시각에 그대로 맞는다
    /// <summary> 무철 모션 시간 키가 기대는 서버 시각의 종류 </summary>
    public enum EMucheolMotionAnchor
    {
        /// <summary> 예고 시작 서버 시각 </summary>
        CastStart = 0,

        /// <summary> 예고가 끝나 실행이 시작되는 서버 시각, 돌진 예고와 재조준의 끝이기도 하다 </summary>
        TelegraphEnd = 1,

        /// <summary> StrikeIndex번째 타가 확정되는 서버 시각 </summary>
        Strike = 2,

        /// <summary> 돌진 시작 서버 시각 </summary>
        DashStart = 3,

        /// <summary> 돌진이 최대 거리에 닿는 서버 시각, 시작 시각과 돌진 시간으로 정한다 </summary>
        DashEnd = 4,

        /// <summary> 충돌처럼 한 번 오는 사건의 서버 시각 </summary>
        Event = 5,

        /// <summary> 그로기가 끝나 보스가 다시 행동하는 서버 시각 </summary>
        GroggyEnd = 6
    }
}
