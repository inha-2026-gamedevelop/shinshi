// System
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 보낸 요청을 기록하고 정해 둔 응답을 돌려주는 HTTP 핸들러. DB 없이 명령줄 도구의 요청 모양과 오류 처리를 본다 </summary>
    internal sealed class StubHttpHandler : HttpMessageHandler
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly List<StubHttpHandler.Request> _requests;
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        /****************************************
        *                Methods
        ****************************************/

        public StubHttpHandler(List<StubHttpHandler.Request> requests, Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _requests = requests;
            _respond  = respond;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            bool hasToken = request.Headers.Contains(ApiRoutes.AUTHORIZATION_HEADER);
            _requests.Add(new Request(request.Method, request.RequestUri!, body, hasToken));
            return _respond(request);
        }

        /// <summary> 기록된 요청 하나 </summary>
        public sealed class Request
        {
            /****************************************
            *              Properties
            ****************************************/

            public HttpMethod Method { get; }
            public Uri Uri { get; }
            public string Body { get; }
            public bool HasToken { get; }

            /****************************************
            *                Methods
            ****************************************/

            public Request(HttpMethod method, Uri uri, string body, bool hasToken)
            {
                Method   = method;
                Uri      = uri;
                Body     = body;
                HasToken = hasToken;
            }
        }
    }
}
