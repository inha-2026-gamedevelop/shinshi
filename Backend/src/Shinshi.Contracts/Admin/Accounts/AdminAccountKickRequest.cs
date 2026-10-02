// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정의 모든 로그인 묶음과 토큰을 폐기한다(Operator). revokeInvites가 true(생략 시 기본)면 아직 폐기하지 않은
    /// 초대도 같은 트랜잭션에서 폐기해 남은 사용 횟수로 교환하지 못하게 한다. 현재 접속은 유지한다(게임 서버가 쥔 접속 임대는
    /// 건드리지 않는다). 비밀번호 로그인은 막지 않아 같은 아이디와 비밀번호로 새 토큰을 받을 수 있다. 로그인 차단은 제재(P1)다 </summary>
    [DataContract]
    public sealed class AdminAccountKickRequest : IAdminWriteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]   private string _operationId;
        [DataMember(Name = "reason")]        private string _reason;
        [DataMember(Name = "revokeInvites")] private bool?  _revokeInvites;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId    => _operationId;
        public string Reason         => _reason;

        // 본문에 없으면 null이다. 기본값(true) 적용은 RevokesInvites로 읽는다
        public bool?  RevokeInvites  => _revokeInvites;
        public bool   RevokesInvites => _revokeInvites ?? true;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountKickRequest(string operationId, string reason)
            : this(operationId, reason, null)
        {
        }

        public AdminAccountKickRequest(string operationId, string reason, bool? revokeInvites)
        {
            _operationId   = operationId;
            _reason        = reason;
            _revokeInvites = revokeInvites;
        }

        private AdminAccountKickRequest() { }
    }
}
