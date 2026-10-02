// System
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Patch.Client
{
    /// <summary> HTTP(S) 게시 저장소에서 키를 연다. 키는 base URL 아래 상대 경로로만 붙는다 </summary>
    public sealed class HttpPatchSource : IPatchSource
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly HttpClient _client;
        private readonly Uri        _baseUri;

        /****************************************
        *              Properties
        ****************************************/

        public Uri BaseUri => _baseUri;

        /****************************************
        *                Methods
        ****************************************/

        // HttpClient 수명은 호출자가 관리한다. 런처는 하나를 만들어 계속 쓴다
        public HttpPatchSource(HttpClient client, Uri baseUri)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(baseUri);
            if (!baseUri.IsAbsoluteUri || ((baseUri.Scheme != Uri.UriSchemeHttps) && (baseUri.Scheme != Uri.UriSchemeHttp)))
            {
                throw new ArgumentException("Patch base URI must be an absolute http or https URI.", nameof(baseUri));
            }

            _client = client;

            // "https://cdn/patch"에 상대 키를 붙이면 마지막 세그먼트가 사라지므로 끝에 "/"를 보장한다
            string text = baseUri.AbsoluteUri;
            _baseUri = text.EndsWith(PatchPaths.SEPARATOR) ? baseUri : new Uri(text + PatchPaths.SEPARATOR);
        }

        public async Task<Stream> OpenAsync(string key, CancellationToken cancellationToken)
        {
            Uri uri = BuildUri(key);

            HttpResponseMessage response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                bool isNotFound = response.StatusCode == HttpStatusCode.NotFound;
                response.Dispose();
                throw new PatchSourceException(key, isNotFound, "Patch source returned HTTP status " + status + ".");
            }

            // 본문 스트림을 닫으면 연결이 풀로 돌아간다. 응답 헤더 객체는 따로 붙잡을 자원이 없다
            return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary> 키를 요청 URI로 바꾼다. 검사를 통과하지 못한 키는 PatchSourceException </summary>
        public Uri BuildUri(string key)
        {
            // 키 검사를 먼저 해 "../"나 절대 URL로 base 밖을 요청하지 못하게 한다
            if (!PatchPaths.IsValidRelativePath(key))
            {
                throw new PatchSourceException(key ?? string.Empty, false, "Patch source key is not a valid relative path.");
            }

            StringBuilder relative = new StringBuilder(key.Length);
            foreach (string segment in key.Split(PatchPaths.SEPARATOR))
            {
                if (relative.Length > 0)
                {
                    relative.Append(PatchPaths.SEPARATOR);
                }

                relative.Append(Uri.EscapeDataString(segment));
            }

            return new Uri(_baseUri, relative.ToString());
        }
    }
}
