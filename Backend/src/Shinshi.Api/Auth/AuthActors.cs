// System
using System;
using System.Security.Claims;

// Third Party
using Microsoft.AspNetCore.Http;

// Project
using Shinshi.Data.Auth;

namespace Shinshi.Backend
{
    /// <summary> 미들웨어가 검증한 DB 토큰의 주체만 요청 수명 동안 보존한다 </summary>
    public static class AuthActors
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string AUTHENTICATION_TYPE = "ShinshiBearer";
        private const string PLAYER_ROLE = "Player";
        private const string SERVER_ROLE = "Server";
        private static readonly object _playerKey = new object();
        private static readonly object _serverKey = new object();
        private static readonly object _playerKindKey = new object();

        /****************************************
        *                Methods
        ****************************************/

        public static PlayerAuthIdentity? GetPlayer(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Items.TryGetValue(_playerKey, out object? value) ? value as PlayerAuthIdentity : null;
        }

        /// <summary> 플레이어 bearer가 접근 토큰인지 게임 하위 세션인지 돌려준다. 플레이어 주체가 없으면 null이다 </summary>
        public static EPlayerTokenKind? GetPlayerTokenKind(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Items.TryGetValue(_playerKindKey, out object? value) ? value as EPlayerTokenKind? : null;
        }

        public static ServerAuthIdentity? GetServer(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Items.TryGetValue(_serverKey, out object? value) ? value as ServerAuthIdentity : null;
        }

        internal static void Clear(HttpContext context)
        {
            context.Items.Remove(_playerKey);
            context.Items.Remove(_serverKey);
            context.Items.Remove(_playerKindKey);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }

        internal static void SetPlayer(HttpContext context, PlayerAuthIdentity identity, EPlayerTokenKind kind)
        {
            context.Items[_playerKey] = identity;
            context.Items[_playerKindKey] = kind;
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, identity.AccountId.ToString("D")),
                new Claim(ClaimTypes.Role, PLAYER_ROLE)
            }, AUTHENTICATION_TYPE));
        }

        internal static void SetServer(HttpContext context, ServerAuthIdentity identity)
        {
            context.Items[_serverKey] = identity;
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, identity.ServiceId.ToString("D")),
                new Claim(ClaimTypes.Role, SERVER_ROLE)
            }, AUTHENTICATION_TYPE));
        }
    }
}
