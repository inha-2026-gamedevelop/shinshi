namespace Shinshi.Network
{
    /// <summary> 파티 참가자의 준비 상태, Checking은 서버가 티켓과 물약을 API로 확인하는 중이다 </summary>
    public enum EPartyReadyState
    {
        NotReady = 0,
        Checking = 1,
        Ready    = 2
    }
}
