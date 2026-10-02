// System
using System;
using System.Net;

// Project
using Shinshi.Data.Auth;

namespace Shinshi.Data.Admin
{
    /// <summary> 어드민 접근 토큰과 요청 IP의 HMAC. 게임 토큰과 같은 키를 쓰지만 용도 문자열이 달라
    /// 게임 접근 토큰을 어드민 해시로 찾을 수 없고 그 반대도 마찬가지다 </summary>
    public sealed class AdminTokenHasher
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ACCESS_DOMAIN     = "shinshi.admin.access.v1";
        public const string REQUEST_IP_DOMAIN = "shinshi.admin.request-ip.v1";

        private readonly AuthTokenHasher _hasher;

        /****************************************
        *                Methods
        ****************************************/

        public AdminTokenHasher(AuthTokenOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _hasher = new AuthTokenHasher(options);
        }

        public string CreateToken() { return _hasher.CreateToken(); }

        /// <summary> 우리 키로 만든 형식인지 DB 없이 확인한다. 게임 토큰도 통과하지만 어드민 해시 조회에서 걸러진다 </summary>
        public bool IsAuthentic(string? token) { return _hasher.IsAuthentic(token); }

        public byte[] HashAccessToken(string token)
        {
            ArgumentNullException.ThrowIfNull(token);
            return _hasher.Hash(ACCESS_DOMAIN, token);
        }

        /// <summary> 감사 로그에 원래 IP 대신 남기는 값. 키 없이는 IP 후보를 대조할 수 없다 </summary>
        public byte[]? HashRequestIp(IPAddress? address)
        {
            if (address == null)
            {
                return null;
            }

            IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            return _hasher.Hash(REQUEST_IP_DOMAIN, normalized.ToString());
        }
    }
}
