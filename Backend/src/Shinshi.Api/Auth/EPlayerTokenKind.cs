namespace Shinshi.Backend
{
    /// <summary> 플레이어 경로를 연 bearer의 종류. 발급 경로가 자기 종류의 토큰으로 더 발급하지 못하게 구분한다 </summary>
    public enum EPlayerTokenKind
    {
        Access,
        GameSession
    }
}
