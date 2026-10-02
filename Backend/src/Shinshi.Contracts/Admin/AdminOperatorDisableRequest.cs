// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 운영자 비활성화(Owner). 대상의 열린 어드민 세션도 함께 폐기한다 </summary>
    [DataContract]
    public sealed class AdminOperatorDisableRequest : IAdminWriteRequest
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

        public AdminOperatorDisableRequest(string operationId, string reason)
        {
            _operationId = operationId;
            _reason      = reason;
        }

        private AdminOperatorDisableRequest() { }
    }
}
