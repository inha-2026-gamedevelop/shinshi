// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;

namespace Shinshi.Launcher.Account
{
    /// <summary> 런처 계정 상태. 로그인, 자동 로그인, 접근 토큰 갱신, 게임 세션 발급을 하고 토큰은 이 객체 밖으로 내보내지 않는다 </summary>
    public sealed class LauncherSession : IDisposable
    {
        // 토큰을 바꾸는 작업(로그인, 갱신, 로그아웃)은 모두 _gate 하나로 직렬화한다.
        // 갱신 토큰은 회전형이라 두 갱신이 같은 토큰을 동시에 보내면 서버는 두 번째를 재사용으로 보고 계정 세션 전체를 폐기한다.
        // 서버는 갱신 토큰을 회전할 때 그 토큰 아래에서 발급한 게임 세션도 모두 폐기한다. 그래서 갱신은 필요할 때만 하고
        // (게임 시작, 자동 로그인, 사용자 동작) 백그라운드 타이머로 하지 않으며, 게임이 도는 동안(IsRefreshSuspended)에는
        // 접근 토큰이 만료돼도 갱신하지 않는다. 갱신하면 실행 중인 게임의 세션이 끊긴다

        /****************************************
        *                Fields
        ****************************************/

        // 접근 토큰이 이보다 적게 남으면 쓰기 전에 갱신한다
        public const int REFRESH_MARGIN_MS = 60000;

        // 첫 요청과, 접근 토큰이 거절된 뒤 갱신하고 한 번 더
        private const int MAX_ISSUE_ATTEMPTS = 2;

        private readonly AuthApiClient      _api;
        private readonly IRefreshTokenStore _store;
        private readonly TimeProvider       _time;
        private readonly SemaphoreSlim      _gate = new SemaphoreSlim(1, 1);
        private readonly object             _lock = new object();

        private ELauncherSessionState _state = ELauncherSessionState.LoggedOut;
        private string?               _accountId;
        private string?               _accessToken;
        private string?               _refreshToken;
        private long                  _accessIssuedTimestamp;
        private TimeSpan              _accessLifetime;
        private bool                  _isRemembered;
        private volatile bool         _isRefreshSuspended;

        /****************************************
        *              Properties
        ****************************************/

        public ELauncherSessionState State
        {
            get
            {
                lock (_lock)
                {
                    return _state;
                }
            }
        }

        public string? AccountId
        {
            get
            {
                lock (_lock)
                {
                    return _accountId;
                }
            }
        }

        public bool HasStoredLogin => _store.HasToken;

        // 런처가 시작한 게임이 도는 동안 true로 둔다. 켜져 있으면 어떤 경로로도 갱신 토큰을 보내지 않는다
        public bool IsRefreshSuspended
        {
            get => _isRefreshSuspended;
            set => _isRefreshSuspended = value;
        }

        // 작업을 부른 스레드나 스레드 풀에서 불린다. 받는 쪽이 UI 스레드로 넘긴다
        public event Action? StateChanged;

        /****************************************
        *                Methods
        ****************************************/

        public LauncherSession(AuthApiClient api, IRefreshTokenStore store, TimeProvider? time = null)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(store);

            _api   = api;
            _store = store;
            _time  = time ?? TimeProvider.System;
        }

        public void Dispose() { _gate.Dispose(); }

        /// <summary> 아이디, 비밀번호로 로그인한다. remember면 갱신 토큰을 저장해 다음 실행에 자동 로그인한다 </summary>
        public async Task<SessionResult> LoginWithCredentialsAsync(string loginId, string password, bool remember,
            ClientBuildInfo build, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(build);

            string normalized = LoginIdRules.Normalize(loginId);
            if (!LoginIdRules.IsValid(normalized) || string.IsNullOrEmpty(password))
            {
                return SessionResult.Fail(ESessionError.InvalidInput);
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (State != ELauncherSessionState.LoggedOut)
                {
                    return SessionResult.Fail(ESessionError.Busy);
                }

                SetState(ELauncherSessionState.LoggingIn);
                AuthApiResult<AuthExchangeResponse> result;
                try
                {
                    result = await _api.ExchangeCredentialsAsync(normalized, password, build, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    SetState(ELauncherSessionState.LoggedOut);
                    throw;
                }

                if (!result.IsSuccess || !IsUsable(result.Value))
                {
                    SetState(ELauncherSessionState.LoggedOut);
                    return SessionResult.Fail(ESessionError.ApiFailed, ToFailure(result));
                }

                if (!remember)
                {
                    _store.Clear();
                }

                return Apply(result.Value!, remember);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary> 저장된 갱신 토큰으로 로그인한다. 저장된 것이 없으면 NoStoredLogin </summary>
        public async Task<SessionResult> TryAutoLoginAsync(ClientBuildInfo build, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(build);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (State != ELauncherSessionState.LoggedOut)
                {
                    return SessionResult.Fail(ESessionError.Busy);
                }

                if (_isRefreshSuspended)
                {
                    return SessionResult.Fail(ESessionError.RefreshSuspended);
                }

                if (!_store.TryLoad(out string? storedToken))
                {
                    return SessionResult.Fail(ESessionError.NoStoredLogin);
                }

                SetState(ELauncherSessionState.LoggingIn);
                SessionResult result = await RefreshLockedAsync(storedToken, build, true).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    SetState(ELauncherSessionState.LoggedOut);
                }

                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary> 접근 토큰이 REFRESH_MARGIN_MS보다 적게 남았으면 갱신한다 </summary>
        public async Task<SessionResult> EnsureAccessTokenAsync(ClientBuildInfo build, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(build);

            if (State != ELauncherSessionState.LoggedIn)
            {
                return SessionResult.Fail(ESessionError.NotLoggedIn);
            }

            if (!NeedsRefresh())
            {
                return SessionResult.Success();
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string? refreshToken;
                lock (_lock)
                {
                    refreshToken = (_state == ELauncherSessionState.LoggedIn) ? _refreshToken : null;
                }

                if (refreshToken == null)
                {
                    return SessionResult.Fail(ESessionError.NotLoggedIn);
                }

                // 기다리는 동안 앞선 호출이 이미 갱신했으면 다시 보내지 않는다
                if (!NeedsRefresh())
                {
                    return SessionResult.Success();
                }

                if (_isRefreshSuspended)
                {
                    return SessionResult.Fail(ESessionError.RefreshSuspended);
                }

                bool isRemembered;
                lock (_lock)
                {
                    isRemembered = _isRemembered;
                }

                return await RefreshLockedAsync(refreshToken, build, isRemembered).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary> 게임 실행 직전에 게임 전용 하위 세션을 받는다. 접근 토큰이 거절되면 한 번 갱신하고 다시 요청한다 </summary>
        public async Task<SessionResult<GameSessionIssueResponse>> IssueGameSessionAsync(ClientBuildInfo build,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(build);

            AuthApiResult<GameSessionIssueResponse>? last = null;
            for (int attempt = 0; attempt < MAX_ISSUE_ATTEMPTS; ++attempt)
            {
                SessionResult ensured = await EnsureAccessTokenAsync(build, cancellationToken).ConfigureAwait(false);
                if (!ensured.IsSuccess)
                {
                    return SessionResult<GameSessionIssueResponse>.From(ensured);
                }

                string? accessToken;
                lock (_lock)
                {
                    accessToken = _accessToken;
                }

                if (accessToken == null)
                {
                    return SessionResult<GameSessionIssueResponse>.From(SessionResult.Fail(ESessionError.NotLoggedIn));
                }

                last = await _api.IssueGameSessionAsync(accessToken, build, cancellationToken).ConfigureAwait(false);
                if (last.IsSuccess && !string.IsNullOrEmpty(last.Value?.GameSessionToken))
                {
                    return SessionResult<GameSessionIssueResponse>.Success(last.Value!);
                }

                if (last.Error != EAuthApiError.Unauthorized)
                {
                    break;
                }

                // 서버가 접근 토큰을 먼저 만료, 폐기했다. 다음 시도에서 갱신하게 남은 시간을 0으로 만든다
                ExpireAccessToken(accessToken);
            }

            AuthApiResult failure = (last == null) ? AuthApiResult.Fail(EAuthApiError.Malformed, 0, 0) : ToFailure(last);
            return SessionResult<GameSessionIssueResponse>.From(SessionResult.Fail(ESessionError.ApiFailed, failure));
        }

        /// <summary> 메모리와 저장소의 토큰을 지운다 </summary>
        public async Task LogoutAsync()
        {
            // TODO(민성): 서버 로그아웃 경로가 생기면 갱신 토큰 계열과 하위 게임 세션을 서버에서도 폐기한다. 지금은 로컬에서만 지운다
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ClearLocked();
            }
            finally
            {
                _gate.Release();
            }
        }

        // _gate를 잡은 상태에서만 부른다. 취소를 넘기지 않는 이유는 보낸 뒤 취소하면 응답을 잃은 것과 같아 로그인을 버려야 하기 때문이다
        private async Task<SessionResult> RefreshLockedAsync(string refreshToken, ClientBuildInfo build, bool remember)
        {
            if (_isRefreshSuspended)
            {
                return SessionResult.Fail(ESessionError.RefreshSuspended);
            }

            AuthApiResult<AuthExchangeResponse> result = await _api.ExchangeRefreshAsync(refreshToken, build, CancellationToken.None)
                .ConfigureAwait(false);
            if (result.IsSuccess && IsUsable(result.Value))
            {
                return Apply(result.Value!, remember);
            }

            AuthApiResult failure = ToFailure(result);

            // 응답을 잃었으면 서버가 이미 회전했을 수 있다. 이전 토큰을 다시 보내면 재사용으로 계정 세션 전체가 폐기되므로 재로그인을 요구한다
            if (failure.IsOutcomeUnknown || (failure.Error == EAuthApiError.Unauthorized))
            {
                ClearLocked();
                return SessionResult.Fail(ESessionError.SessionLost, failure);
            }

            return SessionResult.Fail(ESessionError.ApiFailed, failure);
        }

        private SessionResult Apply(AuthExchangeResponse response, bool remember)
        {
            // 만료는 서버 시각 기준이라 PC 시계 대신 서버가 준 두 값의 차를 남은 시간으로 쓴다
            long lifetimeMs = Math.Max(0L, response.AccessTokenExpiresAtUnixMs - response.ServerTimeUnixMs);

            bool isStoreFailed = false;
            if (remember)
            {
                isStoreFailed = !_store.TrySave(response.RefreshToken);
            }

            lock (_lock)
            {
                _accountId             = response.AccountId;
                _accessToken           = response.AccessToken;
                _refreshToken          = response.RefreshToken;
                _accessIssuedTimestamp = _time.GetTimestamp();
                _accessLifetime        = TimeSpan.FromMilliseconds(lifetimeMs);
                _isRemembered          = remember;
            }

            SetState(ELauncherSessionState.LoggedIn);
            return SessionResult.Success(isStoreFailed);
        }

        private void ClearLocked()
        {
            lock (_lock)
            {
                _accountId    = null;
                _accessToken  = null;
                _refreshToken = null;
                _isRemembered = false;
            }

            _store.Clear();
            SetState(ELauncherSessionState.LoggedOut);
        }

        private bool NeedsRefresh()
        {
            lock (_lock)
            {
                TimeSpan remaining = _accessLifetime - _time.GetElapsedTime(_accessIssuedTimestamp);
                return remaining < TimeSpan.FromMilliseconds(REFRESH_MARGIN_MS);
            }
        }

        private void ExpireAccessToken(string rejectedToken)
        {
            lock (_lock)
            {
                if (string.Equals(_accessToken, rejectedToken, StringComparison.Ordinal))
                {
                    _accessLifetime = TimeSpan.Zero;
                }
            }
        }

        private void SetState(ELauncherSessionState state)
        {
            lock (_lock)
            {
                if (_state == state)
                {
                    return;
                }

                _state = state;
            }

            StateChanged?.Invoke();
        }

        private static bool IsUsable(AuthExchangeResponse? response)
        {
            return (response != null) && !string.IsNullOrEmpty(response.AccountId) && !string.IsNullOrEmpty(response.AccessToken)
                && !string.IsNullOrEmpty(response.RefreshToken);
        }

        // 2xx인데 필수 필드가 비었으면 서버는 처리를 끝냈으므로 본문을 읽지 못한 것과 같이 본다
        private static AuthApiResult ToFailure(AuthApiResult result)
        {
            return result.IsSuccess ? AuthApiResult.Fail(EAuthApiError.Malformed, 0, result.StatusCode) : result;
        }
    }
}
