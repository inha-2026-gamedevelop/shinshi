// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Growth
{
    /// <summary> 실제 원장, 장비 이력 쓰기와 성장 저장소를 격리 스키마에 묶는다. 시작 잔액도 원장을 거쳐 넣어 대사가 0건이어야 한다 </summary>
    internal sealed class GrowthTestEnvironment : IAsyncDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CONTENT_VERSION = "test-content";
        public const string BINDING         = GrowthAssets.ACCOUNT_BINDING;

        private const int    TEST_PORT     = 7777;
        private const int    LEASE_MINUTES = 5;
        private const string SEED_KIND     = "test.growth.seed";

        private readonly PostgresTestDatabase _database;

        /****************************************
        *              Properties
        ****************************************/

        public NpgsqlDataSource                  DataSource { get; }
        public IdempotentOperationExecutor       Executor   { get; }
        public GrowthContent                     Content    { get; } = GrowthContent.LoadEmbedded();
        public PostgresEconomyLedgerWriter       Ledger     { get; } = new PostgresEconomyLedgerWriter();
        public PostgresItemHistoryWriter         History    { get; } = new PostgresItemHistoryWriter();
        public PostgresAccountLeaseRepository    Leases     { get; }
        public PostgresServerPoolRepository      Pool       { get; }
        public PostgresGrowthReader              Reader     { get; }
        public GrowthOperationRunner             Runner     { get; }
        public PostgresTalismanRepository        Talismans  { get; }
        public PostgresEquipmentRepository       Equipment  { get; }
        public PostgresTrainingRepository        Training   { get; }
        public EconomyLedgerReconciler           Reconciler { get; }
        public Guid                              TownId     { get; } = Guid.NewGuid();

        /****************************************
        *                Methods
        ****************************************/

        private GrowthTestEnvironment(PostgresTestDatabase database)
        {
            _database  = database;
            DataSource = database.DataSource;
            Executor   = new IdempotentOperationExecutor(new PostgresOperationStore(DataSource));
            Leases     = new PostgresAccountLeaseRepository(DataSource);
            Pool       = new PostgresServerPoolRepository(DataSource);
            Reader     = new PostgresGrowthReader(DataSource, Content);
            Runner     = new GrowthOperationRunner(DataSource, Executor, Ledger, History, Reader, Content);
            Talismans  = new PostgresTalismanRepository(Runner);
            Equipment  = new PostgresEquipmentRepository(Runner);
            Training   = new PostgresTrainingRepository(Runner);
            Reconciler = new EconomyLedgerReconciler(DataSource);
        }

        public static async Task<GrowthTestEnvironment> CreateAsync()
        {
            PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            GrowthTestEnvironment environment = new GrowthTestEnvironment(database);
            try
            {
                ServerPoolOutcome registered = await environment.Pool.RegisterAsync(new ServerRegistration(environment.TownId,
                    Guid.NewGuid(), ContractCodes.ServerRole.TOWN, "127.0.0.1", TEST_PORT, "test-build", CONTENT_VERSION));
                Assert.Equal(EServerPoolStatus.Accepted, registered.Status);
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

        /// <summary> 계정, 캐릭터와 마을 접속 임대를 만들고 시작 잔액을 원장과 함께 넣는다 </summary>
        public async Task<GrowthPlayer> CreatePlayerAsync(int classId = ContractCodes.PlayerClass.WARRIOR, long essence = 0,
            long shards = 0, IReadOnlyDictionary<string, long>? items = null)
        {
            GrowthPlayer player = new GrowthPlayer(Guid.NewGuid(), Guid.NewGuid());
            await ExecuteAsync(
                """
                INSERT INTO accounts (account_id) VALUES (@AccountId);
                INSERT INTO characters (character_id, account_id, class_id) VALUES (@CharacterId, @AccountId, @ClassId);
                """, new { player.AccountId, player.CharacterId, ClassId = classId });
            await SeedAsync(player, essence, shards, items ?? new Dictionary<string, long>());

            AccountLeaseOutcome lease = await Leases.TryAcquireAsync(player.AccountId, Guid.NewGuid(), TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(LEASE_MINUTES));
            Assert.Equal(EAccountLeaseStatus.Acquired, lease.Status);
            return player;
        }

        /// <summary> 잔액을 원장과 함께 더한다. 운영 지급과 같은 경로라 대사 기준이 맞는다 </summary>
        public async Task SeedAsync(GrowthPlayer player, long essence, long shards, IReadOnlyDictionary<string, long> items)
        {
            OperationRequest request = new OperationRequest(player.AccountId, Guid.NewGuid(), SEED_KIND, "{}");
            OperationResult result = await Executor.ExecuteAsync(request, async (context, token) =>
            {
                GrowthEffectContext seed = new GrowthEffectContext(context, Content, Ledger, History, player.AccountId,
                    "System", request.OperationId, player.AccountId, player.CharacterId, 0,
                    ContractCodes.EconomyReason.ADMIN_GRANT);
                await seed.CreditAsync(seed.Essence(), essence, token);
                await seed.CreditAsync(seed.Shards(), shards, token);
                foreach ((string definitionId, long quantity) in items.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    await seed.CreditAsync(seed.AccountItem(definitionId), quantity, token);
                }

                return OperationDecision.Committed(0, Guid.NewGuid().ToString("D"));
            });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
        }

        /// <summary> 장비 하나를 원장 없이 직접 넣는다(수량형 자산이 아니라 대사 대상이 아니다) </summary>
        public async Task<Guid> InsertEquipmentAsync(GrowthPlayer player, string definitionId, int enhancement,
            bool equipped = false, string origin = GrowthCodes.EquipmentOrigin.CRAFT, bool locked = false)
        {
            Assert.True(Content.TryGetEquipment(definitionId, out GrowthContent.EquipmentDefinition definition));
            Guid itemId = Guid.NewGuid();
            await ExecuteAsync(
                """
                INSERT INTO equipment_instances (item_id, account_id, character_id, definition_id, slot, grade, is_training,
                    origin, enhancement, equipped, locked, bound)
                VALUES (@ItemId, @AccountId, @CharacterId, @DefinitionId, @Slot, @Grade, @IsTraining, @Origin, @Enhancement,
                    @Equipped, @Locked, false);
                """, new
                {
                    ItemId = itemId, player.AccountId, player.CharacterId, definition.DefinitionId, definition.Slot,
                    definition.Grade, definition.IsTraining, Origin = origin, Enhancement = enhancement,
                    Equipped = equipped, Locked = locked
                });
            return itemId;
        }

        public async Task<CharacterGrowthSnapshot> ReadAsync(GrowthPlayer player)
        {
            return Assert.IsType<CharacterGrowthSnapshot>(await Reader.ReadAsync(player.CharacterId));
        }

        public async Task<long> ScalarAsync(string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<long>(sql, parameters);
        }

        public async Task ExecuteAsync(string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(sql, parameters);
        }

        /// <summary> 모든 자산의 원장 합계와 잔액이 같아야 한다 </summary>
        public async Task AssertReconciledAsync()
        {
            IReadOnlyList<EconomyReconciliationMismatch> mismatches = await Reconciler.FindMismatchesAsync(1000);
            Assert.Empty(mismatches);
        }

        public static long Quantity(CharacterGrowthSnapshot growth, string definitionId)
        {
            return growth.AccountItems.Where(item => item.DefinitionId == definitionId).Sum(item => item.Quantity);
        }
    }

    /// <summary> 성장 테스트 계정과 캐릭터 </summary>
    internal sealed class GrowthPlayer
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId   { get; }
        public Guid CharacterId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public GrowthPlayer(Guid accountId, Guid characterId)
        {
            AccountId   = accountId;
            CharacterId = characterId;
        }
    }
}
