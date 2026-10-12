// System
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Auctions;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 마이그레이션을 적용한 격리 스키마에 출정 저장소와 테스트 원장, 가짜 토큰 발급기를 묶는다 </summary>
    internal sealed class SessionTestEnvironment : IAsyncDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CONTENT_VERSION = "0.1.2";
        public const string POTION_ID       = "potion-small";
        public const string BINDING         = SettlementSanityLimits.ACCOUNT_BINDING_KIND;
        public const string MAP_ID          = "region_forest";
        public const string BOSS_ID         = "boss_wolbaek";

        private const int    TEST_PORT        = 7777;
        private const int    LEASE_MINUTES    = 5;
        private const int    TOKEN_HASH_BYTES = 32;

        private readonly PostgresTestDatabase _database;

        /****************************************
        *              Properties
        ****************************************/

        public NpgsqlDataSource               DataSource   => _database.DataSource;
        public DeadlockCountingStore          Store        { get; }
        public IdempotentOperationExecutor    Executor     { get; }
        public TestLedgerWriter               Ledger       { get; }
        public FakeJoinTokenIssuer            JoinTokens   { get; }
        public PostgresAccountLeaseRepository Leases       { get; }
        public PostgresServerPoolRepository   Pool         { get; }
        public PostgresReservationRepository  Reservations { get; }
        public PostgresGameSessionRepository  Sessions     { get; }
        public PostgresSettlementRepository   Settlement   { get; }
        public PostgresPartyAuctionRepository Auctions     { get; }
        public Guid                           TownId       { get; } = Guid.NewGuid();
        public List<Guid>                     DungeonIds   { get; } = new List<Guid>();

        /****************************************
        *                Methods
        ****************************************/

        private SessionTestEnvironment(PostgresTestDatabase database)
        {
            _database    = database;
            Store        = new DeadlockCountingStore(new PostgresOperationStore(database.DataSource));
            Executor     = new IdempotentOperationExecutor(Store);
            Ledger       = new TestLedgerWriter();
            JoinTokens   = new FakeJoinTokenIssuer();
            Leases       = new PostgresAccountLeaseRepository(database.DataSource);
            Pool         = new PostgresServerPoolRepository(database.DataSource);
            Reservations = new PostgresReservationRepository(database.DataSource, Executor, Leases, JoinTokens);
            Sessions     = new PostgresGameSessionRepository(database.DataSource, Executor, Ledger);
            Settlement   = new PostgresSettlementRepository(database.DataSource, Executor, Ledger);
            Auctions     = new PostgresPartyAuctionRepository(database.DataSource, Executor, Ledger);
        }

        public static async Task<SessionTestEnvironment> CreateAsync(int dungeonCount = 1)
        {
            PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            SessionTestEnvironment environment = new SessionTestEnvironment(database);
            try
            {
                await environment.CreateTestLedgerAsync();
                await environment.RegisterAsync(environment.TownId, ContractCodes.ServerRole.TOWN);
                for (int index = 0; index < dungeonCount; ++index)
                {
                    Guid dungeonId = Guid.NewGuid();
                    await environment.RegisterAsync(dungeonId, ContractCodes.ServerRole.DUNGEON);
                    environment.DungeonIds.Add(dungeonId);
                }

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

        /// <summary> 계정, 캐릭터, 지갑, 물약 재고와 마을 접속 임대를 만든다 </summary>
        public async Task<TestPlayer> CreatePlayerAsync(long tickets, long potions)
        {
            TestPlayer player = new TestPlayer(Guid.NewGuid(), Guid.NewGuid());
            await using (NpgsqlConnection connection = await DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO accounts (account_id) VALUES (@AccountId);
                    INSERT INTO characters (character_id, account_id, class_id) VALUES (@CharacterId, @AccountId, 0);
                    INSERT INTO account_wallets (account_id, tickets) VALUES (@AccountId, @Tickets);
                    INSERT INTO account_items (account_id, definition_id, binding_kind, quantity)
                    VALUES (@AccountId, @PotionId, @Binding, @Potions);
                    """, new
                    {
                        player.AccountId, player.CharacterId, Tickets = tickets, PotionId = POTION_ID, Binding = BINDING,
                        Potions = potions
                    });
            }

            AccountLeaseOutcome lease = await Leases.TryAcquireAsync(player.AccountId, Guid.NewGuid(), TownId,
                ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(LEASE_MINUTES));
            Assert.Equal(EAccountLeaseStatus.Acquired, lease.Status);
            return player;
        }

        public ReservationInput CreateReservation(Guid operationId, int mode, IEnumerable<TestPlayer> players,
            long potionsEach)
        {
            List<ParticipantReservationInput> participants = players.Select(player => new ParticipantReservationInput(
                player.AccountId, player.CharacterId, 0,
                potionsEach > 0
                    ? new[] { new PotionReservationInput(POTION_ID, BINDING, potionsEach) }
                    : Array.Empty<PotionReservationInput>())).ToList();
            // 보스전은 bossId와 난이도가 필수다(0012 출정 계약)
            bool boss = mode == GameSessionCodes.Mode.BOSS;
            return new ReservationInput(TownId, operationId, mode, MAP_ID, CONTENT_VERSION, participants, null,
                boss ? BOSS_ID : null, boss ? GameSessionCodes.Difficulty.EASY : GameSessionCodes.Difficulty.NONE);
        }

        /// <summary> 예약을 확정하고 세션 정보를 돌려준다 </summary>
        public async Task<ReservedSessionView> ReserveAsync(int mode, IReadOnlyList<TestPlayer> players, long potionsEach)
        {
            ReservationOutcome outcome = await Reservations.ReserveAsync(
                CreateReservation(Guid.NewGuid(), mode, players, potionsEach));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, outcome.Result.Status);
            return Assert.IsType<ReservedSessionView>(outcome.Session);
        }

        /// <summary> 예약 뒤 /start까지 확정한다 </summary>
        public async Task<ReservedSessionView> StartAsync(int mode, IReadOnlyList<TestPlayer> players, long potionsEach)
        {
            ReservedSessionView session = await ReserveAsync(mode, players, potionsEach);
            OperationResult started = await Sessions.StartAsync(session.InstanceId, session.SessionId, Guid.NewGuid(),
                session.Participants.Select(participant => participant.ParticipantId).ToArray());
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, started.Status);
            return session;
        }

        /// <summary> 보스 성공 정산 뒤 Auction 단계를 배정 던전의 boot로 끝낸다(출정 Closed, 임대 해제, Draining) </summary>
        public async Task<OperationResult> FinishAuctionAsync(ReservedSessionView session)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            Guid? boot = await connection.QuerySingleOrDefaultAsync<Guid?>(
                "SELECT instance_boot_id FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId });
            return await Auctions.FinishAsync(session.InstanceId, boot, session.SessionId, Guid.NewGuid());
        }

        public async Task<WalletSnapshot> ReadWalletAsync(Guid accountId)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<WalletSnapshot>(
                """
                SELECT wallet.tickets AS "Tickets", wallet.tickets_reserved AS "TicketsReserved",
                    item.quantity AS "Potions", item.reserved AS "PotionsReserved", hero.essence AS "Essence"
                FROM account_wallets AS wallet
                JOIN account_items AS item ON item.account_id = wallet.account_id
                    AND item.definition_id = @PotionId AND item.binding_kind = @Binding
                JOIN characters AS hero ON hero.account_id = wallet.account_id
                WHERE wallet.account_id = @AccountId;
                """, new { AccountId = accountId, PotionId = POTION_ID, Binding = BINDING });
        }

        public async Task<long> LedgerSumAsync(Guid accountId, string assetKind)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<long>(
                "SELECT COALESCE(sum(delta), 0)::bigint FROM test_economy_ledger WHERE account_id = @AccountId AND asset_kind = @AssetKind;",
                new { AccountId = accountId, AssetKind = assetKind });
        }

        public async Task<long> ScalarAsync(string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<long>(sql, parameters);
        }

        public async Task<string> TextAsync(string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<string>(sql, parameters);
        }

        public async Task ExecuteAsync(string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(sql, parameters);
        }

        /// <summary> 서비스 자격으로 발급된 것과 같은 살아 있는 서버 임대 행을 현재 등록된 boot에 묶어 둔다. 배정은 이 임대가 있는 던전만 고른다 </summary>
        public async Task GrantServerLeaseAsync(Guid instanceId, int role)
        {
            await ExecuteAsync(
                """
                INSERT INTO auth_server_leases (lease_id, service_id, instance_id, role, token_hash, expires_at, boot_id)
                SELECT gen_random_uuid(), gen_random_uuid(), @InstanceId, @Role, @Hash,
                    clock_timestamp() + make_interval(mins => @Minutes), boot_id
                FROM server_instances WHERE instance_id = @InstanceId;
                """, new { InstanceId = instanceId, Role = role, Hash = RandomNumberGenerator.GetBytes(TOKEN_HASH_BYTES),
                    Minutes = LEASE_MINUTES });
        }

        /// <summary> 출정을 마치고 Draining이 된 던전을 새 bootId 프로세스로 다시 등록한다. 실제 던전은 Draining을 보고 재시작한다 </summary>
        public async Task RebootDungeonAsync(Guid instanceId)
        {
            await RegisterAsync(instanceId, ContractCodes.ServerRole.DUNGEON);
        }

        private async Task RegisterAsync(Guid instanceId, int role)
        {
            ServerPoolOutcome registered = await Pool.RegisterAsync(new ServerRegistration(instanceId, Guid.NewGuid(), role,
                "127.0.0.1", TEST_PORT, "test-build", CONTENT_VERSION));
            Assert.Equal(EServerPoolStatus.Accepted, registered.Status);
            await GrantServerLeaseAsync(instanceId, role);
        }

        private async Task CreateTestLedgerAsync()
        {
            // 실제 economy_ledger는 정수 사유 코드를 요구한다. 사유 대응은 연결 작업에서 정하므로 테스트는 별도 표에 기록한다
            await using NpgsqlConnection connection = await DataSource.OpenConnectionAsync();
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync(
                """
                CREATE TABLE test_economy_ledger
                (
                    ledger_id bigserial PRIMARY KEY,
                    account_id uuid NOT NULL,
                    character_id uuid NULL,
                    asset_kind text NOT NULL,
                    asset_id text NOT NULL,
                    binding_kind text NOT NULL,
                    entry_key text NOT NULL,
                    delta bigint NOT NULL,
                    balance_after bigint NOT NULL,
                    reason text NOT NULL,
                    operation_id uuid NOT NULL,
                    UNIQUE (operation_id, entry_key)
                );
                """, transaction: transaction);
            await transaction.CommitAsync();
        }
    }

    /// <summary> 테스트 계정과 캐릭터 </summary>
    internal sealed class TestPlayer
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid AccountId   { get; }
        public Guid CharacterId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public TestPlayer(Guid accountId, Guid characterId)
        {
            AccountId   = accountId;
            CharacterId = characterId;
        }
    }

    /// <summary> 계정 하나의 잔액 스냅샷 </summary>
    internal sealed class WalletSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public long Tickets         { get; set; }
        public long TicketsReserved { get; set; }
        public long Potions         { get; set; }
        public long PotionsReserved { get; set; }
        public long Essence         { get; set; }
    }

    /// <summary> 실제 저장소를 감싸 재시도된 데드락 횟수를 센다. 재시도로 숨겨진 데드락도 테스트에서 드러난다 </summary>
    internal sealed class DeadlockCountingStore : IOperationStore
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IOperationStore _inner;
        private int _deadlocks;

        /****************************************
        *              Properties
        ****************************************/

        public int Deadlocks => Volatile.Read(ref _deadlocks);

        /****************************************
        *                Methods
        ****************************************/

        public DeadlockCountingStore(IOperationStore inner)
        {
            _inner = inner;
        }

        public Task<IOperationScope> BeginAsync(OperationRequest request, CancellationToken cancellationToken)
        {
            return _inner.BeginAsync(request, cancellationToken);
        }

        public bool IsRetryable(Exception exception)
        {
            if ((exception is PostgresException postgres) && (postgres.SqlState == PostgresErrorCodes.DeadlockDetected))
            {
                Interlocked.Increment(ref _deadlocks);
            }

            return _inner.IsRetryable(exception);
        }
    }

    /// <summary> 같은 트랜잭션에 테스트 원장 행을 쓴다. 효과가 되돌려지면 원장도 함께 사라진다 </summary>
    internal sealed class TestLedgerWriter : IEconomyLedgerWriter
    {
        /****************************************
        *                Methods
        ****************************************/

        public async Task AppendAsync(OperationExecutionContext context, IReadOnlyList<EconomyLedgerLine> entries,
            CancellationToken cancellationToken)
        {
            if (entries.Count == 0)
            {
                return;
            }

            await context.Connection!.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO test_economy_ledger (account_id, character_id, asset_kind, asset_id, binding_kind, entry_key,
                    delta, balance_after, reason, operation_id)
                SELECT entry.account_id, NULLIF(entry.character_id, '00000000-0000-0000-0000-000000000000'::uuid),
                    entry.asset_kind, entry.asset_id, entry.binding_kind, entry.entry_key, entry.delta,
                    entry.balance_after, entry.reason, entry.operation_id
                FROM unnest(@Accounts::uuid[], @Characters::uuid[], @Kinds::text[], @Assets::text[], @Bindings::text[],
                    @Keys::text[], @Deltas::bigint[], @Balances::bigint[], @Reasons::text[], @Operations::uuid[])
                    AS entry(account_id, character_id, asset_kind, asset_id, binding_kind, entry_key, delta,
                        balance_after, reason, operation_id);
                """, new
                {
                    Accounts = entries.Select(entry => entry.AccountId).ToArray(),
                    Characters = entries.Select(entry => entry.CharacterId ?? Guid.Empty).ToArray(),
                    Kinds = entries.Select(entry => entry.AssetKind).ToArray(),
                    Assets = entries.Select(entry => entry.AssetId).ToArray(),
                    Bindings = entries.Select(entry => entry.BindingKind).ToArray(),
                    Keys = entries.Select(entry => entry.EntryKey).ToArray(),
                    Deltas = entries.Select(entry => entry.Delta).ToArray(),
                    Balances = entries.Select(entry => entry.BalanceAfter).ToArray(),
                    Reasons = entries.Select(entry => entry.Reason).ToArray(),
                    Operations = entries.Select(entry => entry.OperationId).ToArray()
                }, context.Transaction, cancellationToken: cancellationToken));
        }
    }

    /// <summary> 토큰 원문을 만들지 않는 가짜 발급기. 발급 요청만 기록한다 </summary>
    internal sealed class FakeJoinTokenIssuer : IJoinTokenIssuer
    {
        /****************************************
        *              Properties
        ****************************************/

        public ConcurrentQueue<DungeonJoinGrant> Grants { get; } = new ConcurrentQueue<DungeonJoinGrant>();

        /****************************************
        *                Methods
        ****************************************/

        public Task<JoinTicket> IssueDungeonTicketAsync(DungeonJoinGrant grant, CancellationToken cancellationToken)
        {
            Grants.Enqueue(grant);
            long expires = DateTimeOffset.UtcNow.Add(grant.TokenLifetime).ToUnixTimeMilliseconds();
            return Task.FromResult(new JoinTicket("test-join-" + grant.ParticipantId.ToString("N"), expires,
                ContractCodes.ServerRole.DUNGEON, grant.InstanceId.ToString("D"), grant.Address, grant.Port,
                grant.CharacterId.ToString("D"), grant.ParticipantId.ToString("D"),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
    }
}
