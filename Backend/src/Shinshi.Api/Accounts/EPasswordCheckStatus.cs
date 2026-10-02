namespace Shinshi.Backend.Accounts
{
    /// <summary> 비밀번호 확인 결과. Busy는 해시 계산 자리를 2초 안에 얻지 못한 경우이며 실패로 세지 않는다 </summary>
    public enum EPasswordCheckStatus
    {
        Verified,
        InvalidCredentials,
        Locked,
        Slowed,
        Busy
    }
}
