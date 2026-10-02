namespace Shinshi.Data.Admin
{
    /// <summary> 초대 폐기 결과. 만료나 사용을 다 한 초대도 아직 폐기하지 않았으면 Revoked다 </summary>
    public enum EAdminInviteRevokeStatus
    {
        Revoked,
        NotFound,
        AlreadyRevoked
    }
}
