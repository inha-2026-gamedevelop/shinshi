namespace Shinshi.Network
{
    /// <summary> 연결 하나의 소셜 관리 요청 판정 결과 </summary>
    public enum ESocialGateVerdict
    {
        // API로 보낸다
        Accept          = 0,

        // 처리 중인 같은 RequestId의 재전송이다. 결과가 곧 가므로 답하지 않는다
        IgnoreDuplicate = 1,

        // Reason으로 거절한다
        Reject          = 2
    }
}
