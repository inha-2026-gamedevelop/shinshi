namespace Shinshi.Launcher.Account
{
    /// <summary> 인증 API 호출의 실패 종류 </summary>
    public enum EAuthApiError
    {
        None,

        // 요청이 서버에 닿지 않았다(연결 거부, 이름 해석 실패, TLS 실패). 서버는 아무것도 처리하지 않았다
        Unreachable,

        // 요청은 보냈지만 응답을 잃었다(시간 초과, 연결 끊김, 게이트웨이 시간 초과). 서버가 처리했을 수 있다
        ResponseLost,

        // 401. 토큰이나 초대 코드가 틀리거나 만료, 폐기됐다
        Unauthorized,

        // 설치 빌드의 protocolVersion, contentHash가 서버와 다르거나 허용 buildId가 아니다
        VersionMismatch,

        Maintenance,
        RateLimited,

        // 그 밖의 4xx 거절. ReasonCode에 ContractCodes.RejectReason 값이 있다
        Rejected,

        // 5xx. 서버 안에서 실패해 처리를 되돌렸다고 본다
        ServerError,

        // 2xx인데 본문을 읽지 못했다. 서버는 처리를 끝냈으므로 응답을 잃은 것과 같다
        Malformed
    }
}
