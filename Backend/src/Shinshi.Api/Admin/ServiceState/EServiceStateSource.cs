namespace Shinshi.Backend.Admin
{
    /// <summary> 서비스 상태 스냅샷의 출처. Unavailable은 DB 모드에서 한 번도 읽지 못해 점검 중으로 막은 값이다 </summary>
    public enum EServiceStateSource
    {
        Configuration,
        Database,
        Unavailable
    }
}
