// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 초대 발급 결과. 코드 원문은 발급을 확정한 그 응답에만 있고 같은 operationId 재전송에는 null이다 </summary>
    [DataContract]
    public sealed class AdminInviteIssueResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operation")]       private OperationResult _operation;
        [DataMember(Name = "inviteId")]        private string          _inviteId;
        [DataMember(Name = "accountId")]       private string          _accountId;
        [DataMember(Name = "maxUses")]         private long            _maxUses;
        [DataMember(Name = "expiresAtUnixMs")] private long            _expiresAtUnixMs;

        // 서버는 HMAC만 저장하므로 응답을 잃으면 이 초대를 폐기하고 새로 발급한다
        [DataMember(Name = "inviteCode")]      private string          _inviteCode;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult Operation       => _operation;
        public string          InviteId        => _inviteId;
        public string          AccountId       => _accountId;
        public long            MaxUses         => _maxUses;
        public long            ExpiresAtUnixMs => _expiresAtUnixMs;
        public string          InviteCode      => _inviteCode;

        /****************************************
        *                Methods
        ****************************************/

        public AdminInviteIssueResponse(OperationResult operation, string inviteId, string accountId, long maxUses,
            long expiresAtUnixMs, string inviteCode)
        {
            _operation       = operation;
            _inviteId        = inviteId;
            _accountId       = accountId;
            _maxUses         = maxUses;
            _expiresAtUnixMs = expiresAtUnixMs;
            _inviteCode      = inviteCode;
        }

        private AdminInviteIssueResponse() { }
    }
}
