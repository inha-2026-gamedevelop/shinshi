// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 확정된 강제 종료. 이미 발급된 토큰(로그인 묶음, 접근, 갱신, 게임 하위 세션, 미소비 입장 토큰)과
    /// invitesRevoked면 미폐기 초대를 폐기하고 현재 접속은 유지한다. 비밀번호 로그인은 막지 않으므로 같은 아이디와 비밀번호로
    /// 새 토큰을 받을 수 있다. 로그인 차단은 제재(P1)다. 게임 서버가 쥔 계정 임대를 만료시키지 않는 이유는 임대 갱신 실패 때
    /// 서버가 플레이어를 끊는다는 계약이 아직 없어, 임대만 풀면 끊기지 않은 기존 접속과 새 로그인이 동시에 살 수 있기 때문이다.
    /// 같은 operationId 재전송도 같은 응답이다(요청 값으로 만든다) </summary>
    [DataContract]
    public sealed class AdminAccountKickResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operation")]          private OperationResult _operation;
        [DataMember(Name = "accountId")]          private string          _accountId;
        [DataMember(Name = "invitesRevoked")]     private bool            _invitesRevoked;
        [DataMember(Name = "currentSessionKept")] private bool            _currentSessionKept;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult Operation          => _operation;
        public string          AccountId          => _accountId;
        public bool            InvitesRevoked     => _invitesRevoked;
        public bool            CurrentSessionKept => _currentSessionKept;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountKickResponse(OperationResult operation, string accountId, bool invitesRevoked,
            bool currentSessionKept)
        {
            _operation          = operation;
            _accountId          = accountId;
            _invitesRevoked     = invitesRevoked;
            _currentSessionKept = currentSessionKept;
        }

        private AdminAccountKickResponse() { }
    }
}
