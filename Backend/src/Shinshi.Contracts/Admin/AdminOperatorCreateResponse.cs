// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 운영자 생성 결과. 비밀값은 생성을 확정한 그 응답에만 있고, 같은 operationId 재전송에는 null이다 </summary>
    [DataContract]
    public sealed class AdminOperatorCreateResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operation")]        private OperationResult _operation;
        [DataMember(Name = "adminId")]          private string          _adminId;
        [DataMember(Name = "loginName")]        private string          _loginName;
        [DataMember(Name = "role")]             private int             _role;

        // 서버는 해시와 암호문만 저장하므로 응답을 잃으면 계정을 비활성화하고 다시 만든다
        [DataMember(Name = "initialPassword")]  private string          _initialPassword;
        [DataMember(Name = "totpSecretBase32")] private string          _totpSecretBase32;
        [DataMember(Name = "totpUri")]          private string          _totpUri;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult Operation        => _operation;
        public string          AdminId          => _adminId;
        public string          LoginName        => _loginName;
        public int             Role             => _role;
        public string          InitialPassword  => _initialPassword;
        public string          TotpSecretBase32 => _totpSecretBase32;
        public string          TotpUri          => _totpUri;

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperatorCreateResponse(OperationResult operation, string adminId, string loginName, int role,
            string initialPassword, string totpSecretBase32, string totpUri)
        {
            _operation        = operation;
            _adminId          = adminId;
            _loginName        = loginName;
            _role             = role;
            _initialPassword  = initialPassword;
            _totpSecretBase32 = totpSecretBase32;
            _totpUri          = totpUri;
        }

        private AdminOperatorCreateResponse() { }
    }
}
