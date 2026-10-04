// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 계정 하나의 생활 재고 조회. 계정 접속 임대가 호출 마을 서버의 것이 아니면 SESSION_MISMATCH로 거절한다 </summary>
    [DataContract]
    public sealed class LifeSnapshotRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]      private string _accountId;
        [DataMember(Name = "accountLeaseId")] private string _accountLeaseId;

        /****************************************
        *              Properties
        ****************************************/

        public string AccountId      => _accountId;
        public string AccountLeaseId => _accountLeaseId;

        /****************************************
        *                Methods
        ****************************************/

        public LifeSnapshotRequest(string accountId, string accountLeaseId)
        {
            _accountId      = accountId;
            _accountLeaseId = accountLeaseId;
        }

        private LifeSnapshotRequest() { }
    }
}
