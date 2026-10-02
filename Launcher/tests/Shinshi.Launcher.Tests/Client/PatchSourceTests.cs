// System
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Xunit;

// Project
using Shinshi.Patch.Client;

namespace Shinshi.Launcher.Tests.Client
{
    /// <summary> 파일시스템, HTTP 원본의 키 해석과 오류 처리 </summary>
    public sealed class PatchSourceTests : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BASE_URI    = "https://cdn.example.test/patch";
        private const string CHANNEL_KEY = "game/channels/internal.json";
        private const string BODY        = "pointer";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "shinshi-launcher-tests", Guid.NewGuid().ToString("N"));

        /****************************************
        *                Methods
        ****************************************/

        public PatchSourceTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Fact]
        public async Task FileSystemOpenAsync_ExistingKey_ReturnsContent()
        {
            string path = Path.Combine(_root, "game", "channels", "internal.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BODY);
            FileSystemPatchSource source = new FileSystemPatchSource(_root);

            await using Stream stream = await source.OpenAsync(CHANNEL_KEY, CancellationToken.None);
            using StreamReader reader = new StreamReader(stream);

            Assert.Equal(BODY, await reader.ReadToEndAsync());
        }

        [Fact]
        public async Task FileSystemOpenAsync_MissingKey_ThrowsNotFound()
        {
            FileSystemPatchSource source = new FileSystemPatchSource(_root);

            PatchSourceException exception = await Assert.ThrowsAsync<PatchSourceException>(
                () => source.OpenAsync(CHANNEL_KEY, CancellationToken.None));

            Assert.True(exception.IsNotFound);
        }

        [Theory]
        [InlineData("../outside.txt")]
        [InlineData("/etc/passwd")]
        [InlineData("C:/Windows/win.ini")]
        [InlineData("game\\channels\\internal.json")]
        public async Task FileSystemOpenAsync_KeyEscapingRoot_Throws(string key)
        {
            string publishRoot = Path.Combine(_root, "publish");
            Directory.CreateDirectory(publishRoot);
            File.WriteAllText(Path.Combine(_root, "outside.txt"), BODY);
            FileSystemPatchSource source = new FileSystemPatchSource(publishRoot);

            PatchSourceException exception = await Assert.ThrowsAsync<PatchSourceException>(
                () => source.OpenAsync(key, CancellationToken.None));

            Assert.False(exception.IsNotFound);
        }

        [Theory]
        [InlineData(BASE_URI)]
        [InlineData(BASE_URI + "/")]
        public void HttpBuildUri_RelativeKey_AppendsUnderBasePath(string baseUri)
        {
            using HttpClient client = new HttpClient();
            HttpPatchSource source = new HttpPatchSource(client, new Uri(baseUri));

            Uri uri = source.BuildUri(CHANNEL_KEY);

            Assert.Equal(BASE_URI + "/" + CHANNEL_KEY, uri.AbsoluteUri);
        }

        [Theory]
        [InlineData("../secret")]
        [InlineData("https://evil.example.test/x")]
        [InlineData("//evil.example.test/x")]
        [InlineData("a?b=c")]
        public void HttpBuildUri_UnsafeKey_Throws(string key)
        {
            using HttpClient client = new HttpClient();
            HttpPatchSource source = new HttpPatchSource(client, new Uri(BASE_URI));

            Assert.Throws<PatchSourceException>(() => source.BuildUri(key));
        }

        [Fact]
        public void HttpConstructor_NonHttpScheme_Throws()
        {
            using HttpClient client = new HttpClient();

            Assert.Throws<ArgumentException>(() => new HttpPatchSource(client, new Uri("file:///C:/patch/")));
        }

        [Fact]
        public async Task HttpOpenAsync_SuccessResponse_ReturnsBody()
        {
            using HttpClient client = new HttpClient(new StubHandler(HttpStatusCode.OK, BODY));
            HttpPatchSource source = new HttpPatchSource(client, new Uri(BASE_URI));

            await using Stream stream = await source.OpenAsync(CHANNEL_KEY, CancellationToken.None);
            using StreamReader reader = new StreamReader(stream);

            Assert.Equal(BODY, await reader.ReadToEndAsync());
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, true)]
        [InlineData(HttpStatusCode.InternalServerError, false)]
        [InlineData(HttpStatusCode.Forbidden, false)]
        public async Task HttpOpenAsync_ErrorStatus_ThrowsPatchSourceException(HttpStatusCode status, bool isNotFound)
        {
            using HttpClient client = new HttpClient(new StubHandler(status, BODY));
            HttpPatchSource source = new HttpPatchSource(client, new Uri(BASE_URI));

            PatchSourceException exception = await Assert.ThrowsAsync<PatchSourceException>(
                () => source.OpenAsync(CHANNEL_KEY, CancellationToken.None));

            Assert.Equal(isNotFound, exception.IsNotFound);
        }

        [Fact]
        public async Task HttpOpenAsync_Cancelled_ThrowsOperationCanceled()
        {
            using HttpClient client = new HttpClient(new StubHandler(HttpStatusCode.OK, BODY));
            HttpPatchSource source = new HttpPatchSource(client, new Uri(BASE_URI));
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.OpenAsync(CHANNEL_KEY, cancellation.Token));
        }

        // 네트워크 없이 고정 응답을 돌려주는 처리기
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string         _body;

            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body   = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HttpResponseMessage response = new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8)
                };

                return Task.FromResult(response);
            }
        }
    }
}
