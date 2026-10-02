namespace Shinshi.Data
{
    /// <summary> 저장 계층의 접속 임대 결과. 공용 DTO 거절 코드가 확정될 때까지 wire 숫자로 변환하지 않는다 </summary>
    public enum EAccountLeaseStatus
    {
        Acquired         = 0,
        AlreadyConnected = 1,
        ServerUnavailable = 2,
        AccountNotFound  = 3,
        Transferred     = 4,
        LeaseNotFound   = 5,
        LeaseExpired    = 6,
        LeaseOwnershipMismatch = 7,
        SourceServerUnavailable = 8,
        ServerRoleMismatch = 9,
        TransientFailure = 10,
        LeaseIdConflict = 11
    }

    /// <summary> 서버의 접속 임대 해제 결과. NotFound는 이미 풀렸거나 이관된 임대이며 멱등 성공으로 다룬다 </summary>
    public enum EAccountLeaseReleaseStatus
    {
        Released   = 0,
        NotFound   = 1,
        OtherOwner = 2
    }

    /// <summary> 접속 경합에서 패한 로그인은 기존 연결을 교체하지 않고 명시적으로 거절한다 </summary>
    public sealed class AccountLeaseOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public EAccountLeaseStatus Status { get; }
        public AccountLease? Lease { get; }
        public bool IsRetryable => Status == EAccountLeaseStatus.TransientFailure;
        public string Message => Status == EAccountLeaseStatus.AlreadyConnected ? "이미 접속 중인 계정" : string.Empty;

        /****************************************
        *                Methods
        ****************************************/

        public AccountLeaseOutcome(EAccountLeaseStatus status, AccountLease? lease)
        {
            Status = status;
            Lease  = lease;
        }
    }
}

