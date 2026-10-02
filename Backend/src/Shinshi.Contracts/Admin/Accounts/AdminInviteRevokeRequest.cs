// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 초대 코드 폐기(Operator). 이미 만든 로그인 묶음은 폐기하지 않는다 </summary>
    [DataContract]
    public sealed class AdminInviteRevokeRequest : IAdminWriteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")] private string _operationId;
        [DataMember(Name = "reason")]      private string _reason;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;
        public string Reason      => _reason;

        /****************************************
        *                Methods
        ****************************************/

        public AdminInviteRevokeRequest(string operationId, string reason)
        {
            _operationId = operationId;
            _reason      = reason;
        }

        private AdminInviteRevokeRequest() { }
    }
}
