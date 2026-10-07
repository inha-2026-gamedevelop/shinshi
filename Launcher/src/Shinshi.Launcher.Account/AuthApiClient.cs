// System
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Launcher.Account
{
    /// <summary> 런처가 부르는 인증, 서비스 상태 API. 토큰과 초대 코드는 요청 본문과 헤더에만 싣고 어디에도 기록하지 않는다 </summary>
    public sealed class AuthApiClient
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int DEFAULT_REQUEST_TIMEOUT_MS = 15000;

        private const string JSON_MEDIA_TYPE   = "application/json";
        private const char   PATH_SEPARATOR    = '/';
        private const int    STATUS_SERVER_MIN = 500;

        private readonly HttpClient             _client;
        private readonly Uri                    _baseUri;
        private readonly TimeSpan               _requestTimeout;
        private readonly JsonSerializerSettings _json = ContractJson.CreateSettings();

        /****************************************
        *              Properties
        ****************************************/

        public Uri BaseUri => _baseUri;

        /****************************************
        *                Methods
        ****************************************/

        // HttpClient 수명은 호출자가 관리한다. 요청마다 제한 시간을 따로 걸므로 HttpClient.Timeout에 기대지 않는다
        public AuthApiClient(HttpClient client, Uri baseUri, TimeSpan? requestTimeout = null)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(baseUri);
            if (!baseUri.IsAbsoluteUri || ((baseUri.Scheme != Uri.UriSchemeHttps) && (baseUri.Scheme != Uri.UriSchemeHttp)))
            {
                throw new ArgumentException("API base URI must be an absolute http or https URI.", nameof(baseUri));
            }

            _client         = client;
            _requestTimeout = requestTimeout ?? TimeSpan.FromMilliseconds(DEFAULT_REQUEST_TIMEOUT_MS);

            // "https://host/api"에 상대 경로를 붙이면 마지막 세그먼트가 사라지므로 끝에 "/"를 보장한다
            string text = baseUri.AbsoluteUri;
            _baseUri = text.EndsWith(PATH_SEPARATOR) ? baseUri : new Uri(text + PATH_SEPARATOR);
        }

        // 비밀번호는 이 요청 본문에만 싣는다. 런처 화면의 로그인 경로다.
        // 빌드를 보내지 않는다. 설치 빌드가 서버와 달라도 로그인은 되고, 빌드 검사는 게임 세션 발급이 한다
        public Task<AuthApiResult<AuthExchangeResponse>> ExchangeCredentialsAsync(string loginId, string password,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(loginId);
            ArgumentException.ThrowIfNullOrEmpty(password);

            AuthExchangeRequest request = AuthExchangeRequest.ForCredentials(loginId, password, null!);
            return SendAsync<AuthExchangeResponse>(HttpMethod.Post, ApiRoutes.AUTH_EXCHANGE, request, null, cancellationToken);
        }

        // 운영자가 기기 변경, 계정 복구에 쓰는 경로라 런처 화면에는 노출하지 않는다
        public Task<AuthApiResult<AuthExchangeResponse>> ExchangeInviteAsync(string inviteCode, ClientBuildInfo build,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(inviteCode);
            ArgumentNullException.ThrowIfNull(build);

            AuthExchangeRequest request = new AuthExchangeRequest(inviteCode, null, build);
            return SendAsync<AuthExchangeResponse>(HttpMethod.Post, ApiRoutes.AUTH_EXCHANGE, request, null, cancellationToken);
        }

        // 응답을 잃으면(IsOutcomeUnknown) 같은 갱신 토큰을 다시 보내지 않는다. 서버가 이미 회전했으면 재사용으로 보고 폐기한다.
        // 빌드를 보내지 않으므로 서버는 점검만 확인한다
        public Task<AuthApiResult<AuthExchangeResponse>> ExchangeRefreshAsync(string refreshToken,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(refreshToken);

            AuthExchangeRequest request = new AuthExchangeRequest(null, refreshToken, null!);
            return SendAsync<AuthExchangeResponse>(HttpMethod.Post, ApiRoutes.AUTH_EXCHANGE, request, null, cancellationToken);
        }

        public Task<AuthApiResult<GameSessionIssueResponse>> IssueGameSessionAsync(string accessToken, ClientBuildInfo build,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(accessToken);
            ArgumentNullException.ThrowIfNull(build);

            GameSessionIssueRequest request = new GameSessionIssueRequest(build);
            return SendAsync<GameSessionIssueResponse>(HttpMethod.Post, ApiRoutes.AUTH_GAME_SESSION, request, accessToken,
                cancellationToken);
        }

        /// <summary> 갱신 토큰 원문으로 서버의 로그인 묶음과 하위 게임 세션을 폐기한다. bearer 없이 보내며 서버는 같은 요청을 반복해도 204다 </summary>
        public async Task<AuthApiResult> LogoutAsync(string refreshToken, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(refreshToken);

            AuthApiResult<string> result = await SendAsync<string>(HttpMethod.Post, ApiRoutes.AUTH_LOGOUT,
                new AuthLogoutRequest(refreshToken), null, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? AuthApiResult.Success(result.StatusCode) : result;
        }

        /// <summary> 방금 발급한 게임 세션을 그 토큰을 bearer로 보내 폐기한다 </summary>
        public async Task<AuthApiResult> RevokeGameSessionAsync(string gameSessionToken, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(gameSessionToken);

            AuthApiResult<string> result = await SendAsync<string>(HttpMethod.Post, ApiRoutes.AUTH_GAME_SESSION_REVOKE, null,
                gameSessionToken, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? AuthApiResult.Success(result.StatusCode) : result;
        }

        public Task<AuthApiResult<ServiceStateResponse>> GetServiceStateAsync(CancellationToken cancellationToken)
        {
            return SendAsync<ServiceStateResponse>(HttpMethod.Get, ApiRoutes.SERVICE_STATE, null, null, cancellationToken);
        }

        // T가 string이면 본문을 읽지 않고 2xx만 성공으로 본다(본문 없는 호출용)
        private async Task<AuthApiResult<T>> SendAsync<T>(HttpMethod method, string route, object? body, string? bearer,
            CancellationToken cancellationToken) where T : class
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);

            using HttpRequestMessage request = new HttpRequestMessage(method, new Uri(_baseUri, route.TrimStart(PATH_SEPARATOR)));
            if (body != null)
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(body, _json), Encoding.UTF8, JSON_MEDIA_TYPE);
            }

            if (bearer != null)
            {
                request.Headers.TryAddWithoutValidation(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + bearer);
            }

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                return Fail<T>(ClassifyRequestFailure(exception), 0, 0);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail<T>(EAuthApiError.ResponseLost, 0, 0);
            }

            using (response)
            {
                int status = (int)response.StatusCode;
                string text;
                try
                {
                    text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    return response.IsSuccessStatusCode ? Fail<T>(EAuthApiError.ResponseLost, 0, status) : MapFailure<T>(status, null);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return response.IsSuccessStatusCode ? Fail<T>(EAuthApiError.ResponseLost, 0, status) : MapFailure<T>(status, null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return MapFailure<T>(status, text);
                }

                if (typeof(T) == typeof(string))
                {
                    return AuthApiResult<T>.Success((T)(object)string.Empty, status);
                }

                T? value = TryDeserialize<T>(text);
                return (value == null) ? Fail<T>(EAuthApiError.Malformed, 0, status) : AuthApiResult<T>.Success(value, status);
            }
        }

        private AuthApiResult<T> MapFailure<T>(int status, string? text) where T : class
        {
            ApiRejection? rejection = (text == null) ? null : TryDeserialize<ApiRejection>(text);
            int reason = rejection?.ReasonCode ?? ContractCodes.RejectReason.NONE;

            if (status == (int)HttpStatusCode.Unauthorized)
            {
                return Fail<T>(EAuthApiError.Unauthorized, reason, status);
            }

            if ((status == (int)HttpStatusCode.TooManyRequests) || (reason == ContractCodes.RejectReason.RATE_LIMITED))
            {
                return Fail<T>(EAuthApiError.RateLimited, reason, status);
            }

            if ((reason == ContractCodes.RejectReason.VERSION_MISMATCH) || (reason == ContractCodes.RejectReason.BUILD_NOT_ALLOWED))
            {
                return Fail<T>(EAuthApiError.VersionMismatch, reason, status);
            }

            if (reason == ContractCodes.RejectReason.MAINTENANCE)
            {
                return Fail<T>(EAuthApiError.Maintenance, reason, status);
            }

            // 프록시의 게이트웨이 오류와 시간 초과는 뒤의 API가 처리를 끝냈는지 알 수 없다
            if ((status == (int)HttpStatusCode.BadGateway) || (status == (int)HttpStatusCode.GatewayTimeout))
            {
                return Fail<T>(EAuthApiError.ResponseLost, reason, status);
            }

            if (status >= STATUS_SERVER_MIN)
            {
                return Fail<T>(EAuthApiError.ServerError, reason, status);
            }

            return Fail<T>(EAuthApiError.Rejected, reason, status);
        }

        // 연결 자체를 맺지 못한 실패만 요청이 서버에 닿지 않았다고 확신할 수 있다. 나머지는 처리됐을 수 있다고 본다
        private static EAuthApiError ClassifyRequestFailure(HttpRequestException exception)
        {
            switch (exception.HttpRequestError)
            {
                case HttpRequestError.ConnectionError:
                case HttpRequestError.NameResolutionError:
                case HttpRequestError.SecureConnectionError:
                case HttpRequestError.ProxyTunnelError:
                    return EAuthApiError.Unreachable;
                default:
                    return EAuthApiError.ResponseLost;
            }
        }

        private T? TryDeserialize<T>(string text) where T : class
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(text, _json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static AuthApiResult<T> Fail<T>(EAuthApiError error, int reasonCode, int statusCode) where T : class
        {
            return new AuthApiResult<T>(error, reasonCode, statusCode, null);
        }
    }
}
