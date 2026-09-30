namespace Shinshi.Common
{
    /// <summary> 요청 결과 상태, Pending은 수락만 했을 뿐 성공이 아니다 </summary>
    public enum EOperationStatus
    {
        Pending   = 0,
        Committed = 1,
        Rejected  = 2
    }
}
