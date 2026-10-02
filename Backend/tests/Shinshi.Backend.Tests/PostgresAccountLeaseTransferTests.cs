// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Backend.Tests
{
    /// <summary> 기존 소유권을 보존하며 계정 임대 이관의 성공, 거절과 동시 경합을 검사한다 </summary>
    public sealed class PostgresAccountLeaseTransferTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int TEST_PORT             = 7777;
        private const int LEASE_DURATION_MINUTES = 1;
        private const int CONCURRENT_TRANSFERS   = 2;
        private const int OTHER_ACCOUNT_COUNT    = 2;
        private const int TIMEOUT_SECONDS        = 30;
        private const string LEASE_SNAPSHOT_SQL  = """
            SELECT account_id AS "AccountId", lease_id AS "LeaseId", instance_id AS "InstanceId",
                role AS "Role", expires_at AS "ExpiresAt"
            FROM account_leases WHERE account_id = @AccountId;
            """;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 마을에서 던전으로 원자 이관한 뒤 이전 연결은 새 소유권을 갱신하거나 해제하지 못한다 </summary>
        [PostgresFact]
        public async Task TransferAsync_ActiveOwner_TransfersSingleLeaseAndRejectsPreviousOwner()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            Guid sourceInstanceId = Guid.NewGuid();
            Guid targetInstanceId = Guid.NewGuid();
            Guid existingLeaseId = Guid.NewGuid();
            Guid newLeaseId = Guid.NewGuid();
            await SeedAccountAndServersAsync(database, accountId, sourceInstanceId, targetInstanceId);
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(database.DataSource);
            await AcquireSourceLeaseAsync(leases, accountId, existingLeaseId, sourceInstanceId);

            AccountLeaseOutcome result = await leases.TransferAsync(accountId, existingLeaseId, sourceInstanceId,
                newLeaseId, targetInstanceId, ContractCodes.ServerRole.DUNGEON, LeaseDuration());

            Assert.Equal(EAccountLeaseStatus.Transferred, result.Status);
            AccountLease transferred = Assert.IsType<AccountLease>(result.Lease);
            Assert.Equal(accountId, transferred.AccountId);
            Assert.Equal(newLeaseId, transferred.LeaseId);
            Assert.Equal(targetInstanceId, transferred.InstanceId);
            Assert.Equal(ContractCodes.ServerRole.DUNGEON, transferred.Role);
            LeaseSnapshot stored = await ReadLeaseAsync(database, accountId);
            AssertLease(transferred, stored);
            Assert.Equal(1L, await CountLeasesAsync(database));
            Assert.False(await leases.RenewAsync(accountId, existingLeaseId, sourceInstanceId, LeaseDuration()));
            Assert.False(await leases.ReleaseAsync(accountId, existingLeaseId, sourceInstanceId));
            AssertSnapshot(stored, await ReadLeaseAsync(database, accountId));
            Assert.True(await leases.RenewAsync(accountId, newLeaseId, targetInstanceId, LeaseDuration()));
        }

        /// <summary> 만료된 기존 임대는 새 임대로 되살리지 않고 저장된 이전 소유권을 그대로 남긴다 </summary>
        [PostgresFact]
        public async Task TransferAsync_ExpiredExistingLease_RejectsWithoutChangingOwner()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            Guid sourceInstanceId = Guid.NewGuid();
            Guid targetInstanceId = Guid.NewGuid();
            Guid existingLeaseId = Guid.NewGuid();
            await SeedAccountAndServersAsync(database, accountId, sourceInstanceId, targetInstanceId);
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(database.DataSource);
            await AcquireSourceLeaseAsync(leases, accountId, existingLeaseId, sourceInstanceId);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                "UPDATE account_leases SET expires_at = clock_timestamp() - interval '1 second' WHERE account_id = @AccountId;",
                new { AccountId = accountId });
            LeaseSnapshot before = await ReadLeaseAsync(database, accountId);

            AccountLeaseOutcome result = await leases.TransferAsync(accountId, existingLeaseId, sourceInstanceId,
                Guid.NewGuid(), targetInstanceId, ContractCodes.ServerRole.DUNGEON, LeaseDuration());

            Assert.Equal(EAccountLeaseStatus.LeaseExpired, result.Status);
            Assert.Null(result.Lease);
            AssertSnapshot(before, await ReadLeaseAsync(database, accountId));
            Assert.Equal(1L, await CountLeasesAsync(database));
        }

        /// <summary> 다른 계정의 임대 ID를 제출하면 두 계정의 기존 소유권을 모두 보존한다 </summary>
        [PostgresFact]
        public async Task TransferAsync_OtherAccountsLease_RejectsOwnershipMismatchWithoutChangingEitherOwner()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid ownerAccountId = Guid.NewGuid();
            Guid otherAccountId = Guid.NewGuid();
            Guid sourceInstanceId = Guid.NewGuid();
            Guid targetInstanceId = Guid.NewGuid();
            Guid ownerLeaseId = Guid.NewGuid();
            await SeedAccountAndServersAsync(database, ownerAccountId, sourceInstanceId, targetInstanceId);
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO accounts (account_id) VALUES (@AccountId);",
                new { AccountId = otherAccountId });
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(database.DataSource);
            await AcquireSourceLeaseAsync(leases, ownerAccountId, ownerLeaseId, sourceInstanceId);
            await AcquireSourceLeaseAsync(leases, otherAccountId, Guid.NewGuid(), sourceInstanceId);
            LeaseSnapshot ownerBefore = await ReadLeaseAsync(database, ownerAccountId);
            LeaseSnapshot otherBefore = await ReadLeaseAsync(database, otherAccountId);

            AccountLeaseOutcome result = await leases.TransferAsync(otherAccountId, ownerLeaseId, sourceInstanceId,
                Guid.NewGuid(), targetInstanceId, ContractCodes.ServerRole.DUNGEON, LeaseDuration());

            Assert.Equal(EAccountLeaseStatus.LeaseOwnershipMismatch, result.Status);
            Assert.Null(result.Lease);
            AssertSnapshot(ownerBefore, await ReadLeaseAsync(database, ownerAccountId));
            AssertSnapshot(otherBefore, await ReadLeaseAsync(database, otherAccountId));
            Assert.Equal((long)OTHER_ACCOUNT_COUNT, await CountLeasesAsync(database));
        }

        /// <summary> 같은 기존 소유권의 동시 이관에서 한 요청만 승인하고 진 요청과 이전 연결은 승자의 임대를 바꾸지 못한다 </summary>
        [PostgresFact]
        public async Task TransferAsync_ConcurrentSameOwner_CommitsOneWinnerAndPreservesWinningOwner()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECONDS));
            Guid accountId = Guid.NewGuid();
            Guid sourceInstanceId = Guid.NewGuid();
            Guid targetInstanceId = Guid.NewGuid();
            Guid existingLeaseId = Guid.NewGuid();
            await SeedAccountAndServersAsync(database, accountId, sourceInstanceId, targetInstanceId);
            PostgresAccountLeaseRepository leases = new PostgresAccountLeaseRepository(database.DataSource);
            await AcquireSourceLeaseAsync(leases, accountId, existingLeaseId, sourceInstanceId);
            Guid[] newLeaseIds = Enumerable.Range(0, CONCURRENT_TRANSFERS).Select(_ => Guid.NewGuid()).ToArray();
            TaskCompletionSource<bool> start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<AccountLeaseOutcome> TransferAfterStartAsync(Guid newLeaseId)
            {
                await start.Task.WaitAsync(timeout.Token);
                return await leases.TransferAsync(accountId, existingLeaseId, sourceInstanceId,
                    newLeaseId, targetInstanceId, ContractCodes.ServerRole.DUNGEON, LeaseDuration(), timeout.Token);
            }

            Task<AccountLeaseOutcome>[] pending = newLeaseIds.Select(TransferAfterStartAsync).ToArray();
            start.SetResult(true);
            AccountLeaseOutcome[] results = await Task.WhenAll(pending).WaitAsync(timeout.Token);

            AccountLeaseOutcome winner = Assert.Single(results, result => result.Status == EAccountLeaseStatus.Transferred);
            AccountLeaseOutcome loser = Assert.Single(results, result => result.Status == EAccountLeaseStatus.LeaseOwnershipMismatch);
            AccountLease transferred = Assert.IsType<AccountLease>(winner.Lease);
            Assert.Null(loser.Lease);
            Assert.Contains(transferred.LeaseId, newLeaseIds);
            Assert.Equal(accountId, transferred.AccountId);
            Assert.Equal(targetInstanceId, transferred.InstanceId);
            Assert.Equal(ContractCodes.ServerRole.DUNGEON, transferred.Role);
            Guid losingLeaseId = newLeaseIds.Single(leaseId => leaseId != transferred.LeaseId);
            LeaseSnapshot stored = await ReadLeaseAsync(database, accountId);
            AssertLease(transferred, stored);
            Assert.Equal(1L, await CountLeasesAsync(database));
            Assert.False(await leases.RenewAsync(accountId, existingLeaseId, sourceInstanceId, LeaseDuration(), timeout.Token));
            Assert.False(await leases.ReleaseAsync(accountId, existingLeaseId, sourceInstanceId, timeout.Token));
            Assert.False(await leases.RenewAsync(accountId, losingLeaseId, targetInstanceId, LeaseDuration(), timeout.Token));
            Assert.False(await leases.ReleaseAsync(accountId, losingLeaseId, targetInstanceId, timeout.Token));
            AssertSnapshot(stored, await ReadLeaseAsync(database, accountId));
        }

        private static TimeSpan LeaseDuration()
        {
            return TimeSpan.FromMinutes(LEASE_DURATION_MINUTES);
        }

        private static async Task AcquireSourceLeaseAsync(PostgresAccountLeaseRepository leases,
            Guid accountId, Guid leaseId, Guid instanceId)
        {
            AccountLeaseOutcome acquired = await leases.TryAcquireAsync(accountId, leaseId, instanceId,
                ContractCodes.ServerRole.TOWN, LeaseDuration());
            Assert.Equal(EAccountLeaseStatus.Acquired, acquired.Status);
        }

        private static async Task SeedAccountAndServersAsync(PostgresTestDatabase database,
            Guid accountId, Guid sourceInstanceId, Guid targetInstanceId)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts (account_id) VALUES (@AccountId);
                INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state)
                VALUES (@SourceInstanceId, @TownRole, '127.0.0.1', @Port, 'test-build', 'test-content', 'Idle'),
                    (@TargetInstanceId, @DungeonRole, '127.0.0.1', @Port, 'test-build', 'test-content', 'Idle');
                """, new
                {
                    AccountId = accountId, SourceInstanceId = sourceInstanceId, TargetInstanceId = targetInstanceId,
                    TownRole = ContractCodes.ServerRole.TOWN, DungeonRole = ContractCodes.ServerRole.DUNGEON,
                    Port = TEST_PORT
                });
        }

        private static async Task<LeaseSnapshot> ReadLeaseAsync(PostgresTestDatabase database, Guid accountId)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<LeaseSnapshot>(LEASE_SNAPSHOT_SQL, new { AccountId = accountId });
        }

        private static async Task<long> CountLeasesAsync(PostgresTestDatabase database)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<long>("SELECT count(*) FROM account_leases;");
        }

        private static void AssertLease(AccountLease expected, LeaseSnapshot actual)
        {
            Assert.Equal(expected.AccountId, actual.AccountId);
            Assert.Equal(expected.LeaseId, actual.LeaseId);
            Assert.Equal(expected.InstanceId, actual.InstanceId);
            Assert.Equal(expected.Role, actual.Role);
            Assert.Equal(expected.ExpiresAt.UtcDateTime, actual.ExpiresAt);
        }

        private static void AssertSnapshot(LeaseSnapshot expected, LeaseSnapshot actual)
        {
            Assert.Equal(expected.AccountId, actual.AccountId);
            Assert.Equal(expected.LeaseId, actual.LeaseId);
            Assert.Equal(expected.InstanceId, actual.InstanceId);
            Assert.Equal(expected.Role, actual.Role);
            Assert.Equal(expected.ExpiresAt, actual.ExpiresAt);
        }

        private sealed class LeaseSnapshot
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid AccountId { get; set; }
            public Guid LeaseId { get; set; }
            public Guid InstanceId { get; set; }
            public int Role { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}
