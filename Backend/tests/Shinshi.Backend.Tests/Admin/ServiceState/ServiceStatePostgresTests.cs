// System
using System;
using System.Diagnostics;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 실제 PostgreSQL에서 서비스 상태 캐시의 초기 행, 주기 재조회, 조회 실패 시 마지막 값 유지를 검사한다 </summary>
    public sealed class ServiceStatePostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int REFRESH_SECONDS = 5;
        private const int WAIT_LIMIT_MS   = 5000;
        private const int POLL_DELAY_MS   = 20;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Store_DatabaseRow_ReadsSeedRefreshesOnIntervalAndKeepsLastOnFailure()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            ManualTimeProvider clock = new ManualTimeProvider();
            ServiceStateStore store = new ServiceStateStore(new FixedOptions<ServiceStateOptions>(new ServiceStateOptions
            {
                Maintenance     = false,
                AllowedBuildIds = new[] { "config-build" },
                RefreshSeconds  = REFRESH_SECONDS
            }), clock, NullLogger<ServiceStateStore>.Instance, new PostgresServiceStateRepository(database.DataSource));

            // 0008 초기 행이 설정보다 우선한다: 점검 중, 빈 허용 빌드. 동기 Current는 첫 읽기를 기다리지 않고
            // 백그라운드 읽기만 시작하며, 비동기 경로는 진행 중인 그 읽기를 기다렸다가 DB 값을 준다
            _ = store.Current;
            ServiceStateSnapshot seed = await store.GetAsync();
            Assert.Equal(EServiceStateSource.Database, seed.Source);
            Assert.True(seed.Maintenance);
            Assert.Empty(seed.AllowedBuildIds);
            Assert.Equal(0, seed.Revision);

            await ExecuteAsync(database.DataSource,
                "UPDATE service_state SET maintenance = false, allowed_build_ids = '{db-build}', revision = 1;");
            Assert.True((await store.GetAsync()).Maintenance);

            clock.Now = clock.Now.AddSeconds(REFRESH_SECONDS);
            ServiceStateSnapshot refreshed = await WaitForRevisionAsync(store, 1);
            Assert.False(refreshed.Maintenance);
            Assert.Equal(new[] { "db-build" }, refreshed.AllowedBuildIds);

            // 테이블이 사라져 읽기가 실패해도 마지막 성공 값을 그대로 쓴다
            await ExecuteAsync(database.DataSource, "ALTER TABLE service_state RENAME TO service_state_hidden;");
            clock.Now = clock.Now.AddSeconds(REFRESH_SECONDS);
            await store.RefreshAsync();
            Assert.Same(refreshed, store.Current);
            await ExecuteAsync(database.DataSource, "ALTER TABLE service_state_hidden RENAME TO service_state;");

            await ExecuteAsync(database.DataSource, "UPDATE service_state SET maintenance = true, revision = 2;");
            await store.RefreshAsync();
            Assert.True(store.Current.Maintenance);
            Assert.Equal(2, store.Current.Revision);
        }

        private static async Task<ServiceStateSnapshot> WaitForRevisionAsync(ServiceStateStore store, long revision)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < WAIT_LIMIT_MS)
            {
                // 첫 호출은 주기 만료를 보고 백그라운드 읽기를 시작하고 이전 값을 준다
                ServiceStateSnapshot snapshot = await store.GetAsync();
                if (snapshot.Revision == revision)
                {
                    return snapshot;
                }
                await Task.Delay(POLL_DELAY_MS);
            }

            throw new TimeoutException("The service state cache did not refresh after its interval.");
        }

        private static async Task ExecuteAsync(NpgsqlDataSource dataSource, string sql)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(sql);
        }
    }
}
