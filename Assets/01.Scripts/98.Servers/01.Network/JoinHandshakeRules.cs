// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 인증 요청의 사전 검사, 프로토콜과 콘텐츠 해시와 토큰 크기를 토큰 검증 전에 본다 </summary>
    public static class JoinHandshakeRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 통과하면 None, 아니면 클라이언트에 보낼 거절 사유 </summary>
        public static ERejectReason Check(int protocolVersion, string contentHash, string joinToken, int expectedProtocolVersion,
            string expectedContentHash)
        {
            if (protocolVersion != expectedProtocolVersion)
            {
                return ERejectReason.VersionMismatch;
            }

            // 서버 해시가 없으면 비교할 기준이 없으므로 받지 않는다
            if ((string.IsNullOrEmpty(expectedContentHash)) || (contentHash == null)
                || (contentHash.Length != Constants.Network.CONTENT_HASH_LENGTH)
                || (!string.Equals(contentHash, expectedContentHash, StringComparison.Ordinal)))
            {
                return ERejectReason.VersionMismatch;
            }

            // 비었거나 지나치게 긴 토큰은 검증기에 넘기지 않는다. 백엔드 호출 비용을 공격자가 키우지 못하게 한다
            if ((string.IsNullOrEmpty(joinToken)) || (joinToken.Length > Constants.Network.MAX_JOIN_TOKEN_LENGTH))
            {
                return ERejectReason.TokenRevoked;
            }

            return ERejectReason.None;
        }
    }
}
