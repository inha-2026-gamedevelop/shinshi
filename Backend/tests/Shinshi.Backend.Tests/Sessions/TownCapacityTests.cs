// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 마을 입장권 발급의 인원 기반 선택과 정원 확보, heartbeat 정원 보고, DB 풀 크기 설정, 하위 호환 계약을 검사한다 </summary>
    public sealed class TownCapacityTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CONTENT        = "0.1.3";
        private const int    TOWN_PORT      = 7777;
        private const int    SERVER_MINUTES = 15;
        private const string BOOT_ID        = "11111111-2222-3333-4444-555555555555";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 인원이 적은 마을을 고르고, 정원이 찬 마을은 건너뛰며, 모두 차면 NO_CAPACITY로 거절한다 </summary>
        [PostgresFact]
        public async Task IssueTownJoin_PicksLeastUsedBelowCapacity_ThenRejectsWhenFull()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            Guid first = await fixture.AddTownAsync(2);
            Guid second = await fixture.AddTownAsync(2);
            Guid lower = first.CompareTo(second) < 0 ? first : second;
            Guid higher = lower == first ? second : first;

            List<string> chosen = new List<string>();
            for (int index = 0; index < 4; ++index)
            {
                AuthValidationOutcome<JoinTicket> outcome = await fixture.IssueAsync(await fixture.AddPlayerAsync());
                Assert.True(outcome.IsSuccess);
                chosen.Add(outcome.Identity!.InstanceId);
            }
            AuthValidationOutcome<JoinTicket> full = await fixture.IssueAsync(await fixture.AddPlayerAsync());

            // 사용량이 같으면 instance_id가 작은 쪽부터, 그 뒤에는 한쪽으로 쏠리지 않고 번갈아 채운다
            Assert.Equal(lower.ToString("D"), chosen[0]);
            Assert.Equal(higher.ToString("D"), chosen[1]);
            Assert.Equal(2, chosen.Count(id => id == lower.ToString("D")));
            Assert.Equal(2, chosen.Count(id => id == higher.ToString("D")));
            Assert.False(full.IsSuccess);
            Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY, full.ReasonCode);
        }

        /// <summary> 동시 발급이 몰려도 정원을 넘겨 발급하지 않는다 </summary>
        [PostgresFact]
        public async Task IssueTownJoin_ConcurrentIssuance_NeverExceedsCapacity()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            const int CAPACITY = 3;
            const int PLAYERS  = 10;
            Guid town = await fixture.AddTownAsync(CAPACITY);
            List<PlayerAuthIdentity> players = new List<PlayerAuthIdentity>();
            for (int index = 0; index < PLAYERS; ++index)
            {
                players.Add(await fixture.AddPlayerAsync());
            }

            AuthValidationOutcome<JoinTicket>[] results = await Task.WhenAll(players.Select(fixture.IssueAsync));

            Assert.True(results.Count(result => result.IsSuccess) <= CAPACITY);
            Assert.All(results.Where(result => !result.IsSuccess),
                result => Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY, result.ReasonCode));
            Assert.Equal(results.Count(result => result.IsSuccess), await fixture.CountUnconsumedTokensAsync(town));
        }

        /// <summary> 정원이 NULL(보고 전, 구버전 서버)이면 기존처럼 제한하지 않는다 </summary>
        [PostgresFact]
        public async Task IssueTownJoin_NullCapacity_IsUnlimited()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            await fixture.AddTownAsync(null);

            for (int index = 0; index < 5; ++index)
            {
                Assert.True((await fixture.IssueAsync(await fixture.AddPlayerAsync())).IsSuccess);
            }
        }

        /// <summary> 소비되지 않은 입장권이 만료되면 슬롯이 돌아온다 </summary>
        [PostgresFact]
        public async Task IssueTownJoin_ExpiredTicket_ReleasesSlot()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            Guid town = await fixture.AddTownAsync(1);
            Assert.True((await fixture.IssueAsync(await fixture.AddPlayerAsync())).IsSuccess);
            PlayerAuthIdentity blocked = await fixture.AddPlayerAsync();
            AuthValidationOutcome<JoinTicket> whileHeld = await fixture.IssueAsync(blocked);

            await fixture.ExecuteAsync("UPDATE auth_join_tokens SET expires_at = clock_timestamp() - interval '1 second' "
                + "WHERE instance_id = @Town;", new { Town = town });
            AuthValidationOutcome<JoinTicket> afterExpiry = await fixture.IssueAsync(blocked);

            Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY, whileHeld.ReasonCode);
            Assert.True(afterExpiry.IsSuccess);
        }

        /// <summary> 이미 접속한 계정이 토큰을 재발급해도 임대와 입장권을 이중으로 세지 않아 정원이 차지 않는다 </summary>
        [PostgresFact]
        public async Task IssueTownJoin_ConnectedAccountsReissuing_AreCountedOnce()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            const int CAPACITY = 4;
            Guid town = await fixture.AddTownAsync(CAPACITY);
            List<PlayerAuthIdentity> connected = new List<PlayerAuthIdentity>();
            for (int index = 0; index < CAPACITY / 2; ++index)
            {
                PlayerAuthIdentity player = await fixture.AddPlayerAsync();
                connected.Add(player);
                await fixture.AddConnectionLeaseAsync(town, player.AccountId);
                Assert.True((await fixture.IssueAsync(player)).IsSuccess);
            }

            // 접속 2명(임대 2 + 재발급 입장권 2)이 슬롯 2개만 차지하므로 신규 2명은 들어오고 그 다음은 거절된다
            AuthValidationOutcome<JoinTicket> third  = await fixture.IssueAsync(await fixture.AddPlayerAsync());
            AuthValidationOutcome<JoinTicket> fourth = await fixture.IssueAsync(await fixture.AddPlayerAsync());
            AuthValidationOutcome<JoinTicket> fifth  = await fixture.IssueAsync(await fixture.AddPlayerAsync());
            AuthValidationOutcome<JoinTicket> again  = await fixture.IssueAsync(connected[0]);

            Assert.True(third.IsSuccess);
            Assert.True(fourth.IsSuccess);
            Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY, fifth.ReasonCode);
            Assert.True(again.IsSuccess);
        }

        /// <summary> heartbeat가 정원과 접속 수를 저장하고, 정원을 생략한 heartbeat는 이전 정원을 유지한다 </summary>
        [PostgresFact]
        public async Task Heartbeat_StoresCapacityAndConnections_OmittedCapacityKeepsPrevious()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Fixture fixture = new Fixture(database.DataSource);
            Guid town = await fixture.AddTownAsync(null);
            PostgresServerPoolRepository pool = new PostgresServerPoolRepository(database.DataSource);

            ServerPoolOutcome reported = await pool.HeartbeatAsync(town, fixture.BootId, 7, 100);
            (int? Capacity, int? Connections) first = await fixture.ReadLoadAsync(town);
            ServerPoolOutcome legacy = await pool.HeartbeatAsync(town, fixture.BootId);
            (int? Capacity, int? Connections) second = await fixture.ReadLoadAsync(town);

            Assert.Equal(EServerPoolStatus.Accepted, reported.Status);
            Assert.Equal(100, first.Capacity);
            Assert.Equal(7, first.Connections);
            Assert.Equal(EServerPoolStatus.Accepted, legacy.Status);
            Assert.Equal(100, second.Capacity);
            Assert.Null(second.Connections);
        }

        /// <summary> 범위를 벗어난 보고값은 저장소가 거절한다 </summary>
        [Fact]
        public async Task Heartbeat_InvalidReport_Throws()
        {
            await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create("Host=localhost;Database=unused");
            PostgresServerPoolRepository pool = new PostgresServerPoolRepository(dataSource);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pool.HeartbeatAsync(Guid.NewGuid(),
                Guid.NewGuid(), -1, 10));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pool.HeartbeatAsync(Guid.NewGuid(),
                Guid.NewGuid(), 1, 0));
        }

        /// <summary> 구버전 서버의 본문(bootId만)은 보고값 없음으로 읽히고, 새 본문은 값이 왕복한다 </summary>
        [Fact]
        public void HeartbeatRequest_LegacyBody_ReadsAsNoReport_NewBodyRoundTrips()
        {
            JsonSerializerSettings settings = ContractJson.CreateSettings();

            ServerHeartbeatRequest legacy = JsonConvert.DeserializeObject<ServerHeartbeatRequest>(
                "{\"bootId\":\"" + BOOT_ID + "\"}", settings)!;
            ServerHeartbeatRequest current = JsonConvert.DeserializeObject<ServerHeartbeatRequest>(
                JsonConvert.SerializeObject(new ServerHeartbeatRequest(BOOT_ID, 12, 100), settings), settings)!;

            Assert.Equal(BOOT_ID, legacy.BootId);
            Assert.Null(legacy.Connections);
            Assert.Null(legacy.Capacity);
            Assert.Equal(12, current.Connections);
            Assert.Equal(100, current.Capacity);
        }

        /// <summary> 풀 크기는 설정 키, 연결 문자열, 기본값 순으로 정해진다 </summary>
        [Fact]
        public void ApplyPoolSize_ConfigThenConnectionStringThenDefault()
        {
            IConfiguration none = new ConfigurationBuilder().Build();
            IConfiguration configured = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Postgres:MaxPoolSize"] = "12" }).Build();
            IConfiguration invalid = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Postgres:MaxPoolSize"] = "0" }).Build();
            const string PLAIN = "Host=db;Database=shinshi";
            const string EXPLICIT = "Host=db;Database=shinshi;Maximum Pool Size=7";

            Assert.Equal(PostgresConnectionConfiguration.DEFAULT_MAX_POOL_SIZE,
                new NpgsqlConnectionStringBuilder(PostgresConnectionConfiguration.ApplyPoolSize(PLAIN, none)).MaxPoolSize);
            Assert.Equal(7, new NpgsqlConnectionStringBuilder(
                PostgresConnectionConfiguration.ApplyPoolSize(EXPLICIT, none)).MaxPoolSize);
            Assert.Equal(12, new NpgsqlConnectionStringBuilder(
                PostgresConnectionConfiguration.ApplyPoolSize(EXPLICIT, configured)).MaxPoolSize);
            Assert.Throws<InvalidOperationException>(() => PostgresConnectionConfiguration.ApplyPoolSize(PLAIN, invalid));
        }

        /****************************************
        *               Fixture
        ****************************************/

        private sealed class Fixture
        {
            private readonly NpgsqlDataSource _dataSource;
            private readonly AuthTokenOptions _options = Shinshi.Backend.Tests.Auth.AuthTokenHasherTests.CreateOptions();
            private readonly PostgresAuthRepository _auth;
            private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Guid> _characters = new();

            public Guid BootId { get; } = Guid.NewGuid();

            public Fixture(NpgsqlDataSource dataSource)
            {
                _dataSource = dataSource;
                _auth       = new PostgresAuthRepository(dataSource, _options);
            }

            public async Task<Guid> AddTownAsync(int? capacity)
            {
                Guid town = Guid.NewGuid();
                await ExecuteAsync(
                    """
                    INSERT INTO server_instances(instance_id,role,address,port,build_id,content_version,state,boot_id,capacity)
                        VALUES (@Town,@Role,'127.0.0.1',@Port,@Content,@Content,'Idle',@Boot,@Capacity);
                    """, new { Town = town, Role = ContractCodes.ServerRole.TOWN, Port = TOWN_PORT, Content = CONTENT,
                        Boot = BootId, Capacity = capacity });
                await _auth.IssueServerLeaseAsync(Guid.NewGuid(), town, ContractCodes.ServerRole.TOWN,
                    TimeSpan.FromMinutes(SERVER_MINUTES));
                return town;
            }

            public async Task<PlayerAuthIdentity> AddPlayerAsync()
            {
                Guid account = Guid.NewGuid();
                Guid character = Guid.NewGuid();
                await ExecuteAsync(
                    """
                    INSERT INTO accounts(account_id) VALUES (@Account);
                    INSERT INTO characters(character_id,account_id,class_id) VALUES (@Character,@Account,@Class);
                    INSERT INTO account_wallets(account_id) VALUES (@Account);
                    """, new { Account = account, Character = character, Class = ContractCodes.PlayerClass.WARRIOR });
                IssuedAuthToken invite = await _auth.IssueInviteAsync(account);
                AuthExchangeResponse login = (await _auth.ExchangeInviteAsync(invite.Token)).Response!;
                PlayerAuthIdentity identity = (await _auth.ValidateAccessAsync(login.AccessToken)).Identity!;
                _characters[identity.AccountId] = character;
                return identity;
            }

            public Task<AuthValidationOutcome<JoinTicket>> IssueAsync(PlayerAuthIdentity identity)
            {
                return _auth.IssueTownJoinTokenAsync(identity, _characters[identity.AccountId]);
            }

            public async Task ExecuteAsync(string sql, object parameters)
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
                await connection.ExecuteAsync(sql, parameters);
            }

            public Task AddConnectionLeaseAsync(Guid town, Guid accountId)
            {
                return ExecuteAsync("INSERT INTO account_leases(account_id,lease_id,instance_id,role,expires_at) "
                    + "VALUES (@Account,@Lease,@Town,@Role,clock_timestamp() + interval '10 minutes');",
                    new { Account = accountId, Lease = Guid.NewGuid(), Town = town, Role = ContractCodes.ServerRole.TOWN });
            }

            public async Task<long> CountUnconsumedTokensAsync(Guid town)
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
                return await connection.QuerySingleAsync<long>("SELECT count(*) FROM auth_join_tokens "
                    + "WHERE instance_id = @Town AND consumed_at IS NULL AND revoked_at IS NULL;", new { Town = town });
            }

            public async Task<(int? Capacity, int? Connections)> ReadLoadAsync(Guid town)
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
                dynamic row = await connection.QuerySingleAsync(
                    "SELECT capacity AS \"Capacity\", reported_connections AS \"Connections\" FROM server_instances "
                    + "WHERE instance_id = @Town;", new { Town = town });
                return ((int?)row.Capacity, (int?)row.Connections);
            }
        }
    }
}
