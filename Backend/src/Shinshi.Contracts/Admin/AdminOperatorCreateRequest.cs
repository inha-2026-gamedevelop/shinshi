// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 운영자 계정 생성(Owner). 비밀번호와 TOTP 비밀은 서버가 만들어 응답으로 한 번만 준다 </summary>
    [DataContract]
    public sealed class AdminOperatorCreateRequest : IAdminWriteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")] private string _operationId;
        [DataMember(Name = "reason")]      private string _reason;

        // 소문자, 숫자, 점, 밑줄, 하이픈 3~64자. 서버가 소문자로 바꿔 저장한다
        [DataMember(Name = "loginName")]   private string _loginName;

        // AdminContractCodes.Role
        [DataMember(Name = "role")]        private int    _role;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;
        public string Reason      => _reason;
        public string LoginName   => _loginName;
        public int    Role        => _role;

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperatorCreateRequest(string operationId, string reason, string loginName, int role)
        {
            _operationId = operationId;
            _reason      = reason;
            _loginName   = loginName;
            _role        = role;
        }

        private AdminOperatorCreateRequest() { }
    }
}
