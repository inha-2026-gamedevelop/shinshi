namespace Shinshi.Data.Admin
{
    /// <summary> 공지 내리기 결과. 이미 내렸거나 노출 기간이 지난 공지는 AlreadyEnded다 </summary>
    public enum EServiceNoticeEndStatus
    {
        Ended,
        NotFound,
        AlreadyEnded
    }
}
