// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 지급 실행, 취소를 확정한 결과와 응답 시점의 지급 상태. 재전송도 현재 상태를 다시 읽어 준다 </summary>
    [DataContract]
    public sealed class AdminGrantActionResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operation")] private OperationResult  _operation;
        [DataMember(Name = "grant")]     private AdminGrantDetail _grant;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult  Operation => _operation;
        public AdminGrantDetail Grant     => _grant;

        /****************************************
        *                Methods
        ****************************************/

        public AdminGrantActionResponse(OperationResult operation, AdminGrantDetail grant)
        {
            _operation = operation;
            _grant     = grant;
        }

        private AdminGrantActionResponse() { }
    }
}
