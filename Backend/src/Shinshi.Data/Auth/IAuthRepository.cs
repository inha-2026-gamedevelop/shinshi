// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Auth
{
    /// <summary> API가 토큰 저장 결과에서만 호출 주체를 만들도록 하는 인증 저장소 계약 </summary>
    public interface IAuthRepository
    {
        Task<AuthExchangeOutcome> ExchangeInviteAsync(string inviteCode, CancellationToken cancellationToken = default);
        Task<AuthExchangeOutcome> RotateRefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
        Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateAccessAsync(string accessToken,
            CancellationToken cancellationToken = default);
        Task<AuthValidationOutcome<ServerAuthIdentity>> ValidateServerLeaseAsync(string serverLeaseToken,
            CancellationToken cancellationToken = default);
        Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeJoinTokenAsync(string joinToken, Guid instanceId,
            int serverRole, Guid serverLeaseId, CancellationToken cancellationToken = default);
        Task<AuthValidationOutcome<IssuedAuthToken>> IssueDungeonJoinTokenAsync(ServerAuthIdentity server,
            DungeonJoinGrant grant, CancellationToken cancellationToken = default);

        // 이 서비스의 키로 만든 토큰(태그 일치)인지 DB 없이 본다. 만료, 폐기된 자기 토큰은 정상 클라이언트의 흔한 상태라
        // 인증 실패 한도에 세지 않을 때 쓴다. 판단할 수 없는 구현은 false(남의 토큰)로 둔다
        bool IsAuthenticToken(string token) => false;
    }
}
