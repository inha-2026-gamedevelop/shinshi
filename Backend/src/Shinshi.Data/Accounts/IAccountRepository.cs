// System
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Data.Accounts
{
    /// <summary> 가입, 계정 정보, 자격 변경 저장소. 비밀번호 해시 계산은 호출자(API)가 트랜잭션 밖에서 끝낸 값만 받는다 </summary>
    public interface IAccountRepository
    {
        // 계정과 자격 행을 한 트랜잭션에 만든다. 같은 아이디가 있으면 계정 행도 남기지 않고 LOGIN_ID_TAKEN이다
        Task<AccountSignupOutcome> CreateAccountAsync(string loginId, string passwordHash, string email, string emailKey,
            CancellationToken cancellationToken = default);

        Task<bool> IsLoginIdTakenAsync(string loginId, CancellationToken cancellationToken = default);

        Task<AccountProfileRecord?> GetProfileAsync(Guid accountId, CancellationToken cancellationToken = default);

        Task<AccountCredentialRecord?> FindCredentialByAccountAsync(Guid accountId,
            CancellationToken cancellationToken = default);

        // 검증한 해시가 그대로일 때만 바꾸고 같은 트랜잭션에서 계정의 모든 로그인 묶음, 게임 세션, 입장 토큰, 초대를 폐기한다
        Task<ECredentialUpdateStatus> ChangePasswordAsync(Guid accountId, string expectedPasswordHash,
            string newPasswordHash, CancellationToken cancellationToken = default);

        // 현재 비밀번호 없는 복구. familyId 묶음이 초대로 시작했고 30분 안이며 살아 있을 때만(아니면 RecoveryNotAllowed) 바꾼다.
        // 자격 행이 있으면 비밀번호와(주면) 이메일을 바꾸며 loginId는 null이어야 한다. 없으면 loginId, 이메일, 비밀번호로 만든다.
        // 아이디가 쓰이면 LoginIdTaken이다. 폐기 범위는 위와 같다
        Task<ECredentialUpdateStatus> RecoverCredentialsAsync(Guid accountId, Guid familyId, string? loginId,
            string passwordHash, string? email, string? emailKey, CancellationToken cancellationToken = default);
    }
}
