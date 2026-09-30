namespace Shinshi.Common
{
    /// <summary> 출정 흐름의 단계 </summary>
    public enum ESessionState
    {
        Lobby           = 0,
        ReadyCheck      = 1,
        Queued          = 2,
        Reserved        = 3,
        Loading         = 4,
        Starting        = 5,
        Running         = 6,
        Settling        = 7,
        Auction         = 8,
        Returning       = 9,
        Closed          = 10,
        RecoveryPending = 11
    }
}
