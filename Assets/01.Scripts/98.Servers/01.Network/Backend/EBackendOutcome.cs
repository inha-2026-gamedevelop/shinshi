namespace Shinshi.Network
{
    /// <summary> 백엔드 호출의 최종 결과 종류, Succeeded만 API가 요청을 받아들였다는 뜻이다 </summary>
    public enum EBackendOutcome
    {
        // 2xx와 응답 본문 해석 성공
        Succeeded       = 0,

        // API가 거절했다(4xx). ReasonCode에 ContractCodes.RejectReason 값이 있다
        Rejected        = 1,

        // 시간 제한 안에 응답이 없었다. 요청이 API에 도착해 처리됐을 수도 있다
        Timeout         = 2,

        // 연결, DNS, TLS 실패
        NetworkError    = 3,

        // 408, 429, 5xx
        ServerError     = 4,

        // 2xx인데 본문을 계약 DTO로 읽지 못했다
        InvalidResponse = 5,

        // 연속 실패로 회로가 열려 보내지 않았다
        CircuitOpen     = 6,

        // 응답 대기 요청이 상한에 닿아 보내지 않았다
        Overloaded       = 7,

        // 서버 임대가 없거나 만료돼 보내지 않았다. 새 임대를 받으면 다시 보낼 수 있는 일시 실패다
        LeaseUnavailable = 8
    }
}
