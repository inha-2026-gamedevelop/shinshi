// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Auth;
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Auth;
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;
using Shinshi.Data.Prologue;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Prologue
{
    /// <summary> 실제 PostgreSQL에서 프롤로그 입장, 출정 수명주기, 단계 저장, 완료, 건너뛰기, 메뉴 안내와 0023 마이그레이션을 검사한다 </summary>
    public sealed class ProloguePostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MIGRATION_0023 = "0023_prologue";
        private const int    CLASS_ID       = 3;
        private const int    TOKEN_BYTES    = 32;
        private const int    PARALLEL_CALLS = 8;
        private const int    RELEASE_BATCH  = 10;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Migration0023_BackfillsExistingCharactersAsDone_NewRowsStartAtIntro()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            (long version, string name) = MigrationTestResources.All.Single(entry => entry.Name == MIGRATION_0023);
            await MigrationTestResources.RollBackAboveAsync(connection, version - 1);

            Guid accountId = Guid.NewGuid();
            Guid existing = Guid.NewGuid();
            Guid created = Guid.NewGuid();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts (account_id) VALUES (@AccountId);
                INSERT INTO characters (character_id, account_id, class_id) VALUES (@Existing, @AccountId, 0);
                """, new { AccountId = accountId, Existing = existing });
            await connection.ExecuteAsync(await MigrationTestResources.ReadUpAsync(name));
            await connection.ExecuteAsync(
                "INSERT INTO characters (character_id, account_id, class_id) VALUES (@Created, @AccountId, 0);",
                new { AccountId = accountId, Created = created });

            Assert.Equal(PrologueCodes.Stage.DONE, await connection.QuerySingleAsync<short>(
                "SELECT prologue_stage FROM characters WHERE character_id = @Id;", new { Id = existing }));
            Assert.Equal(PrologueCodes.Stage.INTRO, await connection.QuerySingleAsync<short>(
                "SELECT prologue_stage FROM characters WHERE character_id = @Id;", new { Id = created }));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM pg_constraint AS constraint_row
                JOIN pg_class AS table_row ON table_row.oid = constraint_row.conrelid
                JOIN pg_namespace AS schema_row ON schema_row.oid = table_row.relnamespace
                WHERE schema_row.nspname = current_schema() AND NOT constraint_row.convalidated;
                """));
            await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "UPDATE characters SET prologue_wolbaek_potions = 10 WHERE character_id = @Id;", new { Id = created }));
        }

        [PostgresFact]
        public async Task FullFlow_EntryExchangeStartEndComplete_ReleasesLeaseAndGrantsOnce()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            Fixture fixture = new Fixture(environment);
            (PlayerAuthIdentity identity, Guid characterId) = await fixture.CreateLobbyPlayerAsync();
            Guid entryOperation = Guid.NewGuid();

            PrologueEntryOutcome entry = await fixture.Prologue.EnterAsync(identity, characterId, entryOperation);
            PrologueEntryOutcome replay = await fixture.Prologue.EnterAsync(identity, characterId, entryOperation);

            Assert.NotNull(entry.Ticket);
            Assert.Equal(entry.Ticket!.JoinToken, replay.Ticket!.JoinToken);
            Assert.Equal(ContractCodes.ServerRole.DUNGEON, entry.Ticket.ServerRole);
            Guid dungeonId = Guid.Parse(entry.Ticket.InstanceId);
            Guid participantId = Guid.Parse(entry.Ticket.ParticipantId);
            Guid sessionId = await environment.ScalarGuidAsync(
                "SELECT session_id FROM session_participants WHERE participant_id = @Id;", new { Id = participantId });
            Assert.Equal(GameSessionStates.MODE_PROLOGUE, await environment.TextAsync(
                "SELECT mode FROM game_sessions WHERE session_id = @Id;", new { Id = sessionId }));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE account_id = @AccountId AND instance_id = @Dungeon;",
                new { identity.AccountId, Dungeon = dungeonId }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM reservations WHERE session_id = @Id;", new { Id = sessionId }));

            await environment.ExecuteAsync(
                "UPDATE characters SET prologue_stage = 2, prologue_wolbaek_potions = 4 WHERE character_id = @Id;",
                new { Id = characterId });
            Guid serverLease = await environment.ScalarGuidAsync(
                """
                SELECT lease_id FROM auth_server_leases WHERE instance_id = @Dungeon AND revoked_at IS NULL
                ORDER BY expires_at DESC LIMIT 1;
                """, new { Dungeon = dungeonId });
            AuthValidationOutcome<JoinAuthIdentity> exchanged = await fixture.Auth.ConsumeJoinTokenAsync(
                entry.Ticket.JoinToken, dungeonId, ContractCodes.ServerRole.DUNGEON, serverLease);
            Assert.True(exchanged.IsSuccess);
            JoinSortieContract sortie = exchanged.Identity!.Sortie!;
            Assert.Equal(GameSessionCodes.Mode.PROLOGUE, sortie.Mode);
            Assert.Equal(PrologueCodes.MAP_ID, sortie.MapId);
            Assert.Equal(PrologueCodes.BATTLE_ID, sortie.BattleId);
            Assert.Equal(PrologueCodes.BOSS_ID, sortie.BossId);
            Assert.Equal(GameSessionCodes.Difficulty.EASY, sortie.Difficulty);
            Assert.Equal(PrologueCodes.Stage.WOLBAEK, sortie.PrologueStage);
            Assert.Equal(4, sortie.PrologueWolbaekPotions);

            LeaseTransferOutcome transferred = await environment.Reservations.TransferParticipantLeaseAsync(dungeonId,
                sessionId, participantId, Guid.NewGuid());
            Assert.Equal(ContractCodes.RejectReason.NONE, transferred.ReasonCode);
            OperationResult started = await environment.Sessions.StartAsync(dungeonId, sessionId, Guid.NewGuid(),
                new[] { participantId });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, started.Status);

            OperationResult essence = await environment.Sessions.AppendEventsAsync(dungeonId, sessionId, Guid.NewGuid(),
                new[] { SessionEventTests.Essence(1, participantId, 1) });
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, essence.Status);

            Guid completeOperation = Guid.NewGuid();
            OperationResult[] completes = await Task.WhenAll(Enumerable.Range(0, PARALLEL_CALLS).Select(_ =>
                fixture.Prologue.CompleteAsync(dungeonId, sessionId, characterId, completeOperation)));
            Assert.All(completes, result => Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status));
            OperationResult otherComplete = await fixture.Prologue.CompleteAsync(dungeonId, sessionId, characterId,
                Guid.NewGuid());
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, otherComplete.Status);

            Guid settlementId = Guid.NewGuid();
            OperationResult ended = await environment.Sessions.AppendEventsAsync(dungeonId, sessionId, Guid.NewGuid(),
                new[] { SessionEventTests.Ended(1, settlementId, new[] { SessionEventTests.Grant(participantId, 0) }) });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, ended.Status);
            OperationResult settled = await environment.Settlement.SettleAsync(dungeonId, sessionId, settlementId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);

            Assert.Equal(GameSessionStates.CLOSED, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = sessionId }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE account_id = @AccountId;", new { identity.AccountId }));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM prologue_completions WHERE character_id = @Id AND source = 'Played';",
                new { Id = characterId }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_unlocks WHERE account_id = @AccountId;", new { identity.AccountId }));
            PrologueStateResponse? state = await fixture.Prologue.ReadStateAsync(identity.AccountId, characterId);
            Assert.Equal(PrologueCodes.Stage.DONE, state!.Stage);
            Assert.True(state.AccountCompleted);
        }

        [PostgresFact]
        public async Task Entry_RejectsConnectedAccountDoneCharacterAndOtherOwner()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            Fixture fixture = new Fixture(environment);
            (PlayerAuthIdentity identity, Guid characterId) = await fixture.CreateLobbyPlayerAsync();
            (PlayerAuthIdentity other, Guid _) = await fixture.CreateLobbyPlayerAsync();

            PrologueEntryOutcome foreign = await fixture.Prologue.EnterAsync(other, characterId, Guid.NewGuid());
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_NOT_FOUND, foreign.ReasonCode);

            AccountLeaseOutcome town = await environment.Leases.TryAcquireAsync(identity.AccountId, Guid.NewGuid(),
                environment.TownId, ContractCodes.ServerRole.TOWN, TimeSpan.FromMinutes(1));
            Assert.Equal(EAccountLeaseStatus.Acquired, town.Status);
            PrologueEntryOutcome connected = await fixture.Prologue.EnterAsync(identity, characterId, Guid.NewGuid());
            Assert.Equal(ContractCodes.RejectReason.ALREADY_CONNECTED, connected.ReasonCode);
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM game_sessions WHERE mode = 'Prologue';"));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM server_instances WHERE role = 1 AND state = 'Idle';"));

            await environment.ExecuteAsync("DELETE FROM account_leases WHERE account_id = @AccountId;",
                new { identity.AccountId });
            await environment.ExecuteAsync("UPDATE characters SET prologue_stage = 4 WHERE character_id = @Id;",
                new { Id = characterId });
            PrologueEntryOutcome done = await fixture.Prologue.EnterAsync(identity, characterId, Guid.NewGuid());
            Assert.Equal(ContractCodes.RejectReason.PROLOGUE_ALREADY_DONE, done.ReasonCode);
        }

        [PostgresFact]
        public async Task Entry_NewOperationOnReservedSession_ReusesSessionAndRevokesOldTicket()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            Fixture fixture = new Fixture(environment);
            (PlayerAuthIdentity identity, Guid characterId) = await fixture.CreateLobbyPlayerAsync();
            Guid firstOperation = Guid.NewGuid();

            PrologueEntryOutcome first = await fixture.Prologue.EnterAsync(identity, characterId, firstOperation);
            PrologueEntryOutcome second = await fixture.Prologue.EnterAsync(identity, characterId, Guid.NewGuid());
            PrologueEntryOutcome firstReplay = await fixture.Prologue.EnterAsync(identity, characterId, firstOperation);

            Assert.NotNull(second.Ticket);
            Assert.NotEqual(first.Ticket!.JoinToken, second.Ticket!.JoinToken);
            Assert.Equal(first.Ticket.ParticipantId, second.Ticket.ParticipantId);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED, firstReplay.ReasonCode);
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM game_sessions WHERE mode = 'Prologue';"));
        }

        [PostgresFact]
        public async Task Entry_SameOperationConcurrently_CreatesOneSession()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(dungeonCount: 2);
            Fixture fixture = new Fixture(environment);
            (PlayerAuthIdentity identity, Guid characterId) = await fixture.CreateLobbyPlayerAsync();
            Guid operationId = Guid.NewGuid();

            PrologueEntryOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, PARALLEL_CALLS).Select(_ =>
                fixture.Prologue.EnterAsync(identity, characterId, operationId)));

            Assert.All(outcomes, outcome => Assert.Equal(outcomes[0].Ticket!.JoinToken, outcome.Ticket!.JoinToken));
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM game_sessions WHERE mode = 'Prologue';"));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM server_instances WHERE role = 1 AND state = 'Idle';"));
        }

        [PostgresFact]
        public async Task ExpiredReservedPrologue_ReleasesDungeonLeaseAndKeepsStage()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            Fixture fixture = new Fixture(environment);
            (PlayerAuthIdentity identity, Guid characterId) = await fixture.CreateLobbyPlayerAsync();
            await environment.ExecuteAsync("UPDATE characters SET prologue_stage = 1 WHERE character_id = @Id;",
                new { Id = characterId });
            PrologueEntryOutcome entry = await fixture.Prologue.EnterAsync(identity, characterId, Guid.NewGuid());
            Assert.NotNull(entry.Ticket);

            await environment.ExecuteAsync(
                "UPDATE game_sessions SET expires_at = clock_timestamp() - interval '1 second' WHERE mode = 'Prologue';");
            int released = await environment.Reservations.ReleaseExpiredAsync(RELEASE_BATCH);

            Assert.Equal(1, released);
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE account_id = @AccountId;", new { identity.AccountId }));
            Assert.Equal(PrologueCodes.Stage.FOREST, (await fixture.Prologue.ReadStateAsync(identity.AccountId,
                characterId))!.Stage);
        }

        [PostgresFact]
        public async Task SaveStage_OnlyMovesForwardAndKeepsTalisman()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            Fixture fixture = new Fixture(environment);
            (PlayerAuthIdentity identity, Guid characterId) = await fixture.CreateLobbyPlayerAsync();
            (Guid dungeonId, Guid sessionId, Guid _) = await fixture.StartPrologueAsync(identity, characterId);

            OperationResult forest = await fixture.SaveStageAsync(dungeonId, sessionId, characterId,
                PrologueCodes.Stage.FOREST, 0, true);
            OperationResult wolbaek = await fixture.SaveStageAsync(dungeonId, sessionId, characterId,
                PrologueCodes.Stage.WOLBAEK, 5, false);
            OperationResult same = await fixture.SaveStageAsync(dungeonId, sessionId, characterId,
                PrologueCodes.Stage.WOLBAEK, 5, false);
            OperationResult backward = await fixture.SaveStageAsync(dungeonId, sessionId, characterId,
                PrologueCodes.Stage.FOREST, 0, false);
            OperationResult tooMany = await fixture.SaveStageAsync(dungeonId, sessionId, characterId,
                PrologueCodes.Stage.ENDING, PrologueCodes.MAX_POTIONS + 1, false);
            OperationResult wrongServer = await fixture.SaveStageAsync(Guid.NewGuid(), sessionId, characterId,
                PrologueCodes.Stage.ENDING, 0, false);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, forest.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, wolbaek.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, same.Status);
            Assert.Equal(ContractCodes.RejectReason.PROLOGUE_STAGE_INVALID, backward.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.PROLOGUE_STAGE_INVALID, tooMany.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, wrongServer.ReasonCode);
            PrologueStateResponse state = (await fixture.Prologue.ReadStateAsync(identity.AccountId, characterId))!;
            Assert.Equal(PrologueCodes.Stage.WOLBAEK, state.Stage);
            Assert.Equal(5, state.WolbaekPotions);
            Assert.True(state.TalismanChecked);
            Assert.False(state.AccountCompleted);
        }

        [PostgresFact]
        public async Task Skip_RequiresAccountCompletion_ThenMarksDoneOnce()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            Fixture fixture = new Fixture(environment);
            (PlayerAuthIdentity identity, Guid characterId) = await fixture.CreateLobbyPlayerAsync();
            Guid operationId = Guid.NewGuid();

            OperationResult required = await fixture.Prologue.SkipAsync(identity.AccountId, characterId, Guid.NewGuid());
            await environment.ExecuteAsync(
                "INSERT INTO account_progress (account_id, intro_completed_at) VALUES (@AccountId, clock_timestamp());",
                new { identity.AccountId });
            OperationResult skipped = await fixture.Prologue.SkipAsync(identity.AccountId, characterId, operationId);
            OperationResult replay = await fixture.Prologue.SkipAsync(identity.AccountId, characterId, operationId);
            OperationResult again = await fixture.Prologue.SkipAsync(identity.AccountId, characterId, Guid.NewGuid());

            Assert.Equal(ContractCodes.RejectReason.PROLOGUE_REQUIRED, required.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, skipped.Status);
            Assert.Equal(skipped.ReceiptId, replay.ReceiptId);
            Assert.Equal(ContractCodes.RejectReason.PROLOGUE_ALREADY_DONE, again.ReasonCode);
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM prologue_completions WHERE character_id = @Id AND source = 'Skipped';",
                new { Id = characterId }));
        }

        [PostgresFact]
        public async Task MenuGuides_DuplicateIsStable_LimitRejectsNewIds()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            Fixture fixture = new Fixture(environment);
            (PlayerAuthIdentity identity, Guid _) = await fixture.CreateLobbyPlayerAsync();
            PostgresMenuGuideRepository guides = new PostgresMenuGuideRepository(environment.DataSource);

            MenuGuideOutcome first = await guides.CompleteAsync(identity.AccountId, "skill_tree");
            MenuGuideOutcome duplicate = await guides.CompleteAsync(identity.AccountId, "skill_tree");
            MenuGuideOutcome invalid = await guides.CompleteAsync(identity.AccountId, "Skill-Tree");
            for (int index = 1; index < PrologueCodes.MAX_MENU_GUIDES; ++index)
            {
                Assert.Equal(ContractCodes.RejectReason.NONE,
                    (await guides.CompleteAsync(identity.AccountId, "menu_" + index.ToString("D2"))).ReasonCode);
            }
            MenuGuideOutcome limited = await guides.CompleteAsync(identity.AccountId, "menu_extra");
            MenuGuideOutcome existingAtLimit = await guides.CompleteAsync(identity.AccountId, "skill_tree");
            MenuGuideOutcome listed = await guides.ListAsync(identity.AccountId);

            Assert.Equal(new[] { "skill_tree" }, first.MenuIds);
            Assert.Equal(first.MenuIds, duplicate.MenuIds);
            Assert.Equal(ContractCodes.RejectReason.MENU_GUIDE_INVALID, invalid.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.MENU_GUIDE_LIMIT, limited.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.NONE, existingAtLimit.ReasonCode);
            Assert.Equal(PrologueCodes.MAX_MENU_GUIDES, listed.MenuIds.Count);
            Assert.Equal(listed.MenuIds.OrderBy(id => id, StringComparer.Ordinal), listed.MenuIds);
        }

        /// <summary> 인증 저장소, 진행, 초기 지급과 프롤로그 저장소를 출정 시험 환경에 묶는다 </summary>
        private sealed class Fixture
        {
            /****************************************
            *              Properties
            ****************************************/

            public SessionTestEnvironment     Environment { get; }
            public PostgresAuthRepository     Auth        { get; }
            public PostgresPrologueRepository Prologue    { get; }

            /****************************************
            *                Methods
            ****************************************/

            public Fixture(SessionTestEnvironment environment)
            {
                Environment = environment;
                Auth = new PostgresAuthRepository(environment.DataSource, AuthTokenHasherTests.CreateOptions());
                GrowthContent content = GrowthContent.LoadEmbedded();
                PostgresAccountProgressRepository progress = new PostgresAccountProgressRepository(environment.DataSource,
                    content, EGrowthContentProfile.Trial, environment.Executor);
                InitialGrantService grants = new InitialGrantService(content, EGrowthContentProfile.Trial,
                    new PostgresEconomyLedgerWriter(), new PostgresItemHistoryWriter());
                Prologue = new PostgresPrologueRepository(environment.DataSource, environment.Executor, Auth, progress,
                    grants);
            }

            /// <summary> 로비 상태(접속 임대 없음)의 계정, 새 캐릭터(단계 INTRO), 살아 있는 로그인 묶음 </summary>
            public async Task<(PlayerAuthIdentity Identity, Guid CharacterId)> CreateLobbyPlayerAsync()
            {
                Guid accountId = Guid.NewGuid();
                Guid characterId = Guid.NewGuid();
                Guid familyId = Guid.NewGuid();
                Guid refreshId = Guid.NewGuid();
                await Environment.ExecuteAsync(
                    """
                    INSERT INTO accounts (account_id) VALUES (@AccountId);
                    INSERT INTO characters (character_id, account_id, class_id) VALUES (@CharacterId, @AccountId, @ClassId);
                    INSERT INTO auth_token_families (family_id, account_id, origin) VALUES (@FamilyId, @AccountId, 'invite');
                    INSERT INTO auth_refresh_tokens (token_id, account_id, family_id, token_hash, expires_at)
                    VALUES (@RefreshId, @AccountId, @FamilyId, @Hash, clock_timestamp() + interval '1 hour');
                    """, new
                    {
                        AccountId = accountId, CharacterId = characterId, ClassId = CLASS_ID, FamilyId = familyId,
                        RefreshId = refreshId, Hash = RandomNumberGenerator.GetBytes(TOKEN_BYTES)
                    });
                return (new PlayerAuthIdentity(accountId, familyId, refreshId), characterId);
            }

            /// <summary> 입장, 교환, 임대 이관, 시작까지 진행한 프롤로그 출정 </summary>
            public async Task<(Guid DungeonId, Guid SessionId, Guid ParticipantId)> StartPrologueAsync(
                PlayerAuthIdentity identity, Guid characterId)
            {
                PrologueEntryOutcome entry = await Prologue.EnterAsync(identity, characterId, Guid.NewGuid());
                Guid dungeonId = Guid.Parse(entry.Ticket!.InstanceId);
                Guid participantId = Guid.Parse(entry.Ticket.ParticipantId);
                Guid sessionId = await Environment.ScalarGuidAsync(
                    "SELECT session_id FROM session_participants WHERE participant_id = @Id;", new { Id = participantId });
                OperationResult started = await Environment.Sessions.StartAsync(dungeonId, sessionId, Guid.NewGuid(),
                    new[] { participantId });
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, started.Status);
                return (dungeonId, sessionId, participantId);
            }

            public Task<OperationResult> SaveStageAsync(Guid dungeonId, Guid sessionId, Guid characterId, int stage,
                int potions, bool talisman)
            {
                return Prologue.SaveStageAsync(new PrologueStageInput(dungeonId, sessionId, Guid.NewGuid(), characterId,
                    stage, potions, talisman));
            }
        }
    }
}
