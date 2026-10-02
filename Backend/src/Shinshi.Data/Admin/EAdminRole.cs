namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 역할. 높을수록 낮은 역할의 권한을 모두 가진다. AdminContractCodes.Role과 같은 값이며 재배열하지 않는다 </summary>
    public enum EAdminRole
    {
        Viewer   = 0,
        Operator = 1,
        Admin    = 2,
        Owner    = 3
    }
}
