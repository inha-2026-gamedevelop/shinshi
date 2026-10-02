// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 접속 토큰 검증 결과를 받는 쪽, 서버 인증기가 구현한다 </summary>
    public interface IJoinTokenVerifyListener
    {
        // reason이 None이 아니면 claims는 쓰지 않는다
        void OnJoinTokenVerified(int requestId, ERejectReason reason, in JoinTokenClaims claims);
    }
}
