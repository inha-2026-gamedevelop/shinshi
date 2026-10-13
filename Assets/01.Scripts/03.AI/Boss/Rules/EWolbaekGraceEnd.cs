namespace Shinshi.AI
{
    /// <summary> 복귀자의 합류 유예가 끝난 이유 </summary>
    public enum EWolbaekGraceEnd
    {
        // 같은 색 동료와 연결 거리 이내로 합류했다
        Joined,

        // 합류 시간이 끝났다, 그때도 멀면 거리 피해가 시작된다
        Expired,

        // 유예 중 다시 쓰러지거나 접속이 끊겼다
        Interrupted
    }
}
