namespace Shinshi.Network
{
    /// <summary> 소셜 API 호출 한 건의 결론 </summary>
    public enum ESocialApiVerdict
    {
        // API가 확정했다
        Committed = 0,

        // API가 처리하지 않거나 규칙으로 거절했다. 다시 보내도 같은 결과이거나 아무 효과가 없었다
        Rejected  = 1,

        // 보내지 않았다(회로 열림, 응답 대기 상한, 서버 임대 없음). 효과가 없었다
        NotSent   = 2,

        // 응답을 받지 못해 처리됐는지 모른다. 스냅샷으로 실제 상태를 확인한다
        Unknown   = 3
    }
}
