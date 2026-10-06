// System
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> DB 없이 서비스 상태 스냅샷의 공지 기간 필터, 안전 기본값, 기능 게이트 판정을 검사한다 </summary>
    public sealed class ServiceStateStoreTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CLOSED_DATABASE = "Host=127.0.0.1;Port=1;Username=test;Database=test;Timeout=2";
        private const string FALLBACK_NOTICE = "configured notice";
        private const string REFRESH_FAILED  = "Service state refresh failed";

        // 문서용 대역(TEST-NET-1)이라 연결이 응답 없이 연결 제한 시간까지 걸린다
        private const string UNREACHABLE_DATABASE = "Host=192.0.2.1;Port=5432;Username=test;Database=test;Timeout=10";

        private const int CONCURRENT_READERS         = 8;
        private const int NON_BLOCKING_LIMIT_SECONDS = 2;

        private static readonly DateTimeOffset G_Now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void ToResponse_NoticePeriods_ShowsOnlyVisibleAndLauncherTitle()
        {
            ServiceNoticeRecord gameCritical = Notice("game only", ServiceNotice.TARGET_GAME,
                ServiceNotice.SEVERITY_CRITICAL, G_Now.AddHours(-1), G_Now.AddHours(1));
            ServiceNoticeRecord launcherInfo = Notice("launcher info", ServiceNotice.TARGET_LAUNCHER,
                ServiceNotice.SEVERITY_INFO, G_Now.AddHours(-2), G_Now.AddHours(1));
            ServiceNoticeRecord bothWarning = Notice("both warning", ServiceNotice.TARGET_BOTH,
                ServiceNotice.SEVERITY_WARNING, G_Now.AddMinutes(-5), G_Now.AddMinutes(5));
            ServiceNoticeRecord scheduled = Notice("scheduled", ServiceNotice.TARGET_BOTH,
                ServiceNotice.SEVERITY_CRITICAL, G_Now.AddMinutes(1), G_Now.AddHours(1));
            ServiceNoticeRecord expired = Notice("expired", ServiceNotice.TARGET_BOTH,
                ServiceNotice.SEVERITY_CRITICAL, G_Now.AddHours(-2), G_Now);
            ServiceStateSnapshot snapshot = new ServiceStateSnapshot(false, null, new[] { "b1" }, 3,
                EServiceStateSource.Database, string.Empty,
                new[] { launcherInfo, expired, scheduled, gameCritical, bothWarning });

            ServiceStateResponse response = snapshot.ToResponse(G_Now);

            Assert.Equal(new[] { "game only", "both warning", "launcher info" },
                response.Notices.Select(notice => notice.Title));
            Assert.Equal("both warning", response.Notice);
            Assert.Equal(G_Now.AddMinutes(5).ToUnixTimeMilliseconds(), response.Notices[1].EndsAtUnixMs);
            Assert.Equal("scheduled", snapshot.ToResponse(G_Now.AddMinutes(2)).Notices[0].Title);
        }

        [Fact]
        public void ToResponse_NoLauncherNotice_UsesSourceFallback()
        {
            ServiceStateSnapshot configured = new ServiceStateSnapshot(true, null, null, 0,
                EServiceStateSource.Configuration, FALLBACK_NOTICE, null);
            ServiceStateSnapshot database = new ServiceStateSnapshot(true, null, null, 1, EServiceStateSource.Database,
                string.Empty, new[] { Notice("game", ServiceNotice.TARGET_GAME, ServiceNotice.SEVERITY_INFO,
                    G_Now.AddHours(-1), G_Now.AddHours(1)) });

            Assert.Equal(FALLBACK_NOTICE, configured.ToResponse(G_Now).Notice);
            Assert.Empty(configured.ToResponse(G_Now).Notices);
            Assert.Equal(string.Empty, database.ToResponse(G_Now).Notice);
            Assert.Single(database.ToResponse(G_Now).Notices);
        }

        [Fact]
        public async Task Current_DatabaseNeverRead_FallsBackToMaintenance()
        {
            await using NpgsqlDataSource closed = NpgsqlDataSource.Create(CLOSED_DATABASE);
            ServiceStateStore store = new ServiceStateStore(new FixedOptions<ServiceStateOptions>(new ServiceStateOptions
            {
                Maintenance     = false,
                Notice          = FALLBACK_NOTICE,
                AllowedBuildIds = new[] { "b1" }
            }), TimeProvider.System, NullLogger<ServiceStateStore>.Instance, new PostgresServiceStateRepository(closed));

            ServiceStateSnapshot current = store.Current;
            ServiceStateSnapshot again = await store.GetAsync();

            Assert.True(store.UsesDatabase);
            Assert.True(current.Maintenance);
            Assert.Equal(EServiceStateSource.Unavailable, current.Source);
            Assert.Empty(current.AllowedBuildIds);
            Assert.Equal(FALLBACK_NOTICE, current.ToResponse(G_Now).Notice);
            Assert.True(again.Maintenance);
        }

        [Fact]
        public async Task GetAsync_ConcurrentFirstReadsAgainstFailingDatabase_ReadsOnce()
        {
            await using NpgsqlDataSource closed = NpgsqlDataSource.Create(CLOSED_DATABASE);
            RecordingLoggerProvider logs = new RecordingLoggerProvider();
            ServiceStateStore store = new ServiceStateStore(new FixedOptions<ServiceStateOptions>(new ServiceStateOptions
            {
                Maintenance    = false,
                RefreshSeconds = ServiceStateOptions.MAX_REFRESH_SECONDS
            }), TimeProvider.System, logs.CreateLogger<ServiceStateStore>(), new PostgresServiceStateRepository(closed));

            ServiceStateSnapshot[] results = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_READERS)
                .Select(_ => store.GetAsync().AsTask()));

            // 기다리던 요청은 락을 얻은 뒤 앞선 시도가 주기 안이라 다시 읽지 않는다
            Assert.All(results, result => Assert.Equal(EServiceStateSource.Unavailable, result.Source));
            Assert.Equal(1, logs.Count(LogLevel.Warning, REFRESH_FAILED));
        }

        [Fact]
        public void Current_NeverReadAndDatabaseSlow_ReturnsUnavailableWithoutWaiting()
        {
            using NpgsqlDataSource unreachable = NpgsqlDataSource.Create(UNREACHABLE_DATABASE);
            ServiceStateStore store = new ServiceStateStore(new FixedOptions<ServiceStateOptions>(new ServiceStateOptions
            {
                Maintenance = false
            }), TimeProvider.System, NullLogger<ServiceStateStore>.Instance,
                new PostgresServiceStateRepository(unreachable));
            Stopwatch watch = Stopwatch.StartNew();

            ServiceStateSnapshot current = store.Current;

            watch.Stop();
            Assert.Equal(EServiceStateSource.Unavailable, current.Source);
            Assert.True(current.Maintenance);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(NON_BLOCKING_LIMIT_SECONDS));
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 0)]
        public async Task StartupService_DatabaseModeWithoutMaintenanceSetting_WarnsOnce(bool configured, int expected)
        {
            await using NpgsqlDataSource closed = NpgsqlDataSource.Create(CLOSED_DATABASE);
            RecordingLoggerProvider logs = new RecordingLoggerProvider();
            ServiceStateStore store = new ServiceStateStore(new FixedOptions<ServiceStateOptions>(
                new ServiceStateOptions()), TimeProvider.System, logs.CreateLogger<ServiceStateStore>(),
                new PostgresServiceStateRepository(closed));
            Dictionary<string, string?> values = new Dictionary<string, string?>();
            if (configured)
            {
                values[ServiceStateStartupService.MAINTENANCE_KEY] = "false";
            }
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            using ServiceStateStartupService service = new ServiceStateStartupService(store, configuration,
                logs.CreateLogger<ServiceStateStartupService>());

            await service.StartAsync(CancellationToken.None);
            await service.ExecuteTask!;
            await service.StopAsync(CancellationToken.None);

            Assert.Equal(expected, logs.Count(LogLevel.Warning, ServiceStateStartupService.MAINTENANCE_KEY));

            // 예열이 첫 읽기를 이미 시도했으므로 주기 안의 첫 요청은 DB를 다시 읽지 않는다
            Assert.Equal(1, logs.Count(LogLevel.Warning, REFRESH_FAILED));
            Assert.Equal(EServiceStateSource.Unavailable, (await store.GetAsync()).Source);
            Assert.Equal(1, logs.Count(LogLevel.Warning, REFRESH_FAILED));
        }

        [Fact]
        public void Constructor_RefreshSecondsOutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ServiceStateStore.FromOptions(
                new FixedOptions<ServiceStateOptions>(new ServiceStateOptions { RefreshSeconds = 0 })));
        }

        [Theory]
        [InlineData(true, new string[0], GameSessionCodes.Mode.NORMAL, ContractCodes.RejectReason.MAINTENANCE)]
        [InlineData(false, new[] { ServiceFeatureKeys.DUNGEON_ENTRY }, GameSessionCodes.Mode.NORMAL,
            ContractCodes.RejectReason.FEATURE_DISABLED)]
        [InlineData(false, new[] { ServiceFeatureKeys.DUNGEON_ENTRY }, GameSessionCodes.Mode.BOSS,
            ContractCodes.RejectReason.FEATURE_DISABLED)]
        [InlineData(false, new[] { ServiceFeatureKeys.BOSS_ENTRY }, GameSessionCodes.Mode.BOSS,
            ContractCodes.RejectReason.FEATURE_DISABLED)]
        [InlineData(false, new[] { ServiceFeatureKeys.BOSS_ENTRY }, GameSessionCodes.Mode.NORMAL,
            ContractCodes.RejectReason.NONE)]
        [InlineData(false, new[] { ServiceFeatureKeys.AUCTION }, GameSessionCodes.Mode.BOSS,
            ContractCodes.RejectReason.NONE)]
        public async Task CheckNewSortie_StateAndMode_ReturnsReason(bool maintenance, string[] disabled, int mode,
            int expected)
        {
            ServiceFeatureGate gate = new ServiceFeatureGate(ServiceStateStore.FromOptions(
                new FixedOptions<ServiceStateOptions>(new ServiceStateOptions
                {
                    Maintenance      = maintenance,
                    DisabledFeatures = disabled
                })));

            Assert.Equal(expected, await gate.CheckNewSortieAsync(mode));
        }

        [Fact]
        public async Task FeatureGate_KnownAndUnknownKeys_ReportOrThrow()
        {
            ServiceFeatureGate gate = new ServiceFeatureGate(ServiceStateStore.FromOptions(
                new FixedOptions<ServiceStateOptions>(new ServiceStateOptions
                {
                    Maintenance      = true,
                    DisabledFeatures = new[] { ServiceFeatureKeys.AUCTION }
                })));

            Assert.True(gate.IsMaintenance);
            Assert.True(gate.IsDisabled(ServiceFeatureKeys.AUCTION));
            Assert.False(await gate.IsDisabledAsync(ServiceFeatureKeys.CRAFTING));
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED,
                await gate.CheckFeatureAsync(ServiceFeatureKeys.AUCTION));
            Assert.Throws<ArgumentException>(() => gate.IsDisabled("Auction"));
            Assert.Equal(10, ServiceFeatureKeys.All.Count);
        }

        private static ServiceNoticeRecord Notice(string title, string target, int severity, DateTimeOffset startsAt,
            DateTimeOffset endsAt)
        {
            return new ServiceNoticeRecord(Guid.NewGuid(), title, title + " body", target, severity, startsAt, endsAt,
                Guid.NewGuid(), startsAt, null, null);
        }
    }
}
