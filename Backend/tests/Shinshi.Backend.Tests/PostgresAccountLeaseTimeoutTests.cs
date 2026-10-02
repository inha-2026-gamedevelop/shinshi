// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Backend.Tests
{
    /// <summary> 실제 연결 풀 타임아웃도 PostgreSQL 문장 제한과 같은 재시도 계약으로 처리한다 </summary>
    public sealed class PostgresAccountLeaseTimeoutTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SINGLE_CONNECTION       = 1;
        private const int CONNECTION_TIMEOUT      = 1;
        private const int TEST_TIMEOUT_SECONDS    = 30;
        private const int LEASE_DURATION_MINUTES  = 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 임대 획득의 연결 풀 대기가 소진되면 예외를 노출하지 않고 재시도 가능한 결과를 반환한다 </summary>
        [PostgresFact]
        public async Task TryAcquireAsync_ExhaustedConnectionPool_ReturnsRetryableFailure()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlDataSource singleConnectionPool = CreateSingleConnectionPool(database);
            await using NpgsqlConnection occupied = await singleConnectionPool.OpenConnectionAsync();
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(singleConnectionPool);

            AccountLeaseOutcome result = await leases.TryAcquireAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                ContractCodes.ServerRole.TOWN, LeaseDuration(), timeout.Token);

            Assert.Equal(EAccountLeaseStatus.TransientFailure, result.Status);
            Assert.True(result.IsRetryable);
            Assert.Null(result.Lease);
        }

        /// <summary> 이관의 연결 풀 대기가 소진되어도 획득과 같은 재시도 가능한 결과를 반환한다 </summary>
        [PostgresFact]
        public async Task TransferAsync_ExhaustedConnectionPool_ReturnsRetryableFailure()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlDataSource singleConnectionPool = CreateSingleConnectionPool(database);
            await using NpgsqlConnection occupied = await singleConnectionPool.OpenConnectionAsync();
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(singleConnectionPool);

            AccountLeaseOutcome result = await leases.TransferAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), ContractCodes.ServerRole.DUNGEON, LeaseDuration(), timeout.Token);

            Assert.Equal(EAccountLeaseStatus.TransientFailure, result.Status);
            Assert.True(result.IsRetryable);
            Assert.Null(result.Lease);
        }

        /// <summary> 갱신과 해제의 연결 풀 제한은 일반 소유권 거절과 구분되는 재시도 가능한 예외로 반환한다 </summary>
        [PostgresFact]
        public async Task RenewAndReleaseAsync_ExhaustedConnectionPool_ThrowTypedRetryableFailures()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlDataSource singleConnectionPool = CreateSingleConnectionPool(database);
            await using NpgsqlConnection occupied = await singleConnectionPool.OpenConnectionAsync();
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(singleConnectionPool);
            Guid accountId = Guid.NewGuid();
            Guid leaseId = Guid.NewGuid();
            Guid instanceId = Guid.NewGuid();

            RetryableDatabaseException renewalFailure = await Assert.ThrowsAsync<RetryableDatabaseException>(() =>
                leases.RenewAsync(accountId, leaseId, instanceId, LeaseDuration(), timeout.Token));
            RetryableDatabaseException releaseFailure = await Assert.ThrowsAsync<RetryableDatabaseException>(() =>
                leases.ReleaseAsync(accountId, leaseId, instanceId, timeout.Token));

            AssertTimeout(renewalFailure);
            AssertTimeout(releaseFailure);
        }

        private static NpgsqlDataSource CreateSingleConnectionPool(PostgresTestDatabase database)
        {
            // DataSource.ConnectionString은 비밀번호를 지운 값이라 원래 연결 문자열에서 다시 채운다
            NpgsqlConnectionStringBuilder original = new NpgsqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable(PostgresFactAttribute.CONNECTION_VARIABLE));
            NpgsqlConnectionStringBuilder settings = new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString)
            {
                Password    = original.Password,
                Pooling     = true,
                MinPoolSize = 0,
                MaxPoolSize = SINGLE_CONNECTION,
                Timeout     = CONNECTION_TIMEOUT
            };
            return NpgsqlDataSource.Create(settings.ConnectionString);
        }

        private static TimeSpan LeaseDuration()
        {
            return TimeSpan.FromMinutes(LEASE_DURATION_MINUTES);
        }

        private static void AssertTimeout(RetryableDatabaseException failure)
        {
            Assert.True(failure.IsRetryable);
            Exception original = Assert.IsAssignableFrom<Exception>(failure.InnerException);
            Assert.True(PostgresTransactionPolicy.IsTimeout(original));
        }
    }
}
