// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 초대 코드 발급(Operator). accountId가 없으면 새 계정을 만들고, 있으면 기기 변경 재발급이다 </summary>
    [DataContract]
    public sealed class AdminInviteIssueRequest : IAdminWriteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MIN_USES  = 1;
        public const int MAX_USES  = 10;
        public const int MIN_HOURS = 1;
        public const int MAX_HOURS = 168;

        [DataMember(Name = "operationId")]            private string _operationId;
        [DataMember(Name = "reason")]                 private string _reason;

        // null이면 새 계정
        [DataMember(Name = "accountId")]              private string _accountId;
        [DataMember(Name = "maxUses")]                private int    _maxUses;
        [DataMember(Name = "expiresInHours")]         private int    _expiresInHours;

        // null이면 true. 기존 로그인, 게임 세션, 미소비 입장 토큰, 이전 초대를 함께 폐기한다
        [DataMember(Name = "revokeExistingSessions")] private bool?  _revokeExistingSessions;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId            => _operationId;
        public string Reason                 => _reason;
        public string AccountId              => _accountId;
        public int    MaxUses                => _maxUses;
        public int    ExpiresInHours         => _expiresInHours;
        public bool?  RevokeExistingSessions => _revokeExistingSessions;

        /****************************************
        *                Methods
        ****************************************/

        public AdminInviteIssueRequest(string operationId, string reason, string accountId, int maxUses,
            int expiresInHours, bool? revokeExistingSessions)
        {
            _operationId            = operationId;
            _reason                 = reason;
            _accountId              = accountId;
            _maxUses                = maxUses;
            _expiresInHours         = expiresInHours;
            _revokeExistingSessions = revokeExistingSessions;
        }

        private AdminInviteIssueRequest() { }
    }
}
