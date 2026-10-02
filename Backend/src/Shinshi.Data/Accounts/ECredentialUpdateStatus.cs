namespace Shinshi.Data.Accounts
{
    /// <summary> 자격 변경 결과. Conflict는 검증한 뒤 다른 요청이 먼저 자격을 바꾼 경우, RecoveryNotAllowed는 현재 비밀번호 없는
    /// 변경을 요청한 로그인 묶음이 초대(복구 코드)로 시작하지 않았거나 30분이 지났거나 폐기된 경우다 </summary>
    public enum ECredentialUpdateStatus
    {
        Updated,
        LoginIdTaken,
        Conflict,
        RecoveryNotAllowed
    }
}
