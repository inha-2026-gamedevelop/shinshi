// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Accounts;
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

        // 게임 하위 세션 계약. 구현하지 않는 테스트 대역은 모두 거절해 닫힌 쪽으로 실패한다
        Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateGameSessionAsync(string gameSessionToken,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AuthValidationOutcome<PlayerAuthIdentity>.Rejected(
                ContractCodes.RejectReason.TOKEN_REVOKED));

        // 부모 로그인이 이미 폐기, 만료됐으면 예외 대신 TOKEN_REVOKED로 돌려준다
        Task<AuthValidationOutcome<IssuedAuthToken>> IssueGameSessionForPlayerAsync(PlayerAuthIdentity identity,
            TimeSpan duration, CancellationToken cancellationToken = default)
            => Task.FromResult(AuthValidationOutcome<IssuedAuthToken>.Rejected(
                ContractCodes.RejectReason.TOKEN_REVOKED));

        // 제시한 하위 세션을 폐기하고 같은 부모로 새 하위 세션을 발급한다. 같은 토큰의 동시 회전은 하나만 성공한다
        Task<AuthValidationOutcome<IssuedAuthToken>> RotateGameSessionAsync(string gameSessionToken, TimeSpan duration,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AuthValidationOutcome<IssuedAuthToken>.Rejected(
                ContractCodes.RejectReason.TOKEN_REVOKED));

        // 하위 세션 행이 있으면(이미 폐기, 만료된 행 포함) true다. 반복 호출해도 같은 결과다
        Task<bool> RevokeGameSessionAsync(string gameSessionToken, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        // 소문자 아이디로 비밀번호 로그인 자격을 찾는다. 구현하지 않는 테스트 대역은 모든 아이디를 없는 아이디로 본다
        Task<AccountCredentialRecord?> FindCredentialByLoginIdAsync(string loginId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AccountCredentialRecord?>(null);

        // 검증한 해시가 그대로일 때만 시작 수단 password인 새 로그인 묶음을 발급한다. 그 사이 비밀번호가 바뀌었으면
        // INVALID_CREDENTIALS다. upgradedPasswordHash가 있으면 같은 트랜잭션에서 저장 해시를 새 회수의 해시로 바꾼다
        Task<AuthExchangeOutcome> IssuePasswordLoginAsync(Guid accountId, string verifiedPasswordHash,
            string? upgradedPasswordHash = null, CancellationToken cancellationToken = default)
            => Task.FromResult(AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.INVALID_CREDENTIALS));
    }
}
