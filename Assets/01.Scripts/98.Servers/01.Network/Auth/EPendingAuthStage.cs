namespace Shinshi.Network
{
    /// <summary> 인증 대기 연결의 단계 </summary>
    public enum EPendingAuthStage
    {
        // 연결됐고 인증 요청을 기다린다
        Waiting   = 0,

        // 요청을 받아 토큰 검증 결과를 기다린다
        Verifying = 1,

        // 거절 응답을 보냈고 전송될 시간을 준 뒤 끊는다
        Closing   = 2
    }
}
