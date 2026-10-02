namespace Shinshi.Backend.Accounts
{
    /// <summary> 비밀번호 시도 허용 판정. Locked는 그 (아이디, IP) 쌍만 막고, Slowed는 아이디 전체 실패가 많아 IP마다 1분 1회로 늦춘 상태다 </summary>
    public enum EPasswordAttemptStatus
    {
        Allowed,
        Locked,
        Slowed
    }
}
