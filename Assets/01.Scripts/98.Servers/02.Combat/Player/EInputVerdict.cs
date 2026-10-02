namespace Shinshi.Combat
{
    /// <summary> 입력 헤더 검증 결과, Accepted가 아니면 입력을 버린다 </summary>
    public enum EInputVerdict
    {
        Accepted            = 0,

        // 아래 셋은 정상 클라이언트가 보낼 수 없는 값이라 바로 연결을 끊는다
        ProtocolMismatch    = 1,
        SessionMismatch     = 2,
        ParticipantMismatch = 3,

        // 아래 셋은 위반으로 세고 창 안에서 임계값을 넘으면 연결을 끊는다
        RateLimited         = 4,
        StaleSequence       = 5,
        InvalidTime         = 6
    }
}
