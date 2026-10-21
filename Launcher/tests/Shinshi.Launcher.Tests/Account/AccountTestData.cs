// System
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;

// Project
using Shinshi.Contracts;
using Shinshi.Launcher.Account;

namespace Shinshi.Launcher.Tests.Account
{
    /// <summary> 계정 시험이 함께 쓰는 값과 생성 도우미 </summary>
    public static class AccountTestData
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ACCOUNT_ID     = "acc-0123456789";
        public const string LOGIN_ID       = "player_1";
        public const string PASSWORD       = "secret-password-1";
        public const string ACCESS_TOKEN   = "access-token-1";
        public const string REFRESH_TOKEN  = "refresh-token-1";
        public const string GAME_TOKEN     = "game-session-token-1";
        public const long   SERVER_TIME_MS = 1_800_000_000_000L;
        public const long   LONG_LIFE_MS   = 15 * 60 * 1000L;

        // REFRESH_MARGIN_MS(60초)보다 짧아 쓰기 전에 갱신이 필요한 수명
        public const long   SHORT_LIFE_MS  = 30 * 1000L;

        /****************************************
        *                Methods
        ****************************************/

        public static ClientBuildInfo Build() { return new ClientBuildInfo("build-1", 1, "hash-1"); }

        public static AuthExchangeResponse Exchange(string accessToken, string refreshToken, long accessLifeMs)
        {
            return new AuthExchangeResponse(ACCOUNT_ID, accessToken, SERVER_TIME_MS + accessLifeMs, refreshToken,
                SERVER_TIME_MS + LONG_LIFE_MS, SERVER_TIME_MS);
        }

        public static GameSessionIssueResponse GameSession()
        {
            return new GameSessionIssueResponse(ACCOUNT_ID, GAME_TOKEN, SERVER_TIME_MS + LONG_LIFE_MS, SERVER_TIME_MS);
        }

        public static AuthApiClient Client(FakeHttpHandler handler, TimeSpan? timeout = null)
        {
            return new AuthApiClient(new HttpClient(handler), new Uri(FakeHttpHandler.BASE_URI), timeout);
        }
    }

    /// <summary> 파일과 DPAPI 없이 메모리에 두는 갱신 토큰 보관소 </summary>
    public sealed class MemoryRefreshTokenStore : IRefreshTokenStore
    {
        /****************************************
        *                Fields
        ****************************************/

        private string? _token;
        private int     _saveCount;
        private string? _requestId;

        /****************************************
        *              Properties
        ****************************************/

        public bool    HasToken  => Volatile.Read(ref _token) != null;
        public string? Token     => Volatile.Read(ref _token);
        public int     SaveCount => Volatile.Read(ref _saveCount);
        public bool CanPrepareRefresh { get; set; } = true;

        /****************************************
        *                Methods
        ****************************************/

        public MemoryRefreshTokenStore(string? token = null)
        {
            _token = token;
        }

        public bool TryLoad([NotNullWhen(true)] out string? refreshToken)
        {
            refreshToken = Volatile.Read(ref _token);
            return refreshToken != null;
        }

        public bool TrySave(string refreshToken)
        {
            _requestId = null;
            Volatile.Write(ref _token, refreshToken);
            Interlocked.Increment(ref _saveCount);
            return true;
        }

        public void Clear() { Volatile.Write(ref _token, null); }

        public bool TryPrepareRefresh(string refreshToken, out string requestId)
        {
            if (!CanPrepareRefresh)
            {
                requestId = string.Empty;
                return false;
            }

            if ((_token != refreshToken) || (_requestId == null))
            {
                _token = refreshToken;
                _requestId = Guid.NewGuid().ToString("N");
            }

            requestId = _requestId;
            return true;
        }
    }
}
