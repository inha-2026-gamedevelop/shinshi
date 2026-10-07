// System
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Admin.Web.Infrastructure
{
    /// <summary> 서버에서 어드민 API만 호출한다. 브라우저 헤더는 복사하지 않으며 진단 원문은 반환하지 않는다 </summary>
    public sealed class AdminWebApiClient
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string API_BASE_KEY = "AdminWeb:ApiBaseUrl";
        public const string DEFAULT_API_BASE = "http://127.0.0.1:5080";

        private const int    TIMEOUT_SECONDS    = 30;
        private const int    MAX_RESPONSE_BYTES = 8 * 1024 * 1024;
        private const string JSON_MEDIA_TYPE    = "application/json";

        private readonly HttpClient _http;
        private readonly AdminWebContext _context;
        private readonly string _baseUrl;
        private readonly JsonSerializerSettings _settings = ContractJson.CreateSettings();

        /****************************************
        *                Methods
        ****************************************/

        public AdminWebApiClient(HttpClient http, AdminWebContext context, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(configuration);
            _http = http;
            _context = context;
            _baseUrl = NormalizeBaseUrl(configuration[API_BASE_KEY] ?? DEFAULT_API_BASE);
            _http.Timeout = TimeSpan.FromSeconds(TIMEOUT_SECONDS);
            _http.MaxResponseContentBufferSize = MAX_RESPONSE_BYTES;
        }

        public Task<AdminWebApiResult<T>> GetAsync<T>(string pathAndQuery,
            CancellationToken cancellationToken = default)
        {
            return SendCoreAsync<T>(HttpMethod.Get, pathAndQuery, null, null, false, true, cancellationToken);
        }

        public Task<AdminWebApiResult<T>> SendAsync<T>(HttpMethod method, string pathAndQuery, object? body,
            bool isWrite = false, CancellationToken cancellationToken = default)
        {
            byte[]? serialized = body == null ? null
                : Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body, _settings));
            string? operationId = null;
            if ((isWrite) && (serialized != null))
            {
                operationId = JObject.Parse(Encoding.UTF8.GetString(serialized)).Value<string>("operationId");
            }

            return SendCoreAsync<T>(method, pathAndQuery,
                serialized == null ? null : CreateContent(serialized), operationId, isWrite, true, cancellationToken);
        }

        /// <summary> 확인한 요청의 바이트와 ID를 다시 보낸다. 연결 실패 뒤에도 본문을 새로 만들지 않는다 </summary>
        public Task<AdminWebApiResult<T>> SendAsync<T>(PendingAdminWrite pending,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(pending);
            return SendCoreAsync<T>(pending.Method, pending.PathAndQuery, pending.CreateContent(),
                pending.OperationId, true, true, cancellationToken);
        }

        internal Task<AdminWebApiResult<AdminLoginResponse>> LoginAsync(AdminLoginRequest request,
            CancellationToken cancellationToken)
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request, _settings));
            return SendCoreAsync<AdminLoginResponse>(HttpMethod.Post, AdminApiRoutes.AUTH_LOGIN,
                CreateContent(body), null, false, false, cancellationToken);
        }

        internal Task<AdminWebApiResult<JToken>> LogoutAsync(CancellationToken cancellationToken)
        {
            return SendCoreAsync<JToken>(HttpMethod.Post, AdminApiRoutes.AUTH_LOGOUT,
                null, null, false, true, cancellationToken);
        }

        public static string NormalizeBaseUrl(string value)
        {
            if ((!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
                || ((uri.Scheme != Uri.UriSchemeHttp) && (uri.Scheme != Uri.UriSchemeHttps))
                || (!string.IsNullOrEmpty(uri.UserInfo)) || (uri.Query.Length > 0) || (uri.Fragment.Length > 0))
            {
                throw new InvalidOperationException("AdminWeb:ApiBaseUrl must be an HTTP or HTTPS URL without credentials, query or fragment.");
            }

            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        internal static void ValidatePath(string pathAndQuery)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pathAndQuery);
            string path = pathAndQuery.Split('?')[0];
            string decoded = Uri.UnescapeDataString(path);
            if (!decoded.StartsWith(AdminApiRoutes.PREFIX + "/", StringComparison.Ordinal)
                || (decoded.IndexOf('\\') >= 0) || (pathAndQuery.IndexOf('#') >= 0))
            {
                throw new ArgumentException("어드민 API 경로만 호출할 수 있습니다.", nameof(pathAndQuery));
            }

            foreach (string segment in decoded.Split('/'))
            {
                if ((segment == ".") || (segment == ".."))
                {
                    throw new ArgumentException("어드민 API 경로가 올바르지 않습니다.", nameof(pathAndQuery));
                }
            }
        }

        private async Task<AdminWebApiResult<T>> SendCoreAsync<T>(HttpMethod method, string pathAndQuery,
            HttpContent? content, string? operationId, bool isWrite, bool authenticated,
            CancellationToken cancellationToken)
        {
            using (content)
            {
                ValidatePath(pathAndQuery);
                string token = string.Empty;
                if ((authenticated) && (!_context.TryGetToken(out token)))
                {
                    return new AdminWebApiResult<T>(StatusCodes.Status401Unauthorized, default, "세션이 만료되었습니다. 다시 로그인해 주세요.",
                        operationId: operationId);
                }

                using HttpRequestMessage request = new(method, _baseUrl + pathAndQuery);
                request.Content = content;
                if (authenticated)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                try
                {
                    using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
                    string raw = await response.Content.ReadAsStringAsync(cancellationToken);
                    int statusCode = (int)response.StatusCode;
                    if (statusCode == StatusCodes.Status401Unauthorized)
                    {
                        if (authenticated)
                        {
                            _context.Revoke();
                        }
                        return new AdminWebApiResult<T>(statusCode, default,
                            authenticated ? "세션이 만료되었거나 폐기되었습니다. 다시 로그인해 주세요."
                                : "로그인 정보 또는 인증 코드를 확인해 주세요.", operationId: operationId);
                    }

                    JToken? parsed = TryParse(raw);
                    JToken? operation = parsed is JObject responseBody ? responseBody["operation"] ?? parsed : parsed;
                    int? reasonCode = ReadInteger(operation, "reasonCode") ?? ReadInteger(parsed, "reasonCode");
                    int? operationStatus = ReadInteger(operation, "status");
                    bool pending = (statusCode == StatusCodes.Status202Accepted) || (operationStatus == ContractCodes.OperationStatus.PENDING);
                    if (pending)
                    {
                        return new AdminWebApiResult<T>(statusCode, default,
                            "처리 중입니다. 성공으로 확정되지 않았습니다. 같은 요청으로 다시 확인해 주세요.",
                            reasonCode, operationId, isPending: true, canRetry: isWrite);
                    }

                    bool success = ((statusCode == StatusCodes.Status200OK) || (statusCode == StatusCodes.Status204NoContent))
                        && (operationStatus != ContractCodes.OperationStatus.REJECTED);
                    if (!success)
                    {
                        return new AdminWebApiResult<T>(statusCode, default, ErrorMessage(statusCode, reasonCode),
                            reasonCode, operationId, canRetry: (isWrite) && ((statusCode == StatusCodes.Status429TooManyRequests)
                                || (statusCode >= StatusCodes.Status500InternalServerError)));
                    }

                    if ((statusCode == StatusCodes.Status204NoContent) && string.IsNullOrWhiteSpace(raw))
                    {
                        return new AdminWebApiResult<T>(statusCode, default, operationId: operationId, isSuccess: true);
                    }

                    try
                    {
                        T? value = JsonConvert.DeserializeObject<T>(raw, _settings);
                        if (value == null)
                        {
                            return InvalidResponse<T>(operationId, isWrite);
                        }

                        return new AdminWebApiResult<T>(statusCode, value, operationId: operationId, isSuccess: true);
                    }
                    catch (JsonException)
                    {
                        return InvalidResponse<T>(operationId, isWrite);
                    }
                }
                catch (HttpRequestException)
                {
                    return Unavailable<T>(operationId, isWrite);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Unavailable<T>(operationId, isWrite);
                }
            }
        }

        private static HttpContent CreateContent(byte[] body)
        {
            ByteArrayContent content = new(body);
            content.Headers.ContentType = new MediaTypeHeaderValue(JSON_MEDIA_TYPE)
            {
                CharSet = "utf-8"
            };
            return content;
        }

        private static JToken? TryParse(string raw)
        {
            try
            {
                return string.IsNullOrWhiteSpace(raw) ? null : JToken.Parse(raw);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static int? ReadInteger(JToken? value, string property)
        {
            JToken? token = value is JObject item ? item[property] : null;
            return token?.Type == JTokenType.Integer ? token.Value<int>() : null;
        }

        private static AdminWebApiResult<T> Unavailable<T>(string? operationId, bool isWrite)
        {
            return new AdminWebApiResult<T>(StatusCodes.Status503ServiceUnavailable, default,
                isWrite ? "API 응답을 받지 못했습니다. 적용 여부가 불확실합니다. 같은 요청으로 재시도해 주세요."
                    : "API에 연결하지 못했습니다. 잠시 후 다시 조회해 주세요.",
                operationId: operationId, canRetry: isWrite, responseLost: isWrite);
        }

        private static AdminWebApiResult<T> InvalidResponse<T>(string? operationId, bool isWrite)
        {
            return new AdminWebApiResult<T>(StatusCodes.Status503ServiceUnavailable, default,
                isWrite ? "응답을 확인하지 못했습니다. 적용 여부가 불확실합니다. 같은 요청으로 재시도해 주세요."
                    : "API 응답을 읽지 못했습니다. 잠시 후 다시 조회해 주세요.",
                operationId: operationId, canRetry: isWrite, responseLost: isWrite);
        }

        private static string ErrorMessage(int statusCode, int? reasonCode)
        {
            if (reasonCode == ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT)
            {
                return "같은 작업 ID로 다른 요청이 제출되었습니다. 기존 작업을 조회해 주세요.";
            }
            if (reasonCode == ContractCodes.RejectReason.STALE_REVISION)
            {
                return "다른 운영자가 먼저 변경했습니다. 최신 상태를 조회한 뒤 새 작업으로 확인해 주세요.";
            }

            return statusCode switch
            {
                StatusCodes.Status403Forbidden => "이 작업을 수행할 권한이 없습니다.",
                StatusCodes.Status404NotFound => "대상을 찾을 수 없거나 접근이 허용되지 않았습니다.",
                StatusCodes.Status409Conflict => "현재 상태와 충돌합니다. 최신 상태를 확인해 주세요.",
                StatusCodes.Status429TooManyRequests => "요청이 너무 많습니다. 잠시 기다린 뒤 다시 시도해 주세요.",
                StatusCodes.Status503ServiceUnavailable => "API가 일시적으로 응답할 수 없습니다. 잠시 후 같은 요청으로 재시도해 주세요.",
                StatusCodes.Status400BadRequest => "입력값을 확인해 주세요.",
                _ => "작업을 완료하지 못했습니다. 대상 상태와 입력값을 확인해 주세요."
            };
        }
    }
}
