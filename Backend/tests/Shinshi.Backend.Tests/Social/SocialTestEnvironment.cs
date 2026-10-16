// System
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Sessions;
using Shinshi.Data.Social;

namespace Shinshi.Backend.Tests.Social
{
    /// <summary> 소셜 저장소를 격리 스키마에 올린다. 마을 둘(Town, OtherTown)과 던전 하나를 등록하고,
    /// 캐릭터는 이름과 접속 임대(character_id 포함)를 가진다 </summary>
    internal sealed class SocialTestEnvironment : IAsyncDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    TOWN_PORT       = 7801;
        private const int    OTHER_TOWN_PORT = 7802;
        private const int    DUNGEON_PORT    = 7803;
        private const int    LEASE_MINUTES   = 5;
        private const string BUILD           = "social-test";

        private readonly PostgresTestDatabase _database;
        private int _sequence;

        /****************************************
        *              Properties
        ****************************************/

        public NpgsqlDataSource               DataSource { get; }
        public PostgresSocialRepository       Social     { get; }
        public PostgresAccountLeaseRepository Leases     { get; }
        public PostgresServerPoolRepository   Pool       { get; }
        public Guid                           TownId      { get; } = Guid.NewGuid();
        public Guid                           OtherTownId { get; } = Guid.NewGuid();
        public Guid                           DungeonId   { get; } = Guid.NewGuid();
        public SocialCaller                   Town        => new SocialCaller(TownId, ContractCodes.ServerRole.TOWN);
        public SocialCaller                   OtherTown   => new SocialCaller(OtherTownId, ContractCodes.ServerRole.TOWN);
        public SocialCaller                   Dungeon     => new SocialCaller(DungeonId, ContractCodes.ServerRole.DUNGEON);

        /****************************************
        *                Methods
        ****************************************/

        private SocialTestEnvironment(PostgresTestDatabase database)
        {
            _database  = database;
            DataSource = database.DataSource;
            Social     = new PostgresSocialRepository(DataSource);
            Leases     = new PostgresAccountLeaseRepository(DataSource);
            Pool       = new PostgresServerPoolRepository(DataSource);
        }

        public static async Task<SocialTestEnvironment> CreateAsync()
        {
            PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            SocialTestEnvironment environment = new SocialTestEnvironment(database);
            try
            {
                await environment.RegisterAsync(environment.TownId, ContractCodes.ServerRole.TOWN, TOWN_PORT);
                await environment.RegisterAsync(environment.OtherTownId, ContractCodes.ServerRole.TOWN, OTHER_TOWN_PORT);
                await environment.RegisterAsync(environment.DungeonId, ContractCodes.ServerRole.DUNGEON, DUNGEON_PORT);
                return environment;
            }
            catch
            {
                await environment.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            return _database.DisposeAsync();
        }

        /// <summary> 계정과 이름 있는 캐릭터를 만든다. instance가 있으면 그 서버에 캐릭터 접속 임대를 얻는다 </summary>
        public async Task<SocialPlayer> CreatePlayerAsync(string? name = null, Guid? instance = null, bool online = true)
        {
            SocialPlayer player = new SocialPlayer(Guid.NewGuid(), Guid.NewGuid(), name ?? NextName());
            await ExecuteAsync(
                """
                INSERT INTO accounts (account_id) VALUES (@AccountId);
                INSERT INTO characters (character_id, account_id, class_id, name, name_key)
                VALUES (@CharacterId, @AccountId, @ClassId, @Name, lower(@Name));
                """, new { player.AccountId, player.CharacterId, ClassId = ContractCodes.PlayerClass.ARCHER, player.Name });
            if (online)
            {
                await ConnectAsync(player, instance ?? TownId);
            }

            return player;
        }

        /// <summary> 캐릭터 접속 임대를 얻는다(TryAcquire는 character_id를 비우므로 뒤에 채운다. 마을 입장 교환과 같은 결과) </summary>
        public async Task ConnectAsync(SocialPlayer player, Guid instance)
        {
            int role = instance == DungeonId ? ContractCodes.ServerRole.DUNGEON : ContractCodes.ServerRole.TOWN;
            await ExecuteAsync("DELETE FROM account_leases WHERE account_id = @AccountId;", new { player.AccountId });
            AccountLeaseOutcome lease = await Leases.TryAcquireAsync(player.AccountId, Guid.NewGuid(), instance, role,
                TimeSpan.FromMinutes(LEASE_MINUTES));
            Assert.Equal(EAccountLeaseStatus.Acquired, lease.Status);
            await ExecuteAsync("UPDATE account_leases SET character_id = @CharacterId WHERE account_id = @AccountId;",
                new { player.AccountId, player.CharacterId });
        }

        public Task DisconnectAsync(SocialPlayer player)
        {
            return ExecuteAsync("DELETE FROM account_leases WHERE account_id = @AccountId;", new { player.AccountId });
        }

        /// <summary> 이 플레이어가 접속한 서버의 호출 주체 </summary>
        public SocialCaller CallerOf(Guid instance)
        {
            return instance == DungeonId ? Dungeon : instance == OtherTownId ? OtherTown : Town;
        }

        public async Task<long> ScalarAsync(string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<long>(sql, parameters);
        }

        public async Task<T> SingleAsync<T>(string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<T>(sql, parameters);
        }

        public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return (await connection.QueryAsync<T>(sql, parameters)).AsList();
        }

        public async Task ExecuteAsync(string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(sql, parameters);
        }

        public Task<Guid> GuildOfAsync(SocialPlayer player)
        {
            return SingleAsync<Guid>("SELECT guild_id FROM guild_members WHERE character_id = @CharacterId;",
                new { player.CharacterId });
        }

        public Task<Guid> MembershipOfAsync(SocialPlayer player)
        {
            return SingleAsync<Guid>("SELECT membership_id FROM guild_members WHERE character_id = @CharacterId;",
                new { player.CharacterId });
        }

        public Task<long> LeaderVersionAsync(Guid guildId)
        {
            return ScalarAsync("SELECT leader_version FROM guilds WHERE guild_id = @GuildId;", new { GuildId = guildId });
        }

        /// <summary> 길드장 생성 뒤 members를 차례로 초대, 수락시켜 가입시킨다 </summary>
        public async Task<Guid> CreateGuildWithAsync(SocialPlayer leader, string guildName, params SocialPlayer[] members)
        {
            AssertCommitted(await Social.CreateGuildAsync(Town, leader.CharacterId, guildName, guildName.ToLowerInvariant(),
                Guid.NewGuid()));
            Guid guildId = await GuildOfAsync(leader);
            foreach (SocialPlayer member in members)
            {
                await AddMemberDirectAsync(guildId, member);
            }

            return guildId;
        }

        /// <summary> 정원 시험용으로 길드원 행과 정원을 직접 넣는다(불변식 member_count = 행 수 유지) </summary>
        public Task AddMemberDirectAsync(Guid guildId, SocialPlayer member)
        {
            return ExecuteAsync(
                """
                INSERT INTO guild_members (character_id, guild_id, membership_id, role) VALUES (@CharacterId, @GuildId, @Membership, 0);
                UPDATE guilds SET member_count = member_count + 1 WHERE guild_id = @GuildId;
                """, new { member.CharacterId, GuildId = guildId, Membership = Guid.NewGuid() });
        }

        /// <summary> member_count가 길드원 행 수와 같고 살아 있는 길드마다 길드장이 정확히 한 명이며 guilds.leader_character_id와 같다 </summary>
        public async Task AssertGuildInvariantsAsync()
        {
            Assert.Equal(0L, await ScalarAsync(
                """
                SELECT count(*) FROM guilds AS guild
                WHERE guild.member_count <> (SELECT count(*) FROM guild_members AS member WHERE member.guild_id = guild.guild_id)
                    OR (guild.disbanded_at IS NULL AND NOT EXISTS (SELECT 1 FROM guild_members AS member
                        WHERE member.guild_id = guild.guild_id AND member.role = 1
                            AND member.character_id = guild.leader_character_id));
                """));
        }

        public static void AssertCommitted(SocialActionOutcome outcome)
        {
            Assert.Equal(ContractCodes.RejectReason.NONE, outcome.ReasonCode);
        }

        public static void AssertRejected(SocialActionOutcome outcome, int reason)
        {
            Assert.Equal(reason, outcome.ReasonCode);
        }

        private async Task RegisterAsync(Guid instance, int role, int port)
        {
            ServerPoolOutcome registered = await Pool.RegisterAsync(new ServerRegistration(instance, Guid.NewGuid(), role,
                "127.0.0.1", port, BUILD, BUILD));
            Assert.Equal(EServerPoolStatus.Accepted, registered.Status);
        }

        // 대소문자 영문 이름. 길이 2~12, 겹치지 않게 순번을 붙인다
        private string NextName()
        {
            int value = ++_sequence;
            char[] letters = new char[4];
            for (int index = letters.Length - 1; index >= 0; --index)
            {
                letters[index] = (char)('a' + (value % 26));
                value /= 26;
            }

            return "Pc" + new string(letters);
        }
    }

    /// <summary> 소셜 시험 계정과 캐릭터 </summary>
    internal sealed record SocialPlayer(Guid AccountId, Guid CharacterId, string Name);
}
