namespace Shinshi.Network
{
    /// <summary> 게임 세션 갱신 응답 하나를 어떻게 처리할지 </summary>
    public enum EGameSessionRefreshOutcome
    {
        Succeeded,

        // 서버에 닿지 못했다(연결 실패, 시간 초과). 같은 토큰으로 다시 보낸다
        RetryTransport,

        // 토큰이 폐기되거나 만료됐다. 런처로 다시 실행해야 한다
        SessionEnded,

        // 그 밖의 거절이나 잘못된 응답. 다시 보내도 같으므로 이 세션으로는 더 갱신하지 않는다
        Stopped
    }
}
