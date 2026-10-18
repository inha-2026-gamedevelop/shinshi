// System
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> HttpClient 하나로 백엔드에 요청을 보내는 전송 계층, 경로에 맞는 자격과 인스턴스 ID를 헤더에 싣는다 </summary>
    public sealed class HttpClientBackendTransport : IBackendTransport
    {
        // UnityWebRequest는 메인 스레드에서만 시작할 수 있어 스레드 풀에서 도는 게이트웨이와 맞지 않는다.
        // HttpClient는 연결을 재사용하도록 프로세스에 하나만 만든다. 요청마다 만들면 소켓이 쌓인다.
        // 시간 제한은 게이트웨이가 취소 토큰으로 건다. HttpClient.Timeout까지 두면 어느 쪽이 끊었는지 구분할 수 없다.
        //
        // 서비스 자격은 임대 발급 경로에만 싣는다. 나머지 서버 경로까지 서비스 자격으로 부르면 그 자격이 새는 순간
        // 어느 인스턴스로든 행세할 수 있다. 임대 토큰은 인스턴스와 bootId에 묶여 수명이 짧다.
        //
        // API는 임대를 갱신하는 순간 이전 토큰을 폐기한다. 갱신 직전에 옛 토큰으로 나간 요청은 401을 받으므로, 그 사이
        // 새 임대가 생겼을 때만 같은 요청(같은 operationId와 본문)을 새 토큰으로 한 번 더 보낸다. 401은 인증 단계의 거절이라
        // 요청이 처리되지 않았으므로 operationId가 없는 요청도 다시 보내도 된다

        /****************************************
        *                Fields
        ****************************************/

        private const string JSON_MEDIA_TYPE     = "application/json";
        private const int    STATUS_UNAUTHORIZED = 401;

        // 같은 서버로 동시에 열 연결 수. 기본값(Mono는 10 안팎)이면 응답 대기 요청이 많을 때 클라이언트 안에서 줄을 선다.
        // 게이트웨이의 응답 대기 상한(256)보다 작게 둬 백엔드 쪽 소켓이 한꺼번에 늘지 않게 한다
        private const int    CONNECTION_LIMIT    = 64;

        private readonly HttpClient        _client;
        private readonly Uri               _baseUri;
        private readonly ServerCredentials _credentials;

        /****************************************
        *                Methods
        ****************************************/

        public HttpClientBackendTransport(Uri baseUri, ServerCredentials credentials)
            : this(baseUri, credentials, null)
        {
        }

        /// <summary> handler는 시험에서 응답을 바꿀 때만 넣는다, null이면 기본 처리기를 쓴다 </summary>
        public HttpClientBackendTransport(Uri baseUri, ServerCredentials credentials, HttpMessageHandler handler)
        {
            if ((baseUri == null) || (!baseUri.IsAbsoluteUri))
            {
                throw new ArgumentException("절대 기준 URL이 필요하다", nameof(baseUri));
            }

            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }

            // 기준 URL에 경로 접두사가 있어도 상대 경로가 그 아래로 붙도록 끝을 '/'로 맞춘다
            string baseText = baseUri.AbsoluteUri;
            _baseUri     = baseText.EndsWith("/", StringComparison.Ordinal) ? baseUri : new Uri(baseText + "/");
            _credentials = credentials;
            _client      = new HttpClient(handler ?? CreateDefaultHandler());
            _client.Timeout                      = Timeout.InfiniteTimeSpan;
            _client.MaxResponseContentBufferSize = Constants.Network.MAX_BACKEND_RESPONSE_BYTES;
        }

        // Unity Mono의 HttpClientHandler는 HttpWebRequest 위에서 돌아 ServicePointManager 한도를 따른다. 새 런타임은 MaxConnectionsPerServer를 따른다.
        // 어느 한쪽이 지원되지 않아도 다른 쪽이 적용되므로 지원 예외는 삼킨다
        private static HttpMessageHandler CreateDefaultHandler()
        {
            if (ServicePointManager.DefaultConnectionLimit < CONNECTION_LIMIT)
            {
                ServicePointManager.DefaultConnectionLimit = CONNECTION_LIMIT;
            }

            var handler = new HttpClientHandler();
            try
            {
                handler.MaxConnectionsPerServer = CONNECTION_LIMIT;
            }
            catch (Exception exception) when ((exception is NotSupportedException) || (exception is NotImplementedException))
            {
                UnityEngine.Debug.LogWarning($"[Backend] 이 런타임은 처리기 단위 연결 한도를 지원하지 않는다, type={exception.GetType().Name}");
            }

            return handler;
        }

        public bool CanSend(BackendRequest request)
        {
            return (request != null) && (_credentials.TryGetAuthorization(request.Path, out _));
        }

        public async Task<BackendTransportResult> SendAsync(BackendRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // CanSend 뒤에 임대가 만료됐을 수 있다. 자격 없이 보내지 않고 연결 실패와 같은 일시 실패로 끝낸다
            if (!_credentials.TryGetAuthorization(request.Path, out string authorization, out int leaseVersion))
            {
                throw new InvalidOperationException("서버 임대가 없어 보내지 않는다");
            }

            BackendTransportResult result = await SendOnceAsync(request, authorization, cancellationToken).ConfigureAwait(false);
            if ((result.StatusCode != STATUS_UNAUTHORIZED) || (ServerCredentials.IsLeasePath(request.Path))
                || (!_credentials.TryGetAuthorization(request.Path, out string renewed, out int renewedVersion))
                || (renewedVersion == leaseVersion))
            {
                return result;
            }

            return await SendOnceAsync(request, renewed, cancellationToken).ConfigureAwait(false);
        }

        private async Task<BackendTransportResult> SendOnceAsync(BackendRequest request, string authorization,
            CancellationToken cancellationToken)
        {
            HttpMethod method = (request.Method == EBackendMethod.Post) ? HttpMethod.Post : HttpMethod.Get;
            var        uri    = new Uri(_baseUri, request.Path.Substring(1));
            using (var message = new HttpRequestMessage(method, uri))
            {
                message.Headers.TryAddWithoutValidation(ApiRoutes.AUTHORIZATION_HEADER, authorization);
                message.Headers.TryAddWithoutValidation(JoinApiRoutes.SERVER_INSTANCE_HEADER, _credentials.InstanceId);
                if (request.BodyJson != null)
                {
                    message.Content = new StringContent(request.BodyJson, Encoding.UTF8, JSON_MEDIA_TYPE);
                }

                using (HttpResponseMessage response = await _client.SendAsync(message, HttpCompletionOption.ResponseContentRead,
                    cancellationToken).ConfigureAwait(false))
                {
                    string body = (response.Content != null)
                        ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                        : null;
                    return new BackendTransportResult((int)response.StatusCode, body, ReadRetryAfter(response));
                }
            }
        }

        // 초 단위와 날짜 형식을 모두 받고, 음수는 0으로, 너무 긴 값은 상한으로 자른다
        private static double ReadRetryAfter(HttpResponseMessage response)
        {
            RetryConditionHeaderValue retryAfter = response.Headers.RetryAfter;
            if (retryAfter == null)
            {
                return 0.0;
            }

            double seconds = 0.0;
            if (retryAfter.Delta.HasValue)
            {
                seconds = retryAfter.Delta.Value.TotalSeconds;
            }
            else if (retryAfter.Date.HasValue)
            {
                seconds = (retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
            }

            return Math.Max(0.0, Math.Min(seconds, Constants.Network.MAX_BACKEND_RETRY_AFTER_SECONDS));
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
