// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 접속 거절 뒤 같은 토큰으로 다시 접속할지 정한다 </summary>
    public static class JoinRetryRules
    {
        // 같은 계정 접속은 거절한다(사용자 결정). 다만 방금 끊긴 자기 연결을 서버가 아직 모르는 경우(KCP Timeout 최대 10초)도
        // 같은 사유로 거절되므로, 그 시간을 넘길 만큼만 다시 시도한다. 그 뒤에도 거절되면 정말 다른 곳에서 접속 중인 것이다.
        // 다른 거절 사유는 다시 보내도 결과가 같거나 토큰을 새로 받아야 하므로 재시도하지 않는다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> retriesSoFar는 이번 접속 시도에서 이미 다시 접속한 횟수 </summary>
        public static bool ShouldRetry(ERejectReason reason, int retriesSoFar)
        {
            return (reason == ERejectReason.AlreadyConnected)
                && (retriesSoFar >= 0) && (retriesSoFar < Constants.Network.JOIN_ALREADY_CONNECTED_MAX_RETRIES);
        }
    }
}
