// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Data.Accounts;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 로그인과 비밀번호 변경이 함께 쓰는 확인 순서: 쌍 차례 -> (아이디, IP) 제한 -> 자격 조회 -> 해시 자리 -> 고정 시간 비교 -> 기록.
    /// 없는 아이디도 현재 기본 회수의 더미 해시를 계산한다. 해시 계산은 DB 트랜잭션 밖에서 한다 </summary>
    public sealed class PasswordCredentialVerifier
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IAccountPasswordHasher _hasher;
        private readonly PasswordHashGate _gate;
        private readonly PasswordLoginLimiter _limiter;

        /****************************************
        *                Methods
        ****************************************/

        public PasswordCredentialVerifier(IAccountPasswordHasher hasher, PasswordHashGate gate,
            PasswordLoginLimiter limiter)
        {
            ArgumentNullException.ThrowIfNull(hasher);
            ArgumentNullException.ThrowIfNull(gate);
            ArgumentNullException.ThrowIfNull(limiter);
            _hasher  = hasher;
            _gate    = gate;
            _limiter = limiter;
        }

        /// <summary> loginId는 정규화한 소문자 아이디다. lookup은 그 아이디의 자격 행을 읽는다(없으면 null).
        /// 같은 쌍의 동시 시도는 차례대로 판정하므로 잠금 전에 여러 시도가 함께 해시를 통과하지 못한다 </summary>
        public async Task<PasswordCheckResult> CheckAsync(string loginId, string clientKey, string? password,
            Func<CancellationToken, Task<AccountCredentialRecord?>> lookup, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(lookup);
            using PasswordLoginLimiter.PasswordAttemptLease? turn = await _limiter.EnterAsync(loginId, clientKey,
                cancellationToken);
            if (turn == null)
            {
                return BusyResult();
            }

            EPasswordAttemptStatus attempt = turn.TryBeginAttempt(out TimeSpan retryAfter);
            if (attempt == EPasswordAttemptStatus.Locked)
            {
                return PasswordCheckResult.Rejected(EPasswordCheckStatus.Locked, retryAfter);
            }
            if (attempt == EPasswordAttemptStatus.Slowed)
            {
                return PasswordCheckResult.Rejected(EPasswordCheckStatus.Slowed, retryAfter);
            }

            AccountCredentialRecord? credential = await lookup(cancellationToken);
            using IDisposable? slot = await _gate.TryEnterAsync(cancellationToken);
            if (slot == null)
            {
                return BusyResult();
            }

            bool verified = credential == null
                ? _hasher.VerifyDummy(password)
                : _hasher.Verify(password, credential.PasswordHash);
            if (!verified || (credential == null))
            {
                turn.RecordFailure();
                return PasswordCheckResult.Rejected(EPasswordCheckStatus.InvalidCredentials, TimeSpan.Zero);
            }

            turn.RecordSuccess();

            // 기본 회수를 올린 뒤 성공한 로그인부터 새 회수로 다시 저장해 점차 맞춘다. 같은 해시 자리 안에서 계산한다
            string? upgraded = _hasher.NeedsRehash(credential.PasswordHash) ? _hasher.Hash(password!) : null;
            return PasswordCheckResult.Verified(credential, upgraded);
        }

        /// <summary> 새 비밀번호를 해시 자리 안에서 해시한다. 자리를 얻지 못하면 null이다 </summary>
        public async Task<string?> HashAsync(string password, CancellationToken cancellationToken)
        {
            using IDisposable? slot = await _gate.TryEnterAsync(cancellationToken);
            return slot == null ? null : _hasher.Hash(password);
        }

        private static PasswordCheckResult BusyResult()
        {
            return PasswordCheckResult.Rejected(EPasswordCheckStatus.Busy,
                TimeSpan.FromSeconds(PasswordHashGate.RETRY_AFTER_SECONDS));
        }
    }
}
