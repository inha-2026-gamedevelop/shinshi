namespace Shinshi.Network
{
    /// <summary> 런처 세션을 받지 못한 사유, 로그와 안내에만 쓰고 토큰 내용은 담지 않는다 </summary>
    public enum ELauncherSessionFailure
    {
        None,
        InvalidArgs,
        UnsupportedPlatform,
        NoMainThreadContext,
        ConnectFailed,
        ServerProcessMismatch,
        ReadFailed,
        InvalidPayload,
        ProtocolMismatch,
        EmptyAccount,
        EmptyToken,
        Expired,
        InvalidApiBaseUrl,
        AckFailed,
        Timeout,

        // 받은 뒤 갱신하려 했더니 API가 토큰을 폐기된 것으로 거절했다
        Revoked
    }
}
