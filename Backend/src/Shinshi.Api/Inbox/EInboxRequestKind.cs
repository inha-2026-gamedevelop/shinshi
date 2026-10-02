namespace Shinshi.Backend.Inbox
{
    /// <summary> 우편함 계정별 요청 한도의 종류. 목록과 수령은 한도를 따로 센다 </summary>
    public enum EInboxRequestKind
    {
        List  = 0,
        Claim = 1
    }
}
