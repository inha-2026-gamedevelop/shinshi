namespace Shinshi.Network
{
    /// <summary> 채팅 송신을 거절한 이유, 메시지에 번호로 실리므로 값을 바꾸지 않고 끝에 붙인다 </summary>
    public enum EChatReject
    {
        None                  = 0,
        Empty                 = 1,
        TooLong               = 2,
        InvalidCharacter      = 3,
        RateLimited           = 4,
        NoParty               = 5,
        InvalidChannel        = 6,
        NotConnected          = 7,
        NoGuild               = 8,
        WhisperTargetNotFound = 9,
        WhisperTargetOffline  = 10,
        WhisperSelf           = 11,
        BackendUnavailable    = 12,
        FeatureDisabled       = 13
    }
}
