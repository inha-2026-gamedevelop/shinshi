// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 지급 초안 작성 결과. 같은 operationId 재전송은 처음 만든 지급 ID를 준다 </summary>
    [DataContract]
    public sealed class AdminGrantCreateResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operation")] private OperationResult _operation;
        [DataMember(Name = "grantId")]   private string          _grantId;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult Operation => _operation;
        public string          GrantId   => _grantId;

        /****************************************
        *                Methods
        ****************************************/

        public AdminGrantCreateResponse(OperationResult operation, string grantId)
        {
            _operation = operation;
            _grantId   = grantId;
        }

        private AdminGrantCreateResponse() { }
    }
}
