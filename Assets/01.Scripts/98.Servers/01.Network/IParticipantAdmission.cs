// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 명단 결합 뒤 역할별 입장 조건을 한 번 더 본다, 인증기가 메인 스레드에서 부른다 </summary>
    public interface IParticipantAdmission
    {
        // None이 아니면 인증기가 결합을 풀고 그 사유로 거절한다. 던전은 출정 일치, 시작 뒤 새 참가자 거절을 여기서 막는다
        ERejectReason Admit(in JoinTokenClaims claims, SessionBinding binding);
    }
}
