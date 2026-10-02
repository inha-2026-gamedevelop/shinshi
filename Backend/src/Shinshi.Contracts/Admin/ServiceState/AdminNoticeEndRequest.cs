// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 노출 기간이 끝나기 전에 공지를 내린다(Operator). 행은 지우지 않고 종료 시각만 남긴다 </summary>
    [DataContract]
    public sealed class AdminNoticeEndRequest : IAdminWriteRequest
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

        public AdminNoticeEndRequest(string operationId, string reason)
        {
            _operationId = operationId;
            _reason      = reason;
        }

        private AdminNoticeEndRequest() { }
    }
}
