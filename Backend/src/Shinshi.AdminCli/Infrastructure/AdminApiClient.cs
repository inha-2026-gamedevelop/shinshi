// System
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Contracts;

namespace Shinshi.AdminCli
{
    /// <summary> 어드민 API 호출. DB에 붙지 않고 이 HTTP 경로만 쓴다. 핸들러를 받아 테스트가 TestServer 핸들러를 넣는다 </summary>
    internal sealed class AdminApiClient : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    TIMEOUT_SECONDS       = 30;
        private const string JSON_MEDIA_TYPE       = "application/json";
        private const string REASON_CODE_PROPERTY  = "reasonCode";
        private const string STATUS_PROPERTY       = "status";
        private const string OPERATION_ID_PROPERTY = "operationId";

        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string? _token;
        private readonly JsonSerializerSettings _settings = ContractJson.CreateSettings();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> baseUrl 끝의 슬래시는 떼고 경로 상수를 그대로 붙인다. 경로 접두사가 있는 API 주소도 그대로 쓴다 </summary>
        public AdminApiClient(HttpMessageHandler handler, string baseUrl, string? token)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
            _http = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(TIMEOUT_SECONDS)
            };
            _baseUrl = baseUrl.TrimEnd('/');
            _token   = token;
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        public async Task<AdminApiResponse<T>> GetAsync<T>(string pathAndQuery, CancellationToken cancellationToken)
        {
            AdminApiResponse<string> raw = await SendRawAsync(HttpMethod.Get, pathAndQuery, null, false,
                cancellationToken);
            return new AdminApiResponse<T>(raw.StatusCode, raw.RawBody, Deserialize<T>(raw.RawBody));
        }

        /// <summary> isWrite면 연결 실패를 "응답 유실"로 알려 같은 operationId로 다시 보내라고 안내하게 한다 </summary>
        public async Task<AdminApiResponse<T>> SendAsync<T>(HttpMethod method, string pathAndQuery, object? body,
            bool isWrite, CancellationToken cancellationToken)
        {
            AdminApiResponse<string> raw = await SendRawAsync(method, pathAndQuery, body, isWrite, cancellationToken);
            return new AdminApiResponse<T>(raw.StatusCode, raw.RawBody, Deserialize<T>(raw.RawBody));
        }

        /// <summary> 200, 204만 성공이다. 202(PENDING)를 포함한 나머지는 AdminApiException이다 </summary>
        public async Task<AdminApiResponse<string>> SendRawAsync(HttpMethod method, string pathAndQuery, object? body,
            bool isWrite, CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, _baseUrl + pathAndQuery);
            if (body != null)
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(body, _settings), Encoding.UTF8,
                    JSON_MEDIA_TYPE);
            }

            if (_token != null)
            {
                request.Headers.TryAddWithoutValidation(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + _token);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                // 예외 메시지는 주소만 담고 요청 헤더(토큰)는 담지 않는다
                throw new CliUnavailableException("API에 연결하지 못했습니다(" + _baseUrl + "): " + exception.Message,
                    isWrite, exception);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new CliUnavailableException("API 응답이 " + TIMEOUT_SECONDS + "초 안에 오지 않았습니다(" + _baseUrl + ").",
                    isWrite, exception);
            }

            using (response)
            {
                string raw = await response.Content.ReadAsStringAsync(cancellationToken);
                int status = (int)response.StatusCode;
                if ((response.StatusCode == HttpStatusCode.OK) || (response.StatusCode == HttpStatusCode.NoContent))
                {
                    return new AdminApiResponse<string>(status, raw, raw);
                }

                throw ToException(status, raw);
            }
        }

        private T Deserialize<T>(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new CliUnavailableException("API가 빈 응답을 보냈습니다.", false);
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(raw, _settings)
                    ?? throw new CliUnavailableException("API 응답을 읽지 못했습니다.", false);
            }
            catch (JsonException exception)
            {
                throw new CliUnavailableException("API 응답이 예상한 JSON 형식이 아닙니다.", false, exception);
            }
        }

        // ApiRejection과 거절된 OperationResult는 둘 다 reasonCode를 싣는다. status가 있으면 OperationResult다
        private static AdminApiException ToException(int status, string raw)
        {
            int? reasonCode = null;
            string? operationId = null;
            JObject? body = TryParseObject(raw);
            if (body != null)
            {
                JToken? reason = body[REASON_CODE_PROPERTY];
                if ((reason != null) && (reason.Type == JTokenType.Integer))
                {
                    reasonCode = reason.Value<int>();
                }

                if (body[STATUS_PROPERTY] != null)
                {
                    operationId = body[OPERATION_ID_PROPERTY]?.Value<string>();
                }
            }

            return new AdminApiException(status, reasonCode, operationId, raw ?? string.Empty);
        }

        private static JObject? TryParseObject(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                return JToken.Parse(raw) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
