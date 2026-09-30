namespace Shinshi.Common
{
    /// <summary> 참가자 생사 상태, 연결 상태(EParticipantConnectionState)와 별도 축이다 </summary>
    public enum EParticipantLifeState
    {
        Alive         = 0,
        Dead          = 1,
        ReviveWaiting = 2,
        Spectating    = 3
    }
}
