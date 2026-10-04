namespace Shinshi.UI
{
    /// <summary> 성장, 정산 결과가 서버에 저장되는 진행 상태 </summary>
    public enum ESaveStatus
    {
        Idle,
        Saving,
        Saved,
        Retrying
    }
}
