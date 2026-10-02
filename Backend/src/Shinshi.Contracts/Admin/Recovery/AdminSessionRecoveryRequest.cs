// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 수동 세션 복구(Admin). action은 AdminSessionRecoveryCodes 값이다 </summary>
    [DataContract]
    public sealed class AdminSessionRecoveryRequest : IAdminWriteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")] private string _operationId;
        [DataMember(Name = "reason")]      private string _reason;
        [DataMember(Name = "action")]      private string _action;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;
        public string Reason      => _reason;
        public string Action      => _action;

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionRecoveryRequest(string operationId, string reason, string action)
        {
            _operationId = operationId;
            _reason      = reason;
            _action      = action;
        }

        private AdminSessionRecoveryRequest() { }
    }
}
