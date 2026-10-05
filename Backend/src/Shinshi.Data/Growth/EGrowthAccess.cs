namespace Shinshi.Data.Growth
{
    /// <summary> 마을 서버가 캐릭터 성장 상태를 읽을 수 있는지 </summary>
    public enum EGrowthAccess
    {
        Allowed       = 0,
        NotFound      = 1,
        WrongLocation = 2
    }
}
