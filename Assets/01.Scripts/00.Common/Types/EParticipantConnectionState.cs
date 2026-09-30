namespace Shinshi.Common
{
    /// <summary> 참가자 연결 상태, 생사(EParticipantLifeState)와 별도 축이다 </summary>
    public enum EParticipantConnectionState
    {
        Connected      = 0,
        ReconnectGrace = 1,
        Departed       = 2
    }
}
