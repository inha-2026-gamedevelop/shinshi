// System
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json.Linq;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Life;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 캐릭터 프로필의 저장 데이터, 계정 공용 상태, 삭제 정책과 읽기 전용 조회를 실제 PostgreSQL에서 검사한다 </summary>
    [Collection(ProgramEnvironmentCollection.NAME)]
    public sealed class AdminCharactersPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string REASON              = "profile incident 42";
        private const string NAME                = "FoxMain";
        private const string NAME_KEY            = "foxmain";
        private const string APPEARANCE          = "{\"hair\":2,\"skin\":1}";
        private const string EFFECT_LEARNED      = "T01-1A";
        private const string EFFECT_FAILED       = "T01-2B";
        private const string SKILL_ID            = "skill_profile";
        private const string UNLOCK_KIND         = "RegionCleared";
        private const string UNLOCK_TARGET       = "region_profile";
        private const string MAP_ID              = "map_profile";
        private const long   ESSENCE             = 123;
        private const long   SHARDS              = 45;
        private const long   CHARACTER_REVISION  = 7;
        private const long   LOADOUT_REVISION    = 11;
        private const long   CURRENCY            = 1234;
        private const long   CURRENCY_RESERVED   = 50;
        private const long   TICKETS             = 8;
        private const long   TICKETS_RESERVED    = 2;
        private const long   STORED_ACTION       = 18;
        private const long   WALLET_REVISION     = 13;
        private const int    TRAINING_ATTACK     = 10;
        private const int    TRAINING_HEALTH     = 20;
        private const int    TRAINING_DEFENSE    = 3;
        private const int    TRAINING_CRIT       = 4;
        private const int    TRAINING_ENERGY     = 5;
        private const int    TRAINING_ITEM_CAP   = 40;
        private const int    TRAINING_TOTAL_CAP  = 100;
        private const int    EQUIPPED_ENHANCE    = 7;
        private const int    STORED_ENHANCE      = 12;
        private const int    LEARNED_LEVEL       = 3;
        private const int    REGISTER_FAILURES   = 4;
        private const int    FOX_TRACES          = 13;
        private const long   PROGRESS_REVISION   = 3;
        private const long   HERBS               = 12;
        private const long   HERBS_RESERVED      = 2;
        private const long   POTIONS             = 9;
        private const long   POTIONS_RESERVED    = 1;
        private const int    SESSION_COUNT       = 6;
        private const int    RECENT_SESSION_CAP  = 5;
        private const int    CHARACTER_CLASS     = 2;
        private const long   RECOVER_INTERVAL_MS = 300000;

        private static readonly DateTimeOffset G_FixtureTime = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Detail_FullCharacter_ReturnsCharacterDataAndOnlyItsFiveLatestSessions()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            CharacterFixture fixture = await SeedFullAsync(database.DataSource);
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false);
            using HttpClient client = host.CreateHttpsClient();
            (Guid adminId, byte[] secret) = await host.CreateAdminAsync("profile.viewer", EAdminRole.Viewer);
            string token = await AdminApiTestHost.LoginAsync(client, "profile.viewer", secret);
            string before = await ReadStoredStateAsync(database.DataSource);

            using HttpResponseMessage response = await GetDetailAsync(client, token, fixture.CharacterId);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            AdminCharacterDetailResponse detail = await AdminTestSupport.ReadAsync<AdminCharacterDetailResponse>(response);
            Assert.Equal(fixture.CharacterId.ToString("D"), detail.CharacterId);
            Assert.Equal(fixture.AccountId.ToString("D"), detail.AccountId);
            Assert.Equal(NAME, detail.Name);
            Assert.Equal(CHARACTER_CLASS, detail.ClassId);
            Assert.Null(detail.ClassDisplayName);
            Assert.True(JToken.DeepEquals(JObject.Parse(APPEARANCE), JObject.Parse(detail.AppearanceJson)));
            Assert.Null(detail.CreatedAtUnixMs);
            Assert.Null(detail.DeletedAtUnixMs);
            Assert.Equal(ESSENCE, detail.Essence);
            Assert.Equal(SHARDS, detail.Shards);
            Assert.Equal(CHARACTER_REVISION, detail.Revision);
            Assert.Equal(LOADOUT_REVISION, detail.LoadoutRevision);
            Assert.Null(detail.CombatStats);
            AssertTraining(detail.Training);
            AssertEquipment(detail, fixture);
            AssertTalisman(detail);
            AssertAccountState(detail, fixture);

            Assert.Equal(RECENT_SESSION_CAP, detail.RecentSessions.Length);
            Assert.Equal(fixture.Sessions.Reverse().Take(RECENT_SESSION_CAP).Select(id => id.ToString("D")),
                detail.RecentSessions.Select(session => session.SessionId));
            Assert.All(detail.RecentSessions, session =>
            {
                Assert.Equal(fixture.CharacterId.ToString("D"), session.CharacterId);
                Assert.Equal("Normal", session.Mode);
                Assert.Equal(MAP_ID, session.MapId);
                Assert.Equal("Closed", session.SessionState);
                Assert.Equal("Settled", session.ParticipantState);
                Assert.Equal("Settled", session.Result);
                Assert.Equal(ESSENCE, session.EssenceAccrued);
                Assert.Equal(SHARDS, session.BaseEssenceAccrued);
                Assert.NotNull(session.StartedAtUnixMs);
                Assert.NotNull(session.ClosedAtUnixMs);
            });
            Assert.DoesNotContain(detail.RecentSessions, session => session.SessionId == fixture.SiblingSessionId.ToString("D"));
            Assert.Equal(before, await ReadStoredStateAsync(database.DataSource));
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                """
                SELECT count(*) FROM admin_audit_log
                WHERE action = @Action AND target_kind = @TargetKind AND target_id = @TargetId
                    AND admin_id = @AdminId AND reason = @Reason AND operation_id IS NULL
                    AND request_ip_hash IS NOT NULL;
                """, new { Action = AdminAuditActions.CHARACTER_VIEW, TargetKind = AdminAuditActions.TARGET_CHARACTER,
                    TargetId = fixture.CharacterId.ToString("D"), AdminId = adminId, Reason = REASON }));
        }

        [PostgresFact]
        public async Task Detail_SoftDeletedCharacterWithoutOptionalRows_ReturnsDeletionAndEmptyState()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            Guid characterId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO accounts (account_id) VALUES (@AccountId);
                    INSERT INTO characters (character_id, account_id, class_id, deleted_at, delete_operation_id)
                    VALUES (@CharacterId, @AccountId, 0, @DeletedAt, @DeleteOperationId);
                    """, new { AccountId = accountId, CharacterId = characterId, DeletedAt = G_FixtureTime,
                        DeleteOperationId = Guid.NewGuid() });
            }
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync("deleted.profile", EAdminRole.Viewer);
            string token = await AdminApiTestHost.LoginAsync(client, "deleted.profile", secret);
            string before = await ReadStoredStateAsync(database.DataSource);
            using HttpResponseMessage response = await GetDetailAsync(client, token, characterId);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AdminCharacterDetailResponse detail = await AdminTestSupport.ReadAsync<AdminCharacterDetailResponse>(response);
            Assert.Equal(characterId.ToString("D"), detail.CharacterId);
            Assert.Equal(G_FixtureTime.ToUnixTimeMilliseconds(), detail.DeletedAtUnixMs);
            Assert.Null(detail.Name);
            Assert.Null(detail.AppearanceJson);
            Assert.Null(detail.CreatedAtUnixMs);
            Assert.Null(detail.ClassDisplayName);
            Assert.Null(detail.CombatStats);
            Assert.NotNull(detail.Training);
            Assert.Equal(0, detail.Training.Attack);
            Assert.Equal(0, detail.Training.Health);
            Assert.Equal(0, detail.Training.Defense);
            Assert.Equal(0, detail.Training.Crit);
            Assert.Equal(0, detail.Training.Energy);
            Assert.Equal(0, detail.Training.Total);
            Assert.Equal(TRAINING_ITEM_CAP, detail.Training.ItemCap);
            Assert.Equal(TRAINING_TOTAL_CAP, detail.Training.TotalCap);
            Assert.Equal(0, detail.HighestEnhanceLevel);
            Assert.Empty(detail.Equipment);
            Assert.Empty(detail.TalismanProgress);
            Assert.Empty(detail.TalismanLoadout);
            Assert.NotNull(detail.AccountProgress);
            Assert.Null(detail.AccountProgress.IntroCompletedAtUnixMs);
            Assert.Null(detail.AccountProgress.FoxTraces);
            Assert.Null(detail.AccountProgress.Revision);
            Assert.Null(detail.AccountProgress.UpdatedAtUnixMs);
            Assert.Empty(detail.AccountProgress.Unlocks);
            Assert.Null(detail.Wallet);
            Assert.Null(detail.Lease);
            Assert.False(detail.Online);
            Assert.Empty(detail.Items);
            Assert.Empty(detail.RecentSessions);
            Assert.Empty(detail.OtherCharacters);
            Assert.Null(detail.Life.ActionValue);
            Assert.Null(detail.Life.ActionAnchorUnixMs);
            Assert.Null(detail.Life.NextRecoverAtUnixMs);
            Assert.Equal(LifeContent.ACTION_MAX, detail.Life.ActionCap);
            Assert.Empty(detail.Life.Herbs);
            Assert.Empty(detail.Life.Potions);
            Assert.Equal(before, await ReadStoredStateAsync(database.DataSource));
        }

        [PostgresFact]
        public async Task ReadDetail_ExpiredLeaseAndRecoveredActionCap_ReturnsOfflineWithoutWritingRecovery()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            CharacterFixture fixture = await SeedFullAsync(database.DataSource);
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE account_wallets SET action_value = @StoredAction,
                        action_anchor = clock_timestamp() - interval '1 hour' WHERE account_id = @AccountId;
                    UPDATE account_leases SET expires_at = clock_timestamp() - interval '1 hour'
                        WHERE account_id = @AccountId;
                    """, new { AccountId = fixture.AccountId, StoredAction = LifeContent.ACTION_MAX - 1 });
            }
            string before = await ReadStoredStateAsync(database.DataSource);
            PostgresAdminCharacterRepository repository = new PostgresAdminCharacterRepository(database.DataSource);

            AdminCharacterDetailResponse? detail = await repository.ReadDetailAsync(fixture.CharacterId);

            Assert.NotNull(detail);
            Assert.False(detail.Online);
            Assert.NotNull(detail.Lease);
            Assert.False(detail.Lease.Online);
            Assert.Equal(LifeContent.ACTION_MAX - 1, detail.Wallet.ActionValue);
            Assert.Equal(LifeContent.ACTION_MAX, detail.Life.ActionValue);
            Assert.Null(detail.Life.NextRecoverAtUnixMs);
            Assert.Equal(detail.Wallet.ActionAnchorUnixMs, detail.Life.ActionAnchorUnixMs);
            Assert.Equal(before, await ReadStoredStateAsync(database.DataSource));
        }

        [PostgresFact]
        public async Task Detail_MissingCharacter_PersistsAttemptAuditAndReturnsNotFound()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            using AdminApiTestHost host = new AdminApiTestHost(database.DataSource, false);
            using HttpClient client = host.CreateHttpsClient();
            (Guid _, byte[] secret) = await host.CreateAdminAsync("missing.profile", EAdminRole.Viewer);
            string token = await AdminApiTestHost.LoginAsync(client, "missing.profile", secret);
            Guid characterId = Guid.NewGuid();

            using HttpResponseMessage response = await GetDetailAsync(client, token, characterId);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(AdminContractCodes.RejectReason.NOT_FOUND,
                (await AdminTestSupport.ReadAsync<ApiRejection>(response)).ReasonCode);
            Assert.Equal(1L, await AdminTestSupport.CountAsync(database.DataSource,
                "SELECT count(*) FROM admin_audit_log WHERE action = @Action AND target_id = @TargetId;",
                new { Action = AdminAuditActions.CHARACTER_VIEW, TargetId = characterId.ToString("D") }));
            Assert.Equal(0L, await AdminTestSupport.CountAsync(database.DataSource, "SELECT count(*) FROM characters;"));
        }

        [PostgresFact]
        public async Task ReadDetail_UnlockWithoutProgressRow_ReturnsStoredUnlockAndUnknownProgress()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid accountId = Guid.NewGuid();
            Guid characterId = Guid.NewGuid();
            Guid operationId = Guid.NewGuid();
            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO accounts (account_id) VALUES (@AccountId);
                    INSERT INTO characters (character_id, account_id, class_id) VALUES (@CharacterId, @AccountId, 0);
                    INSERT INTO account_unlocks (account_id, unlock_kind, target_id, operation_id, unlocked_at)
                    VALUES (@AccountId, @UnlockKind, @UnlockTarget, @OperationId, @UnlockedAt);
                    """, new { AccountId = accountId, CharacterId = characterId, OperationId = operationId,
                        UnlockKind = UNLOCK_KIND, UnlockTarget = UNLOCK_TARGET, UnlockedAt = G_FixtureTime });
            }
            string before = await ReadStoredStateAsync(database.DataSource);
            PostgresAdminCharacterRepository repository = new PostgresAdminCharacterRepository(database.DataSource);

            AdminCharacterDetailResponse? detail = await repository.ReadDetailAsync(characterId);

            Assert.NotNull(detail);
            Assert.NotNull(detail.AccountProgress);
            Assert.Null(detail.AccountProgress.IntroCompletedAtUnixMs);
            Assert.Null(detail.AccountProgress.FoxTraces);
            Assert.Null(detail.AccountProgress.Revision);
            Assert.Null(detail.AccountProgress.UpdatedAtUnixMs);
            AdminCharacterUnlockView unlock = Assert.Single(detail.AccountProgress.Unlocks);
            Assert.Equal(UNLOCK_KIND, unlock.UnlockKind);
            Assert.Equal(UNLOCK_TARGET, unlock.TargetId);
            Assert.Equal(operationId.ToString("D"), unlock.OperationId);
            Assert.Equal(G_FixtureTime.ToUnixTimeMilliseconds(), unlock.UnlockedAtUnixMs);
            Assert.Equal(before, await ReadStoredStateAsync(database.DataSource));
        }

        private static void AssertTraining(AdminCharacterTrainingView training)
        {
            Assert.Equal(TRAINING_ATTACK, training.Attack);
            Assert.Equal(TRAINING_HEALTH, training.Health);
            Assert.Equal(TRAINING_DEFENSE, training.Defense);
            Assert.Equal(TRAINING_CRIT, training.Crit);
            Assert.Equal(TRAINING_ENERGY, training.Energy);
            Assert.Equal(TRAINING_ATTACK + TRAINING_HEALTH + TRAINING_DEFENSE + TRAINING_CRIT + TRAINING_ENERGY,
                training.Total);
            Assert.Equal(TRAINING_ITEM_CAP, training.ItemCap);
            Assert.Equal(TRAINING_TOTAL_CAP, training.TotalCap);
        }

        private static void AssertEquipment(AdminCharacterDetailResponse detail, CharacterFixture fixture)
        {
            Assert.Equal(2, detail.Equipment.Length);
            AdminCharacterEquipmentView equipped = Assert.Single(detail.Equipment, item => item.ItemId == fixture.EquippedId.ToString("D"));
            Assert.Equal("equip_profile_blade", equipped.DefinitionId);
            Assert.Equal(0, equipped.Slot);
            Assert.Equal(3, equipped.Grade);
            Assert.False(equipped.IsTraining);
            Assert.Equal("Craft", equipped.Origin);
            Assert.Equal(EQUIPPED_ENHANCE, equipped.Enhancement);
            Assert.True(equipped.Equipped);
            Assert.True(equipped.Locked);
            Assert.True(equipped.Bound);
            Assert.Equal(G_FixtureTime.ToUnixTimeMilliseconds(), equipped.CreatedAtUnixMs);
            AdminCharacterEquipmentView stored = Assert.Single(detail.Equipment, item => item.ItemId == fixture.StoredId.ToString("D"));
            Assert.Equal("equip_profile_armor", stored.DefinitionId);
            Assert.Equal(STORED_ENHANCE, stored.Enhancement);
            Assert.False(stored.Equipped);
            Assert.False(stored.Locked);
            Assert.False(stored.Bound);
            Assert.Equal(STORED_ENHANCE, detail.HighestEnhanceLevel);
        }

        private static void AssertTalisman(AdminCharacterDetailResponse detail)
        {
            Assert.Equal(2, detail.TalismanProgress.Length);
            AdminCharacterTalismanProgressView learned = Assert.Single(detail.TalismanProgress, item => item.EffectId == EFFECT_LEARNED);
            Assert.Equal(LEARNED_LEVEL, learned.Level);
            Assert.Equal(0, learned.RegisterFailures);
            Assert.Equal(G_FixtureTime.ToUnixTimeMilliseconds(), learned.UpdatedAtUnixMs);
            AdminCharacterTalismanProgressView failed = Assert.Single(detail.TalismanProgress, item => item.EffectId == EFFECT_FAILED);
            Assert.Equal(0, failed.Level);
            Assert.Equal(REGISTER_FAILURES, failed.RegisterFailures);
            AdminCharacterTalismanLoadoutView loadout = Assert.Single(detail.TalismanLoadout);
            Assert.Equal(SKILL_ID, loadout.SkillId);
            Assert.Equal(0, loadout.Tier);
            Assert.Equal(EFFECT_LEARNED, loadout.EffectId);
        }

        private static void AssertAccountState(AdminCharacterDetailResponse detail, CharacterFixture fixture)
        {
            Assert.Equal(CURRENCY, detail.Wallet.Currency);
            Assert.Equal(CURRENCY_RESERVED, detail.Wallet.CurrencyReserved);
            Assert.Equal(TICKETS, detail.Wallet.Tickets);
            Assert.Equal(TICKETS_RESERVED, detail.Wallet.TicketsReserved);
            Assert.Equal(STORED_ACTION, detail.Wallet.ActionValue);
            Assert.Equal(WALLET_REVISION, detail.Wallet.Revision);
            Assert.Equal(detail.Wallet.ActionAnchorUnixMs, detail.Life.ActionAnchorUnixMs);
            long intervals = (detail.Life.SnapshotAtUnixMs - detail.Wallet.ActionAnchorUnixMs) / RECOVER_INTERVAL_MS;
            Assert.Equal(STORED_ACTION + intervals, detail.Life.ActionValue);
            Assert.Equal(detail.Wallet.ActionAnchorUnixMs + ((intervals + 1) * RECOVER_INTERVAL_MS), detail.Life.NextRecoverAtUnixMs);
            Assert.Equal(LifeContent.ACTION_MAX, detail.Life.ActionCap);
            Assert.Equal(HERBS, Assert.Single(detail.Life.Herbs).Quantity);
            Assert.Equal(HERBS_RESERVED, Assert.Single(detail.Life.Herbs).Reserved);
            Assert.Equal(2, detail.Life.Potions.Length);
            Assert.Equal(POTIONS, Assert.Single(detail.Life.Potions, item => item.DefinitionId == LifeContent.POTION_SMALL_ID).Quantity);
            Assert.Equal(0, Assert.Single(detail.Life.Potions, item => item.DefinitionId == LifeContent.POTION_LARGE_ID).Quantity);
            Assert.Equal(4, detail.Items.Length);
            Assert.Equal(POTIONS_RESERVED, Assert.Single(detail.Items, item => item.DefinitionId == LifeContent.POTION_SMALL_ID).Reserved);
            Assert.Equal(G_FixtureTime.ToUnixTimeMilliseconds(), detail.AccountProgress.IntroCompletedAtUnixMs);
            Assert.Equal(FOX_TRACES, detail.AccountProgress.FoxTraces);
            Assert.Equal(PROGRESS_REVISION, detail.AccountProgress.Revision);
            Assert.Equal(G_FixtureTime.ToUnixTimeMilliseconds(), detail.AccountProgress.UpdatedAtUnixMs);
            AdminCharacterUnlockView unlock = Assert.Single(detail.AccountProgress.Unlocks);
            Assert.Equal(UNLOCK_KIND, unlock.UnlockKind);
            Assert.Equal(UNLOCK_TARGET, unlock.TargetId);
            Assert.Equal(fixture.UnlockOperationId.ToString("D"), unlock.OperationId);
            Assert.Equal(G_FixtureTime.ToUnixTimeMilliseconds(), unlock.UnlockedAtUnixMs);
            Assert.True(detail.Online);
            Assert.True(detail.Lease.Online);
            Assert.Equal(fixture.LeaseId.ToString("D"), detail.Lease.LeaseId);
            Assert.Equal(fixture.InstanceId.ToString("D"), detail.Lease.InstanceId);
            Assert.Equal(1, detail.Lease.Role);
            Assert.Equal(2, detail.OtherCharacters.Length);
            Assert.DoesNotContain(detail.OtherCharacters, character => character.CharacterId == detail.CharacterId);
            AdminCharacterSummaryView deleted = Assert.Single(detail.OtherCharacters, character => character.CharacterId == fixture.DeletedSiblingId.ToString("D"));
            Assert.Equal("FoxDeleted", deleted.Name);
            Assert.Equal(G_FixtureTime.ToUnixTimeMilliseconds(), deleted.DeletedAtUnixMs);
            Assert.Null(deleted.CreatedAtUnixMs);
            AdminCharacterSummaryView alive = Assert.Single(detail.OtherCharacters, character => character.CharacterId == fixture.SiblingId.ToString("D"));
            Assert.Equal("FoxSibling", alive.Name);
            Assert.Null(alive.DeletedAtUnixMs);
        }

        private static Task<HttpResponseMessage> GetDetailAsync(HttpClient client, string token, Guid characterId)
        {
            return AdminApiTestHost.SendAsync(client, HttpMethod.Get,
                string.Format(AdminApiRoutes.CHARACTER_FORMAT, characterId.ToString("D")) + "?reason=" + Uri.EscapeDataString(REASON), token);
        }

        private static async Task<string> ReadStoredStateAsync(NpgsqlDataSource dataSource)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<string>(
                """
                SELECT jsonb_build_object(
                    'characters', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.character_id) FROM characters AS stored),
                    'wallets', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.account_id) FROM account_wallets AS stored),
                    'items', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.account_id, stored.definition_id, stored.binding_kind) FROM account_items AS stored),
                    'training', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.character_id) FROM character_training AS stored),
                    'equipment', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.item_id) FROM equipment_instances AS stored),
                    'talisman', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.character_id, stored.effect_id) FROM talisman_progress AS stored),
                    'loadout', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.character_id, stored.skill_id, stored.tier) FROM talisman_loadout AS stored),
                    'progress', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.account_id) FROM account_progress AS stored),
                    'unlocks', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.account_id, stored.unlock_kind, stored.target_id) FROM account_unlocks AS stored),
                    'leases', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.account_id) FROM account_leases AS stored),
                    'sessions', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.session_id) FROM game_sessions AS stored),
                    'participants', (SELECT jsonb_agg(to_jsonb(stored) ORDER BY stored.participant_id) FROM session_participants AS stored),
                    'ledgerCount', (SELECT count(*) FROM economy_ledger),
                    'operationCount', (SELECT count(*) FROM operations))::text;
                """);
        }

        private static async Task<CharacterFixture> SeedFullAsync(NpgsqlDataSource dataSource)
        {
            CharacterFixture fixture = new CharacterFixture();
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO accounts (account_id) VALUES (@AccountId), (@ForeignAccountId);
                INSERT INTO characters (character_id, account_id, class_id, name, name_key, appearance,
                    essence, shards, revision, loadout_revision)
                VALUES (@CharacterId, @AccountId, @CharacterClass, @Name, @NameKey, CAST(@Appearance AS jsonb),
                    @Essence, @Shards, @CharacterRevision, @LoadoutRevision),
                    (@SiblingId, @AccountId, 0, 'FoxSibling', 'foxsibling', NULL, 0, 0, 0, 0),
                    (@DeletedSiblingId, @AccountId, 1, 'FoxDeleted', 'foxdeleted', NULL, 0, 0, 0, 0),
                    (@ForeignCharacterId, @ForeignAccountId, 0, 'FoxForeign', 'foxforeign', NULL, 0, 0, 0, 0);
                UPDATE characters SET deleted_at = @FixtureTime, delete_operation_id = @DeleteOperationId
                    WHERE character_id = @DeletedSiblingId;
                INSERT INTO account_wallets (account_id, currency, currency_reserved, tickets, tickets_reserved,
                    action_value, action_anchor, revision)
                VALUES (@AccountId, @Currency, @CurrencyReserved, @Tickets, @TicketsReserved,
                    @StoredAction, clock_timestamp() - interval '650 seconds', @WalletRevision);
                INSERT INTO character_training (character_id, attack, health, defense, crit, energy)
                VALUES (@CharacterId, @Attack, @Health, @Defense, @Crit, @Energy);
                INSERT INTO equipment_instances (item_id, account_id, character_id, definition_id, slot, grade,
                    is_training, origin, enhancement, equipped, locked, bound, created_at)
                VALUES (@EquippedId, @AccountId, @CharacterId, 'equip_profile_blade', 0, 3, false, 'Craft', @EquippedEnhance, true, true, true, @FixtureTime),
                    (@StoredId, @AccountId, @CharacterId, 'equip_profile_armor', 1, 1, false, 'Craft', @StoredEnhance, false, false, false, @FixtureTime),
                    (@SiblingItemId, @AccountId, @SiblingId, 'equip_sibling', 0, 3, false, 'Craft', 25, false, false, false, @FixtureTime);
                INSERT INTO talisman_progress (character_id, effect_id, level, register_failures, updated_at)
                VALUES (@CharacterId, @LearnedEffect, @LearnedLevel, 0, @FixtureTime),
                    (@CharacterId, @FailedEffect, 0, @RegisterFailures, @FixtureTime),
                    (@SiblingId, 'T02-1A', 5, 0, @FixtureTime);
                INSERT INTO talisman_loadout (character_id, skill_id, tier, effect_id)
                VALUES (@CharacterId, @SkillId, 0, @LearnedEffect);
                INSERT INTO account_items (account_id, definition_id, binding_kind, quantity, reserved)
                VALUES (@AccountId, @HerbId, 'Account', @Herbs, @HerbsReserved),
                    (@AccountId, @PotionSmallId, 'Account', @Potions, @PotionsReserved),
                    (@AccountId, @PotionLargeId, 'Account', 0, 0),
                    (@AccountId, 'item_material', 'Account', 5, 0),
                    (@ForeignAccountId, @HerbId, 'Account', 999, 0);
                INSERT INTO account_progress (account_id, intro_completed_at, fox_traces, revision, updated_at)
                VALUES (@AccountId, @FixtureTime, @FoxTraces, @ProgressRevision, @FixtureTime);
                INSERT INTO account_unlocks (account_id, unlock_kind, target_id, operation_id, unlocked_at)
                VALUES (@AccountId, @UnlockKind, @UnlockTarget, @UnlockOperationId, @FixtureTime);
                INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state)
                VALUES (@InstanceId, 1, '127.0.0.1', 7777, 'profile-test', 'profile-content', 'Active');
                INSERT INTO account_leases (account_id, lease_id, instance_id, role, expires_at)
                VALUES (@AccountId, @LeaseId, @InstanceId, 1, clock_timestamp() + interval '1 hour');
                """, new
                {
                    fixture.AccountId, fixture.CharacterId, fixture.SiblingId, fixture.DeletedSiblingId,
                    fixture.ForeignAccountId, fixture.ForeignCharacterId, fixture.EquippedId, fixture.StoredId,
                    fixture.SiblingItemId, fixture.LeaseId, fixture.InstanceId, fixture.UnlockOperationId,
                    DeleteOperationId = Guid.NewGuid(), FixtureTime = G_FixtureTime, CharacterClass = CHARACTER_CLASS,
                    Name = NAME, NameKey = NAME_KEY, Appearance = APPEARANCE, Essence = ESSENCE, Shards = SHARDS,
                    CharacterRevision = CHARACTER_REVISION, LoadoutRevision = LOADOUT_REVISION,
                    Currency = CURRENCY, CurrencyReserved = CURRENCY_RESERVED, Tickets = TICKETS,
                    TicketsReserved = TICKETS_RESERVED, StoredAction = STORED_ACTION, WalletRevision = WALLET_REVISION,
                    Attack = TRAINING_ATTACK, Health = TRAINING_HEALTH, Defense = TRAINING_DEFENSE,
                    Crit = TRAINING_CRIT, Energy = TRAINING_ENERGY, EquippedEnhance = EQUIPPED_ENHANCE,
                    StoredEnhance = STORED_ENHANCE, LearnedEffect = EFFECT_LEARNED, FailedEffect = EFFECT_FAILED,
                    LearnedLevel = LEARNED_LEVEL, RegisterFailures = REGISTER_FAILURES, SkillId = SKILL_ID,
                    HerbId = LifeContent.HERB_ID, PotionSmallId = LifeContent.POTION_SMALL_ID,
                    PotionLargeId = LifeContent.POTION_LARGE_ID, Herbs = HERBS, HerbsReserved = HERBS_RESERVED,
                    Potions = POTIONS, PotionsReserved = POTIONS_RESERVED, FoxTraces = FOX_TRACES,
                    ProgressRevision = PROGRESS_REVISION, UnlockKind = UNLOCK_KIND, UnlockTarget = UNLOCK_TARGET
                });
            for (int index = 0; index < SESSION_COUNT; ++index)
            {
                await SeedSessionAsync(connection, fixture, fixture.Sessions[index], fixture.CharacterId,
                    G_FixtureTime.AddMinutes(index));
            }
            await SeedSessionAsync(connection, fixture, fixture.SiblingSessionId, fixture.SiblingId,
                G_FixtureTime.AddDays(1));
            return fixture;
        }

        private static Task SeedSessionAsync(NpgsqlConnection connection, CharacterFixture fixture,
            Guid sessionId, Guid characterId, DateTimeOffset createdAt)
        {
            return connection.ExecuteAsync(
                """
                INSERT INTO game_sessions (session_id, mode, map_id, state, starting_count, content_version,
                    instance_id, server_lease, created_at, started_at, closed_at, result)
                VALUES (@SessionId, 'Normal', @MapId, 'Closed', 1, 'profile-content', @InstanceId,
                    @ServerLease, @CreatedAt, @CreatedAt, @CreatedAt, 'Settled');
                INSERT INTO session_participants (session_id, participant_id, account_id, character_id, state,
                    essence_accrued, base_essence_accrued, settled_at)
                VALUES (@SessionId, @ParticipantId, @AccountId, @CharacterId, 'Settled', @Essence, @BaseEssence, @CreatedAt);
                """, new { SessionId = sessionId, ParticipantId = Guid.NewGuid(), fixture.AccountId,
                    CharacterId = characterId, fixture.InstanceId, ServerLease = Guid.NewGuid(), CreatedAt = createdAt,
                    MapId = MAP_ID, Essence = ESSENCE, BaseEssence = SHARDS });
        }

        private sealed class CharacterFixture
        {
            public Guid AccountId { get; } = Guid.NewGuid();
            public Guid CharacterId { get; } = Guid.NewGuid();
            public Guid SiblingId { get; } = Guid.NewGuid();
            public Guid DeletedSiblingId { get; } = Guid.NewGuid();
            public Guid ForeignAccountId { get; } = Guid.NewGuid();
            public Guid ForeignCharacterId { get; } = Guid.NewGuid();
            public Guid EquippedId { get; } = Guid.NewGuid();
            public Guid StoredId { get; } = Guid.NewGuid();
            public Guid SiblingItemId { get; } = Guid.NewGuid();
            public Guid InstanceId { get; } = Guid.NewGuid();
            public Guid LeaseId { get; } = Guid.NewGuid();
            public Guid UnlockOperationId { get; } = Guid.NewGuid();
            public Guid SiblingSessionId { get; } = Guid.NewGuid();
            public Guid[] Sessions { get; } = Enumerable.Range(0, SESSION_COUNT).Select(_ => Guid.NewGuid()).ToArray();
        }
    }
}

