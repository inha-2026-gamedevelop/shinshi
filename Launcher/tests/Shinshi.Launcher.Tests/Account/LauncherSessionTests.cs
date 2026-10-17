// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Launcher.Account;

namespace Shinshi.Launcher.Tests.Account
{
    /// <summary> 로그인, 자동 로그인, 갱신 직렬화, 응답 유실 처리, 게임 실행 중 갱신 금지 </summary>
    public sealed class LauncherSessionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ROTATED_ACCESS  = "access-token-2";
        private const string ROTATED_REFRESH = "refresh-token-2";
        private const int    WAIT_MS         = 5000;
        private const int    SETTLE_MS       = 100;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task LoginWithCredentialsAsync_Remember_StoresOnlyRefreshTokenAndLowercasesId()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN,
                AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult result = await session.LoginWithCredentialsAsync("  Player_1 ", AccountTestData.PASSWORD, true,
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(ELauncherSessionState.LoggedIn, session.State);
            Assert.Equal(AccountTestData.ACCOUNT_ID, session.AccountId);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);
            Assert.Equal(AccountTestData.LOGIN_ID, Assert.Single(handler.Requests).ReadBody<AuthExchangeRequest>()!.LoginId);
        }

        [Fact]
        public async Task LoginWithCredentialsAsync_NotRemembered_ClearsStore()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN,
                AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore("old-refresh");
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult result = await session.LoginWithCredentialsAsync(AccountTestData.LOGIN_ID, AccountTestData.PASSWORD, false,
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Null(store.Token);
            Assert.Equal(0, store.SaveCount);
        }

        [Theory]
        [InlineData("ab", AccountTestData.PASSWORD)]
        [InlineData("player-1", AccountTestData.PASSWORD)]
        [InlineData("player_name_too_long", AccountTestData.PASSWORD)]
        [InlineData(AccountTestData.LOGIN_ID, "")]
        public async Task LoginWithCredentialsAsync_InvalidInput_DoesNotCallApi(string loginId, string password)
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => throw new InvalidOperationException("must not be called"));
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), new MemoryRefreshTokenStore());

            SessionResult result = await session.LoginWithCredentialsAsync(loginId, password, true,
                CancellationToken.None);

            Assert.Equal(ESessionError.InvalidInput, result.Error);
            Assert.Empty(handler.Requests);
            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
        }

        [Fact]
        public async Task LoginWithCredentialsAsync_InvalidCredentials_StaysLoggedOutWithReason()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Reject(HttpStatusCode.Unauthorized,
                AuthRejectCodes.INVALID_CREDENTIALS)));
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), new MemoryRefreshTokenStore());

            SessionResult result = await session.LoginWithCredentialsAsync(AccountTestData.LOGIN_ID, AccountTestData.PASSWORD, true,
                CancellationToken.None);

            Assert.Equal(ESessionError.ApiFailed, result.Error);
            Assert.Equal(EAuthApiError.Unauthorized, result.ApiError);
            Assert.Equal(AuthRejectCodes.INVALID_CREDENTIALS, result.ReasonCode);
            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
        }

        [Fact]
        public async Task EnsureAccessTokenAsync_ConcurrentCalls_SendOneRefresh()
        {
            TaskCompletionSource release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int refreshCalls = 0;
            FakeHttpHandler handler = new FakeHttpHandler(async request =>
            {
                AuthExchangeRequest body = request.ReadBody<AuthExchangeRequest>()!;
                if (body.RefreshToken == null)
                {
                    return ExchangeOk(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN, AccountTestData.SHORT_LIFE_MS);
                }

                Interlocked.Increment(ref refreshCalls);
                await release.Task;
                return ExchangeOk(ROTATED_ACCESS, ROTATED_REFRESH, AccountTestData.LONG_LIFE_MS);
            });
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            using LauncherSession session = await LoggedInAsync(handler, store);

            Task<SessionResult> first  = session.EnsureAccessTokenAsync(CancellationToken.None);
            Task<SessionResult> second = session.EnsureAccessTokenAsync(CancellationToken.None);
            await Task.Delay(SETTLE_MS);
            release.SetResult();
            SessionResult[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.All(results, result => Assert.True(result.IsSuccess));
            Assert.Equal(1, refreshCalls);
            Assert.Equal(ROTATED_REFRESH, store.Token);
        }

        [Fact]
        public async Task EnsureAccessTokenAsync_RefreshResponseLost_KeepsStoreAndDoesNotResendInSameRun()
        {
            int refreshCalls = 0;
            FakeHttpHandler handler = new FakeHttpHandler(request =>
            {
                if (request.ReadBody<AuthExchangeRequest>()!.RefreshToken == null)
                {
                    return Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN,
                        AccountTestData.SHORT_LIFE_MS));
                }

                Interlocked.Increment(ref refreshCalls);
                throw new HttpRequestException(HttpRequestError.ResponseEnded, "response lost");
            });
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            using LauncherSession session = await LoggedInAsync(handler, store);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);

            SessionResult result = await session.EnsureAccessTokenAsync(CancellationToken.None);
            SessionResult again  = await session.EnsureAccessTokenAsync(CancellationToken.None);

            Assert.Equal(ESessionError.ApiFailed, result.Error);
            Assert.Equal(EAuthApiError.ResponseLost, result.ApiError);
            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
            Assert.Null(session.AccountId);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);
            Assert.True(session.HasStoredLogin);
            Assert.Equal(ESessionError.NotLoggedIn, again.Error);
            Assert.Equal(1, refreshCalls);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.GatewayTimeout)]
        public async Task TryAutoLoginAsync_GatewayErrorDuringDeploy_KeepsStoredTokenAndNextStartLogsIn(HttpStatusCode status)
        {
            int calls = 0;
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(Interlocked.Increment(ref calls) == 1
                ? new HttpResponseMessage(status)
                : ExchangeOk(ROTATED_ACCESS, ROTATED_REFRESH, AccountTestData.LONG_LIFE_MS)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN);
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult first  = await session.TryAutoLoginAsync(CancellationToken.None);
            SessionResult second = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.Equal(ESessionError.ApiFailed, first.Error);
            Assert.Equal(EAuthApiError.ResponseLost, first.ApiError);
            Assert.True(second.IsSuccess);
            Assert.Equal(ELauncherSessionState.LoggedIn, session.State);
            Assert.Equal(ROTATED_REFRESH, store.Token);
            Assert.All(handler.Requests,
                request => Assert.Equal(AccountTestData.REFRESH_TOKEN, request.ReadBody<AuthExchangeRequest>()!.RefreshToken));
        }

        [Fact]
        public async Task TryAutoLoginAsync_ResponseLostThenServerReportsReuse_ClearsStore()
        {
            int calls = 0;
            FakeHttpHandler handler = new FakeHttpHandler(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new HttpRequestException(HttpRequestError.ResponseEnded, "response lost");
                }

                return Task.FromResult(FakeHttpHandler.Reject(HttpStatusCode.Unauthorized, ContractCodes.RejectReason.TOKEN_REVOKED));
            });
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN);
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult first  = await session.TryAutoLoginAsync(CancellationToken.None);
            SessionResult second = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.Equal(ESessionError.ApiFailed, first.Error);
            Assert.Equal(ESessionError.SessionLost, second.Error);
            Assert.Null(store.Token);
            Assert.False(session.HasStoredLogin);
            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
        }

        [Fact]
        public async Task TryAutoLoginAsync_TokenExpired_ClearsStore()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Reject(HttpStatusCode.Unauthorized,
                ContractCodes.RejectReason.TOKEN_EXPIRED)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN);
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult result = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.Equal(ESessionError.SessionLost, result.Error);
            Assert.Null(store.Token);
        }

        [Fact]
        public async Task TryAutoLoginAsync_Maintenance_KeepsStoredToken()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Reject(HttpStatusCode.Forbidden,
                ContractCodes.RejectReason.MAINTENANCE)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN);
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult result = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.Equal(ESessionError.ApiFailed, result.Error);
            Assert.Equal(EAuthApiError.Maintenance, result.ApiError);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);
        }

        [Fact]
        public async Task EnsureAccessTokenAsync_RefreshUnreachable_KeepsLogin()
        {
            FakeHttpHandler handler = new FakeHttpHandler(request =>
            {
                if (request.ReadBody<AuthExchangeRequest>()!.RefreshToken == null)
                {
                    return Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN,
                        AccountTestData.SHORT_LIFE_MS));
                }

                throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
            });
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            using LauncherSession session = await LoggedInAsync(handler, store);

            SessionResult result = await session.EnsureAccessTokenAsync(CancellationToken.None);

            Assert.Equal(ESessionError.ApiFailed, result.Error);
            Assert.Equal(EAuthApiError.Unreachable, result.ApiError);
            Assert.Equal(ELauncherSessionState.LoggedIn, session.State);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);
        }

        [Fact]
        public async Task EnsureAccessTokenAsync_LongLifetime_DoesNotRefresh()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN,
                AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS)));
            using LauncherSession session = await LoggedInAsync(handler, new MemoryRefreshTokenStore());

            SessionResult result = await session.EnsureAccessTokenAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task EnsureAccessTokenAsync_RefreshSuspended_DoesNotSendRefresh()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN,
                AccountTestData.REFRESH_TOKEN, AccountTestData.SHORT_LIFE_MS)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            using LauncherSession session = await LoggedInAsync(handler, store);

            session.IsRefreshSuspended = true;
            SessionResult result = await session.EnsureAccessTokenAsync(CancellationToken.None);
            SessionResult<GameSessionIssueResponse> issued = await session.IssueGameSessionAsync(AccountTestData.Build(),
                CancellationToken.None);

            Assert.Equal(ESessionError.RefreshSuspended, result.Error);
            Assert.Equal(ESessionError.RefreshSuspended, issued.Error);
            Assert.Single(handler.Requests);
            Assert.Equal(ELauncherSessionState.LoggedIn, session.State);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);
        }

        [Fact]
        public async Task TryAutoLoginAsync_RefreshSuspended_DoesNotSendStoredToken()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => throw new InvalidOperationException("must not be called"));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN);
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store) { IsRefreshSuspended = true };

            SessionResult result = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.Equal(ESessionError.RefreshSuspended, result.Error);
            Assert.Empty(handler.Requests);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);
        }

        [Fact]
        public async Task TryAutoLoginAsync_StoredToken_RotatesAndSavesNewToken()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(ExchangeOk(ROTATED_ACCESS, ROTATED_REFRESH,
                AccountTestData.LONG_LIFE_MS)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN);
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult result = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(ELauncherSessionState.LoggedIn, session.State);
            Assert.Equal(ROTATED_REFRESH, store.Token);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, Assert.Single(handler.Requests).ReadBody<AuthExchangeRequest>()!.RefreshToken);
        }

        [Fact]
        public async Task TryAutoLoginAsync_NoStoredToken_DoesNotCallApi()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => throw new InvalidOperationException("must not be called"));
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), new MemoryRefreshTokenStore());

            SessionResult result = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.Equal(ESessionError.NoStoredLogin, result.Error);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task TryAutoLoginAsync_Unreachable_KeepsStoredToken()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN);
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult result = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.Equal(ESessionError.ApiFailed, result.Error);
            Assert.Equal(EAuthApiError.Unreachable, result.ApiError);
            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);
        }

        [Fact]
        public async Task TryAutoLoginAsync_TokenRevoked_ClearsStore()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(FakeHttpHandler.Reject(HttpStatusCode.Unauthorized,
                ContractCodes.RejectReason.TOKEN_REVOKED)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN);
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult result = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.Equal(ESessionError.SessionLost, result.Error);
            Assert.Null(store.Token);
            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
        }

        [Fact]
        public async Task IssueGameSessionAsync_AccessRejected_RefreshesOnceAndRetries()
        {
            int issueCalls = 0;
            FakeHttpHandler handler = new FakeHttpHandler(request =>
            {
                if (request.Path == ApiRoutes.AUTH_EXCHANGE)
                {
                    bool isRefresh = request.ReadBody<AuthExchangeRequest>()!.RefreshToken != null;
                    return Task.FromResult(isRefresh
                        ? ExchangeOk(ROTATED_ACCESS, ROTATED_REFRESH, AccountTestData.LONG_LIFE_MS)
                        : ExchangeOk(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS));
                }

                ++issueCalls;
                return Task.FromResult((request.Authorization == ApiRoutes.BEARER_PREFIX + ROTATED_ACCESS)
                    ? FakeHttpHandler.Json(HttpStatusCode.OK, AccountTestData.GameSession())
                    : FakeHttpHandler.Reject(HttpStatusCode.Unauthorized, ContractCodes.RejectReason.TOKEN_EXPIRED));
            });
            using LauncherSession session = await LoggedInAsync(handler, new MemoryRefreshTokenStore());

            SessionResult<GameSessionIssueResponse> result = await session.IssueGameSessionAsync(AccountTestData.Build(),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(AccountTestData.GAME_TOKEN, result.Value!.GameSessionToken);
            Assert.Equal(2, issueCalls);
            Assert.Equal(2, handler.Count(ApiRoutes.AUTH_EXCHANGE));
        }

        [Fact]
        public async Task LoginAndRefresh_SendNoBuild_OnlyGameSessionIssueSendsBuild()
        {
            FakeHttpHandler handler = new FakeHttpHandler(request =>
            {
                if (request.Path == ApiRoutes.AUTH_EXCHANGE)
                {
                    return Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN,
                        AccountTestData.SHORT_LIFE_MS));
                }

                return Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, AccountTestData.GameSession()));
            });
            using LauncherSession session = await LoggedInAsync(handler, new MemoryRefreshTokenStore());

            // 접근 토큰이 짧아 게임 세션 발급 전에 갱신이 한 번 나간다
            SessionResult<GameSessionIssueResponse> issued = await session.IssueGameSessionAsync(AccountTestData.Build(),
                CancellationToken.None);

            Assert.True(issued.IsSuccess);
            IReadOnlyList<RecordedRequest> exchanges = handler.Requests.Where(request => request.Path == ApiRoutes.AUTH_EXCHANGE).ToArray();
            Assert.Equal(2, exchanges.Count);
            Assert.All(exchanges, request => Assert.Null(request.ReadBody<AuthExchangeRequest>()!.Build));
            Assert.Equal(AccountTestData.REFRESH_TOKEN, exchanges[1].ReadBody<AuthExchangeRequest>()!.RefreshToken);
            RecordedRequest game = Assert.Single(handler.Requests, request => request.Path == ApiRoutes.AUTH_GAME_SESSION);
            Assert.Equal("build-1", game.ReadBody<GameSessionIssueRequest>()!.Build.BuildId);
        }

        [Fact]
        public async Task TryAutoLoginAsync_SendsNoBuild()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(ExchangeOk(ROTATED_ACCESS, ROTATED_REFRESH,
                AccountTestData.LONG_LIFE_MS)));
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler),
                new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN));

            SessionResult result = await session.TryAutoLoginAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Null(Assert.Single(handler.Requests).ReadBody<AuthExchangeRequest>()!.Build);
        }

        [Fact]
        public async Task IssueGameSessionAsync_VersionMismatch_KeepsLogin()
        {
            FakeHttpHandler handler = new FakeHttpHandler(request => Task.FromResult(request.Path == ApiRoutes.AUTH_EXCHANGE
                ? ExchangeOk(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS)
                : FakeHttpHandler.Reject(HttpStatusCode.Forbidden, ContractCodes.RejectReason.VERSION_MISMATCH)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            using LauncherSession session = await LoggedInAsync(handler, store);

            SessionResult<GameSessionIssueResponse> result = await session.IssueGameSessionAsync(AccountTestData.Build(),
                CancellationToken.None);

            Assert.Equal(ESessionError.ApiFailed, result.Error);
            Assert.Equal(EAuthApiError.VersionMismatch, result.ApiError);
            Assert.Equal(ELauncherSessionState.LoggedIn, session.State);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);
        }

        [Fact]
        public async Task LogoutAsync_SendsRefreshTokenToServerBeforeClearing()
        {
            string? tokenAtServer = null;
            string? storeAtServer = "unset";
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            FakeHttpHandler handler = new FakeHttpHandler(request =>
            {
                if (request.Path == ApiRoutes.AUTH_LOGOUT)
                {
                    tokenAtServer = request.ReadBody<AuthLogoutRequest>()!.RefreshToken;
                    storeAtServer = store.Token;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                }

                return Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS));
            });
            using LauncherSession session = await LoggedInAsync(handler, store);

            SessionResult result = await session.LogoutAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, tokenAtServer);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, storeAtServer);
            Assert.Null(store.Token);
            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
            Assert.Null(handler.Requests.Single(request => request.Path == ApiRoutes.AUTH_LOGOUT).Authorization);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        public async Task LogoutAsync_ServerRejects_StillClearsLocalLogin(HttpStatusCode status)
        {
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            FakeHttpHandler handler = new FakeHttpHandler(request => Task.FromResult(request.Path == ApiRoutes.AUTH_LOGOUT
                ? new HttpResponseMessage(status)
                : ExchangeOk(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS)));
            using LauncherSession session = await LoggedInAsync(handler, store);

            SessionResult result = await session.LogoutAsync();

            Assert.Equal(ESessionError.ApiFailed, result.Error);
            Assert.Null(store.Token);
            Assert.Null(session.AccountId);
            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
        }

        [Fact]
        public async Task LogoutAsync_ServerUnreachable_StillClearsLocalLogin()
        {
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            FakeHttpHandler handler = new FakeHttpHandler(request => request.Path == ApiRoutes.AUTH_LOGOUT
                ? throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")
                : Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN, AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS)));
            using LauncherSession session = await LoggedInAsync(handler, store);

            SessionResult result = await session.LogoutAsync();

            Assert.Equal(EAuthApiError.Unreachable, result.ApiError);
            Assert.Null(store.Token);
            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
        }

        [Fact]
        public async Task LogoutAsync_NotLoggedInWithStoredToken_RevokesStoredTokenOnServer()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore(AccountTestData.REFRESH_TOKEN);
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);

            SessionResult result = await session.LogoutAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, Assert.Single(handler.Requests).ReadBody<AuthLogoutRequest>()!.RefreshToken);
            Assert.Null(store.Token);
        }

        [Fact]
        public async Task LogoutAsync_NoToken_DoesNotCallServer()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => throw new InvalidOperationException("must not be called"));
            using LauncherSession session = new LauncherSession(AccountTestData.Client(handler), new MemoryRefreshTokenStore());

            SessionResult result = await session.LogoutAsync();

            Assert.True(result.IsSuccess);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task LogoutAsync_GameRunning_DoesNothing()
        {
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN,
                AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS)));
            using LauncherSession session = await LoggedInAsync(handler, store);
            session.IsRefreshSuspended = true;

            SessionResult result = await session.LogoutAsync();

            Assert.Equal(ESessionError.RefreshSuspended, result.Error);
            Assert.Equal(ELauncherSessionState.LoggedIn, session.State);
            Assert.Equal(AccountTestData.REFRESH_TOKEN, store.Token);
            Assert.Equal(0, handler.Count(ApiRoutes.AUTH_LOGOUT));
        }

        [Fact]
        public async Task LogoutAsync_ClearsMemoryAndStore()
        {
            FakeHttpHandler handler = new FakeHttpHandler(_ => Task.FromResult(ExchangeOk(AccountTestData.ACCESS_TOKEN,
                AccountTestData.REFRESH_TOKEN, AccountTestData.LONG_LIFE_MS)));
            MemoryRefreshTokenStore store = new MemoryRefreshTokenStore();
            using LauncherSession session = await LoggedInAsync(handler, store);
            int changes = 0;
            session.StateChanged += () => Interlocked.Increment(ref changes);

            await session.LogoutAsync();

            Assert.Equal(ELauncherSessionState.LoggedOut, session.State);
            Assert.Null(session.AccountId);
            Assert.Null(store.Token);
            Assert.Equal(1, changes);
        }

        private static async Task<LauncherSession> LoggedInAsync(FakeHttpHandler handler, MemoryRefreshTokenStore store)
        {
            LauncherSession session = new LauncherSession(AccountTestData.Client(handler), store);
            SessionResult login = await session.LoginWithCredentialsAsync(AccountTestData.LOGIN_ID, AccountTestData.PASSWORD, true,
                CancellationToken.None);
            Assert.True(login.IsSuccess);
            return session;
        }

        private static HttpResponseMessage ExchangeOk(string accessToken, string refreshToken, long accessLifeMs)
        {
            return FakeHttpHandler.Json(HttpStatusCode.OK, AccountTestData.Exchange(accessToken, refreshToken, accessLifeMs));
        }
    }
}
