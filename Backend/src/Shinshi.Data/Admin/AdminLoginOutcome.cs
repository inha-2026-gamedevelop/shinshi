// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 로그인 결과. 실패는 원인을 담지 않아 호출자가 원인별 응답을 만들 수 없게 한다 </summary>
    public sealed class AdminLoginOutcome
    {
        /****************************************
        *                Fields
        ****************************************/

        public static readonly AdminLoginOutcome Failed = new AdminLoginOutcome(false, string.Empty, default, null);

        /****************************************
        *              Properties
        ****************************************/

        public bool IsSuccess { get; }

        // 응답으로 한 번만 주는 원문. DB에는 해시만 있다
        public string AccessToken { get; }
        public DateTimeOffset ExpiresAt { get; }
        public AdminIdentity? Identity { get; }

        /****************************************
        *                Methods
        ****************************************/

        private AdminLoginOutcome(bool isSuccess, string accessToken, DateTimeOffset expiresAt, AdminIdentity? identity)
        {
            IsSuccess   = isSuccess;
            AccessToken = accessToken;
            ExpiresAt   = expiresAt;
            Identity    = identity;
        }

        public static AdminLoginOutcome Succeeded(string accessToken, DateTimeOffset expiresAt, AdminIdentity identity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
            ArgumentNullException.ThrowIfNull(identity);
            return new AdminLoginOutcome(true, accessToken, expiresAt, identity);
        }
    }
}
