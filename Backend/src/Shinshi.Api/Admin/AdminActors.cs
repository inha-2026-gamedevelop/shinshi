// System
using System;

// Third Party
using Microsoft.AspNetCore.Http;

// Project
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 어드민 미들웨어가 확인한 운영자와 요청 IP 해시를 요청 수명 동안 보존한다. 게임 주체(AuthActors)와 저장 키가 다르다 </summary>
    public static class AdminActors
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly object _identityKey      = new object();
        private static readonly object _requestIpHashKey = new object();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> RequireAdminRole 엔드포인트에서만 부른다. 확인된 운영자가 없으면 설정 오류이므로 예외다 </summary>
        public static AdminIdentity Get(HttpContext context)
        {
            return TryGet(context)
                ?? throw new InvalidOperationException("Admin identity is missing. Mark the endpoint with RequireAdminRole.");
        }

        public static AdminIdentity? TryGet(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Items.TryGetValue(_identityKey, out object? value) ? value as AdminIdentity : null;
        }

        /// <summary> 감사 로그에 남길 요청 IP의 HMAC. 로그인처럼 운영자가 아직 없는 어드민 엔드포인트에도 있다 </summary>
        public static byte[]? GetRequestIpHash(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Items.TryGetValue(_requestIpHashKey, out object? value) ? value as byte[] : null;
        }

        internal static void Clear(HttpContext context)
        {
            context.Items.Remove(_identityKey);
            context.Items.Remove(_requestIpHashKey);
        }

        internal static void SetRequestIpHash(HttpContext context, byte[]? requestIpHash)
        {
            context.Items[_requestIpHashKey] = requestIpHash;
        }

        internal static void SetIdentity(HttpContext context, AdminIdentity identity)
        {
            context.Items[_identityKey] = identity;
        }
    }
}
