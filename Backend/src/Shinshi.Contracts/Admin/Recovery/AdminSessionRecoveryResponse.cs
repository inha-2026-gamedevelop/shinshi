// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 확정된 수동 복구. operation은 어드민 작업 결과이고, 실제 정산이나 장애 취소는 세션 배정 서버를 actor로 한
    /// 별도 작업(recoveryActorId, recoveryOperationId)이다. 그 원장은 GET operations/{recoveryActorId}/{recoveryOperationId}로 본다 </summary>
    [DataContract]
    public sealed class AdminSessionRecoveryResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operation")]           private OperationResult _operation;
        [DataMember(Name = "sessionId")]           private string          _sessionId;
        [DataMember(Name = "action")]              private string          _action;
        [DataMember(Name = "recoveryActorId")]     private string          _recoveryActorId;
        [DataMember(Name = "recoveryOperationId")] private string          _recoveryOperationId;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult Operation           => _operation;
        public string          SessionId           => _sessionId;
        public string          Action              => _action;
        public string          RecoveryActorId     => _recoveryActorId;
        public string          RecoveryOperationId => _recoveryOperationId;

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionRecoveryResponse(OperationResult operation, string sessionId, string action,
            string recoveryActorId, string recoveryOperationId)
        {
            _operation           = operation;
            _sessionId           = sessionId;
            _action              = action;
            _recoveryActorId     = recoveryActorId;
            _recoveryOperationId = recoveryOperationId;
        }

        private AdminSessionRecoveryResponse() { }
    }
}
