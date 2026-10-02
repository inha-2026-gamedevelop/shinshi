// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 지급 실행과 취소 요청. 대상 지급은 경로의 grantId다 </summary>
    [DataContract]
    public sealed class AdminGrantActionRequest : IAdminWriteRequest
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

        public AdminGrantActionRequest(string operationId, string reason)
        {
            _operationId = operationId;
            _reason      = reason;
        }

        private AdminGrantActionRequest() { }
    }
}
