namespace Shinshi.Patch.Client
{
    /// <summary> 채널이 가리키는 릴리스와 활성 설치의 관계 </summary>
    public enum EPatchCheckStatus
    {
        Failed,
        NotInstalled,
        UpToDate,
        UpdateAvailable
    }
}
