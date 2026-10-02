// System
using System;

// Project
using Shinshi.Data.Accounts;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 비밀번호 확인 결과와, 확인에 성공했으면 비교한 자격 행. 거절이면 다시 보낼 때까지의 시간을 함께 준다 </summary>
    public sealed class PasswordCheckResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public EPasswordCheckStatus Status { get; }
        public AccountCredentialRecord? Credential { get; }
        public TimeSpan RetryAfter { get; }

        // 저장 해시의 회수가 현재 기본보다 낮아 같은 비밀번호를 새 회수로 다시 해시한 값. 그대로면 null이다
        public string? UpgradedPasswordHash { get; }

        /****************************************
        *                Methods
        ****************************************/

        private PasswordCheckResult(EPasswordCheckStatus status, AccountCredentialRecord? credential, TimeSpan retryAfter,
            string? upgradedPasswordHash)
        {
            Status               = status;
            Credential           = credential;
            RetryAfter           = retryAfter;
            UpgradedPasswordHash = upgradedPasswordHash;
        }

        public static PasswordCheckResult Verified(AccountCredentialRecord credential, string? upgradedPasswordHash)
        {
            ArgumentNullException.ThrowIfNull(credential);
            return new PasswordCheckResult(EPasswordCheckStatus.Verified, credential, TimeSpan.Zero, upgradedPasswordHash);
        }

        public static PasswordCheckResult Rejected(EPasswordCheckStatus status, TimeSpan retryAfter)
        {
            if (status == EPasswordCheckStatus.Verified)
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            return new PasswordCheckResult(status, null, retryAfter, null);
        }
    }
}
