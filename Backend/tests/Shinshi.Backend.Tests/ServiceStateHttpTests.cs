using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

using Shinshi.Backend;
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests
{
    /// <summary> TestServer에서 서비스 상태와 MVC 거절 응답의 JSON 계약을 검사한다 </summary>
    public sealed class ServiceStateHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TEST_BUILD  = "test-build";
        private const string TEST_NOTICE = "test notice";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서비스 상태는 인증 없이 읽는 점검 스냅샷이며 DTO 키를 유지한다 </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Get_ServiceStateConfigured_ReturnsContractSnapshot(bool maintenance)
        {
            using BackendApplicationFactory factory = new BackendApplicationFactory(maintenance);
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            long beforeRequest = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            using HttpResponseMessage response = await client.GetAsync(ApiRoutes.SERVICE_STATE);
            string body = await response.Content.ReadAsStringAsync();
            long afterRequest = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ServiceStateResponse? state = JsonConvert.DeserializeObject<ServiceStateResponse>(body,
                ContractJson.CreateSettings());
            JObject json = JObject.Parse(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
            Assert.NotNull(state);
            Assert.Equal(maintenance, state.Maintenance);
            Assert.Equal(TEST_NOTICE, state.Notice);
            Assert.Equal(new[] { "departure" }, state.DisabledFeatures);
            Assert.Equal(new[] { TEST_BUILD }, state.AllowedBuildIds);
            Assert.InRange(state.ServerTimeUnixMs, beforeRequest, afterRequest);
            Assert.Equal(JTokenType.Integer, json["serverTimeUnixMs"]!.Type);
            Assert.Null(json["Maintenance"]);
            Assert.Null(json["ServerTimeUnixMs"]);
        }

        /// <summary> MVC의 실패 응답도 private 필드와 정수 거절 코드를 그대로 직렬화한다 </summary>
        [Fact]
        public async Task Get_RejectionThroughMvc_PreservesDataMemberAndInt64()
        {
            using BackendApplicationFactory factory = new BackendApplicationFactory(true);
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

            using HttpResponseMessage response = await client.GetAsync(RejectionSerializationController.TEST_ROUTE);
            string body = await response.Content.ReadAsStringAsync();
            JObject json = JObject.Parse(body);
            ApiRejection? rejection = JsonConvert.DeserializeObject<ApiRejection>(body,
                ContractJson.CreateSettings());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.NotNull(rejection);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, rejection.ReasonCode);
            Assert.Equal(TEST_BUILD, rejection.BuildId);
            Assert.Equal(RejectionSerializationController.TEST_TIME, rejection.ServerTimeUnixMs);
            Assert.Equal(JTokenType.Integer, json["reasonCode"]!.Type);
            Assert.Equal(JTokenType.Integer, json["serverTimeUnixMs"]!.Type);
            Assert.Equal("902", json["reasonCode"]!.ToString(Formatting.None));
            Assert.Null(json["ReasonCode"]);
        }

        private sealed class BackendApplicationFactory : WebApplicationFactory<Program>
        {
            private readonly bool _maintenance;

            public BackendApplicationFactory(bool maintenance)
            {
                _maintenance = maintenance;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureServices(services =>
                {
                    services.PostConfigure<ServiceStateOptions>(options =>
                    {
                        options.Maintenance      = _maintenance;
                        options.Notice           = TEST_NOTICE;
                        options.DisabledFeatures = new[] { "departure" };
                        options.AllowedBuildIds  = new[] { TEST_BUILD };
                    });
                    services.AddControllers().AddApplicationPart(typeof(RejectionSerializationController).Assembly);
                });
            }
        }
    }

    /// <summary> 테스트 호스트에서만 MVC 거절 직렬화를 실행한다 </summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class RejectionSerializationController : ControllerBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string TEST_ROUTE = "/__tests/rejection";
        public const long   TEST_TIME  = 9007199254740993L;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 별도의 운영 거절 경로를 요구하지 않고 JSON formatter를 실행한다 </summary>
        [HttpGet(TEST_ROUTE)]
        public ActionResult<ApiRejection> GetRejection()
        {
            return Conflict(new ApiRejection(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT,
                "test-build", TEST_TIME));
        }
    }
}
