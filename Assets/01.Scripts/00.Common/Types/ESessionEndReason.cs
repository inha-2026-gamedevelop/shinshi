namespace Shinshi.Common
{
    /// <summary> 출정 종료 원인, Success가 아니면 보스 기본 보상을 만들지 않는다 </summary>
    public enum ESessionEndReason
    {
        Success           = 0,
        Timeout           = 1,
        AllDead           = 2,
        Abandon           = 3,
        DisconnectExpired = 4,
        ServiceCancelled  = 5
    }
}
