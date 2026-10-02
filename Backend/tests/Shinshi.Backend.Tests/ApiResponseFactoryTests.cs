using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

using Shinshi.Backend;
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests
{
    /// <summary> 응답 팩터리와 서비스 상태 컨트롤러의 실제 반환 DTO를 직접 검사한다 </summary>
    public sealed class ApiResponseFactoryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TEST_REVISION    = 9007199254740993L;
        private const long TEST_SERVER_TIME = 1700000000000L;
        private const string TEST_BUILD     = "test-build";
        private const string TEST_NOTICE    = "test notice";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 작업 상태마다 HTTP 상태를 고르고 기존 결과 DTO를 그대로 반환한다 </summary>
        [Theory]
        [InlineData(ContractCodes.OperationStatus.PENDING, ContractCodes.RejectReason.NONE, StatusCodes.Status202Accepted)]
        [InlineData(ContractCodes.OperationStatus.COMMITTED, ContractCodes.RejectReason.NONE, StatusCodes.Status200OK)]
        [InlineData(ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, StatusCodes.Status409Conflict)]
        public void FromOperation_KnownStatus_ReturnsMappedStatusAndOriginalDto(int status, int reasonCode, int httpStatus)
        {
            OperationResult operation = new OperationResult("operation-placeholder", status, reasonCode,
                TEST_REVISION, TEST_SERVER_TIME, "receipt-placeholder");

            ObjectResult response = ApiResponseFactory.FromOperation(operation);

            Assert.Equal(httpStatus, response.StatusCode);
            Assert.Same(operation, response.Value);
            OperationResult result = Assert.IsType<OperationResult>(response.Value);
            Assert.Equal(status, result.Status);
            Assert.Equal(reasonCode, result.ReasonCode);
            Assert.Equal(TEST_REVISION, result.CommittedRevision);
            Assert.Equal(TEST_SERVER_TIME, result.ServerTimeUnixMs);
        }

        /// <summary> 일반 거절은 사유와 빌드 신원을 보존하고 주입된 시각을 사용한다 </summary>
        [Fact]
        public void Reject_FixedClock_ReturnsApiRejectionWithReasonBuildAndTime()
        {
            ObjectResult response = ApiResponseFactory.Reject(StatusCodes.Status503ServiceUnavailable,
                ContractCodes.RejectReason.MAINTENANCE, TEST_BUILD, new FixedTimeProvider());

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
            ApiRejection rejection = Assert.IsType<ApiRejection>(response.Value);
            Assert.Equal(ContractCodes.RejectReason.MAINTENANCE, rejection.ReasonCode);
            Assert.Equal(TEST_BUILD, rejection.BuildId);
            Assert.Equal(TEST_SERVER_TIME, rejection.ServerTimeUnixMs);
        }

        /// <summary> 서비스 상태 조회는 설정된 상태와 고정된 서버 시각을 실제 DTO로 반환한다 </summary>
        [Fact]
        public async Task Get_ConfiguredServiceState_ReturnsConfiguredSnapshotAndFixedTime()
        {
            ServiceCollection services = new ServiceCollection();
            services.Configure<ServiceStateOptions>(options =>
            {
                options.Maintenance      = false;
                options.Notice           = TEST_NOTICE;
                options.DisabledFeatures = new[] { "departure" };
                options.AllowedBuildIds  = new[] { TEST_BUILD };
            });
            using ServiceProvider provider = services.BuildServiceProvider();
            ServiceStateController controller = new ServiceStateController(
                provider.GetRequiredService<IOptionsMonitor<ServiceStateOptions>>(), new FixedTimeProvider());

            ActionResult<ServiceStateResponse> response = await controller.Get(CancellationToken.None);

            Assert.Null(response.Result);
            ServiceStateResponse state = Assert.IsType<ServiceStateResponse>(response.Value);
            Assert.False(state.Maintenance);
            Assert.Equal(TEST_NOTICE, state.Notice);
            Assert.Equal(new[] { "departure" }, state.DisabledFeatures);
            Assert.Equal(new[] { TEST_BUILD }, state.AllowedBuildIds);
            Assert.Equal(TEST_SERVER_TIME, state.ServerTimeUnixMs);
        }

        /// <summary> 설정을 제공하지 않아도 초기 상태는 점검과 빈 허용 빌드 목록을 반환한다 </summary>
        [Fact]
        public async Task Get_DefaultServiceState_ReturnsMaintenanceSnapshotAndFixedTime()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddOptions<ServiceStateOptions>();
            using ServiceProvider provider = services.BuildServiceProvider();
            ServiceStateController controller = new ServiceStateController(
                provider.GetRequiredService<IOptionsMonitor<ServiceStateOptions>>(), new FixedTimeProvider());

            ActionResult<ServiceStateResponse> response = await controller.Get(CancellationToken.None);

            Assert.Null(response.Result);
            ServiceStateResponse state = Assert.IsType<ServiceStateResponse>(response.Value);
            Assert.True(state.Maintenance);
            Assert.Equal("서비스 준비 중", state.Notice);
            Assert.Empty(state.DisabledFeatures);
            Assert.Empty(state.AllowedBuildIds);
            Assert.Equal(TEST_SERVER_TIME, state.ServerTimeUnixMs);
        }

        private sealed class FixedTimeProvider : TimeProvider
        {
            public override DateTimeOffset GetUtcNow()
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(TEST_SERVER_TIME);
            }
        }
    }
}
