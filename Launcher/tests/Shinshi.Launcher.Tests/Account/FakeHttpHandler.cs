// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Launcher.Tests.Account
{
    /// <summary> 요청을 기록하고 경로별 응답을 돌려주는 가짜 HTTP 처리기 </summary>
    public sealed class FakeHttpHandler : HttpMessageHandler
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string BASE_URI = "https://api.example.test/";

        private const string JSON_MEDIA_TYPE = "application/json";

        private readonly Func<RecordedRequest, Task<HttpResponseMessage>> _respond;
        private readonly List<RecordedRequest>                            _requests = new List<RecordedRequest>();
        private readonly object                                           _lock     = new object();

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_lock)
                {
                    return _requests.ToArray();
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public FakeHttpHandler(Func<RecordedRequest, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public int Count(string route)
        {
            int count = 0;
            foreach (RecordedRequest request in Requests)
            {
                if (request.Path == route)
                {
                    ++count;
                }
            }

            return count;
        }

        public static HttpResponseMessage Json(HttpStatusCode status, object body)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonConvert.SerializeObject(body, ContractJson.CreateSettings()), Encoding.UTF8,
                    JSON_MEDIA_TYPE)
            };
        }

        public static HttpResponseMessage Text(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, JSON_MEDIA_TYPE) };
        }

        public static HttpResponseMessage Reject(HttpStatusCode status, int reasonCode)
        {
            return Json(status, new ApiRejection(reasonCode, string.Empty, 0));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = (request.Content == null) ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            string? authorization = request.Headers.TryGetValues(ApiRoutes.AUTHORIZATION_HEADER, out IEnumerable<string>? values)
                ? string.Join(",", values)
                : null;
            RecordedRequest recorded = new RecordedRequest(request.Method, request.RequestUri!, body, authorization, cancellationToken);
            lock (_lock)
            {
                _requests.Add(recorded);
            }

            return await _respond(recorded);
        }
    }

    /// <summary> FakeHttpHandler가 받은 요청 하나 </summary>
    public sealed class RecordedRequest
    {
        /****************************************
        *              Properties
        ****************************************/

        public HttpMethod        Method            { get; }
        public Uri               Uri               { get; }
        public string            Path              => Uri.AbsolutePath;
        public string            Body              { get; }
        public string?           Authorization     { get; }
        public CancellationToken CancellationToken { get; }

        /****************************************
        *                Methods
        ****************************************/

        public RecordedRequest(HttpMethod method, Uri uri, string body, string? authorization, CancellationToken cancellationToken)
        {
            Method            = method;
            Uri               = uri;
            Body              = body;
            Authorization     = authorization;
            CancellationToken = cancellationToken;
        }

        public T? ReadBody<T>() where T : class { return JsonConvert.DeserializeObject<T>(Body, ContractJson.CreateSettings()); }
    }
}
