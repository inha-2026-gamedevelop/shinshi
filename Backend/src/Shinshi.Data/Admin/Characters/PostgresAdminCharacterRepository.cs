// System
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Life;

namespace Shinshi.Data.Admin
{
    /// <summary> 캐릭터 상세를 REPEATABLE READ 읽기 전용 스냅샷의 고정 SELECT 묶음으로 조회한다 </summary>
    public sealed class PostgresAdminCharacterRepository : IAdminCharacterRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string DETAIL_SQL =
            """
            SELECT character_id AS "CharacterId", account_id AS "AccountId", name AS "Name", class_id AS "ClassId",
                appearance::text AS "AppearanceJson", deleted_at AS "DeletedAt", essence AS "Essence",
                shards AS "Shards", revision AS "Revision", loadout_revision AS "LoadoutRevision",
                transaction_timestamp() AS "SnapshotAt"
            FROM characters WHERE character_id = @CharacterId;

            SELECT attack AS "Attack", health AS "Health", defense AS "Defense", crit AS "Crit", energy AS "Energy",
                attack + health + defense + crit + energy AS "Total", @ItemCap AS "ItemCap", @TotalCap AS "TotalCap"
            FROM character_training WHERE character_id = @CharacterId;

            SELECT item_id::text AS "ItemId", definition_id AS "DefinitionId", slot AS "Slot", grade AS "Grade",
                is_training AS "IsTraining", origin AS "Origin", enhancement AS "Enhancement",
                equipped AS "Equipped", locked AS "Locked", bound AS "Bound",
                floor(extract(epoch FROM created_at) * 1000)::bigint AS "CreatedAtUnixMs"
            FROM equipment_instances WHERE character_id = @CharacterId ORDER BY slot, item_id;

            SELECT effect_id AS "EffectId", level AS "Level", register_failures AS "RegisterFailures",
                floor(extract(epoch FROM updated_at) * 1000)::bigint AS "UpdatedAtUnixMs"
            FROM talisman_progress WHERE character_id = @CharacterId ORDER BY effect_id;

            SELECT skill_id AS "SkillId", tier AS "Tier", effect_id AS "EffectId"
            FROM talisman_loadout WHERE character_id = @CharacterId ORDER BY skill_id, tier;

            SELECT intro_completed_at AS "IntroCompletedAt", fox_traces AS "FoxTraces",
                revision AS "Revision", updated_at AS "UpdatedAt"
            FROM account_progress
            WHERE account_id = (SELECT account_id FROM characters WHERE character_id = @CharacterId);

            SELECT unlock_kind AS "UnlockKind", target_id AS "TargetId", operation_id::text AS "OperationId",
                floor(extract(epoch FROM unlocked_at) * 1000)::bigint AS "UnlockedAtUnixMs"
            FROM account_unlocks
            WHERE account_id = (SELECT account_id FROM characters WHERE character_id = @CharacterId)
            ORDER BY unlock_kind, target_id;

            SELECT currency AS "Currency", currency_reserved AS "CurrencyReserved", tickets AS "Tickets",
                tickets_reserved AS "TicketsReserved", action_value AS "ActionValue",
                action_anchor AS "ActionAnchor", revision AS "Revision"
            FROM account_wallets
            WHERE account_id = (SELECT account_id FROM characters WHERE character_id = @CharacterId);

            SELECT definition_id AS "DefinitionId", binding_kind AS "BindingKind",
                quantity AS "Quantity", reserved AS "Reserved"
            FROM account_items
            WHERE account_id = (SELECT account_id FROM characters WHERE character_id = @CharacterId)
            ORDER BY definition_id, binding_kind;

            SELECT lease_id::text AS "LeaseId", instance_id::text AS "InstanceId", role AS "Role",
                floor(extract(epoch FROM expires_at) * 1000)::bigint AS "ExpiresAtUnixMs",
                expires_at > transaction_timestamp() AS "Online"
            FROM account_leases
            WHERE account_id = (SELECT account_id FROM characters WHERE character_id = @CharacterId);

            SELECT session.session_id::text AS "SessionId", participant.participant_id::text AS "ParticipantId",
                participant.character_id::text AS "CharacterId", session.mode AS "Mode", session.map_id AS "MapId",
                session.state AS "SessionState", participant.state AS "ParticipantState", session.result AS "Result",
                floor(extract(epoch FROM session.created_at) * 1000)::bigint AS "CreatedAtUnixMs",
                floor(extract(epoch FROM session.started_at) * 1000)::bigint AS "StartedAtUnixMs",
                floor(extract(epoch FROM session.closed_at) * 1000)::bigint AS "ClosedAtUnixMs",
                participant.essence_accrued AS "EssenceAccrued",
                participant.base_essence_accrued AS "BaseEssenceAccrued"
            FROM session_participants AS participant
            JOIN game_sessions AS session ON session.session_id = participant.session_id
            WHERE participant.character_id = @CharacterId
            ORDER BY session.created_at DESC, session.session_id
            LIMIT @SessionLimit;

            SELECT character_id::text AS "CharacterId", name AS "Name", class_id AS "ClassId",
                NULL::bigint AS "CreatedAtUnixMs",
                floor(extract(epoch FROM deleted_at) * 1000)::bigint AS "DeletedAtUnixMs"
            FROM characters
            WHERE account_id = (SELECT account_id FROM characters WHERE character_id = @CharacterId)
                AND character_id <> @CharacterId
            ORDER BY character_id;
            """;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminCharacterRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        public async Task<AdminCharacterDetailResponse?> ReadDetailAsync(Guid characterId,
            CancellationToken cancellationToken = default)
        {
            if (characterId == Guid.Empty)
            {
                throw new ArgumentException("Character ID is required.", nameof(characterId));
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY;", transaction: transaction,
                cancellationToken: cancellationToken));
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
            using SqlMapper.GridReader results = await connection.QueryMultipleAsync(new CommandDefinition(DETAIL_SQL,
                new
                {
                    CharacterId = characterId,
                    ItemCap = AdminCharacterProfileLimits.TRAINING_ITEM_CAP,
                    TotalCap = AdminCharacterProfileLimits.TRAINING_TOTAL_CAP,
                    SessionLimit = AdminCharacterProfileLimits.RECENT_SESSION_LIMIT
                }, transaction, cancellationToken: cancellationToken));

            CharacterRow? character = await results.ReadSingleOrDefaultAsync<CharacterRow>();
            if (character == null)
            {
                return null;
            }

            AdminCharacterTrainingView training = await results.ReadSingleOrDefaultAsync<AdminCharacterTrainingView>()
                ?? new AdminCharacterTrainingView(0, 0, 0, 0, 0, 0, AdminCharacterProfileLimits.TRAINING_ITEM_CAP,
                    AdminCharacterProfileLimits.TRAINING_TOTAL_CAP);
            AdminCharacterEquipmentView[] equipment = (await results.ReadAsync<AdminCharacterEquipmentView>()).ToArray();
            AdminCharacterTalismanProgressView[] progress =
                (await results.ReadAsync<AdminCharacterTalismanProgressView>()).ToArray();
            AdminCharacterTalismanLoadoutView[] loadout =
                (await results.ReadAsync<AdminCharacterTalismanLoadoutView>()).ToArray();
            AccountProgressRow? accountProgress = await results.ReadSingleOrDefaultAsync<AccountProgressRow>();
            AdminCharacterUnlockView[] unlocks = (await results.ReadAsync<AdminCharacterUnlockView>()).ToArray();
            WalletRow? walletRow = await results.ReadSingleOrDefaultAsync<WalletRow>();
            AdminAccountItemView[] items = (await results.ReadAsync<AdminAccountItemView>()).ToArray();
            AdminAccountLeaseView? lease = await results.ReadSingleOrDefaultAsync<AdminAccountLeaseView>();
            AdminAccountSessionView[] sessions = (await results.ReadAsync<AdminAccountSessionView>()).ToArray();
            AdminCharacterSummaryView[] others = (await results.ReadAsync<AdminCharacterSummaryView>()).ToArray();
            await transaction.CommitAsync(cancellationToken);

            DateTimeOffset snapshotAt = ToUtc(character.SnapshotAt);
            AdminCharacterWalletView? wallet = walletRow == null ? null : new AdminCharacterWalletView(
                walletRow.Currency, walletRow.CurrencyReserved, walletRow.Tickets, walletRow.TicketsReserved,
                walletRow.ActionValue, ToUtc(walletRow.ActionAnchor).ToUnixTimeMilliseconds(), walletRow.Revision);
            AdminCharacterLifeView life = ToLife(walletRow, items, snapshotAt);
            int highestEnhanceLevel = equipment.Length == 0 ? 0 : equipment.Max(item => item.Enhancement);

            // characters에는 생성 시각 열이 없고 백엔드 콘텐츠에는 직업 표시명과 직업 기본 능력치가 없다
            return new AdminCharacterDetailResponse(character.CharacterId.ToString("D"),
                character.AccountId.ToString("D"), character.Name, character.ClassId, null, character.AppearanceJson,
                null, ToUnixMs(character.DeletedAt), character.Essence, character.Shards, character.Revision,
                character.LoadoutRevision, training, null, equipment, highestEnhanceLevel, progress, loadout,
                new AdminCharacterAccountProgressView(
                    ToUnixMs(accountProgress?.IntroCompletedAt), accountProgress?.FoxTraces, accountProgress?.Revision,
                    ToUnixMs(accountProgress?.UpdatedAt), unlocks),
                life, wallet, items, lease, lease?.Online ?? false, sessions, others);
        }

        private static AdminCharacterLifeView ToLife(WalletRow? wallet, AdminAccountItemView[] items,
            DateTimeOffset snapshotAt)
        {
            ActionEnergyState? energy = wallet == null ? null : ActionEnergyRules.Recover(wallet.ActionValue,
                ToUtc(wallet.ActionAnchor), snapshotAt);
            DateTimeOffset? nextRecoverAt = energy.HasValue ? ActionEnergyRules.NextRecoverAt(energy.Value) : null;
            AdminAccountItemView[] herbs = items.Where(item => (item.BindingKind == LifeContent.BINDING_KIND)
                && (item.DefinitionId == LifeContent.HERB_ID)).ToArray();
            AdminAccountItemView[] potions = items.Where(item => (item.BindingKind == LifeContent.BINDING_KIND)
                && LifeContent.PotionIds.Contains(item.DefinitionId)).ToArray();
            return new AdminCharacterLifeView(energy?.Value, LifeContent.ACTION_MAX, ToUnixMs(wallet?.ActionAnchor),
                nextRecoverAt?.ToUnixTimeMilliseconds(), snapshotAt.ToUnixTimeMilliseconds(), herbs, potions);
        }

        private static DateTimeOffset ToUtc(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        private static long? ToUnixMs(DateTime? value)
        {
            return value.HasValue ? ToUtc(value.Value).ToUnixTimeMilliseconds() : null;
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class CharacterRow
        {
            public Guid CharacterId { get; set; }
            public Guid AccountId { get; set; }
            public string? Name { get; set; }
            public int ClassId { get; set; }
            public string? AppearanceJson { get; set; }
            public DateTime? DeletedAt { get; set; }
            public long Essence { get; set; }
            public long Shards { get; set; }
            public long Revision { get; set; }
            public long LoadoutRevision { get; set; }
            public DateTime SnapshotAt { get; set; }
        }

        private sealed class WalletRow
        {
            public long Currency { get; set; }
            public long CurrencyReserved { get; set; }
            public long Tickets { get; set; }
            public long TicketsReserved { get; set; }
            public long ActionValue { get; set; }
            public DateTime ActionAnchor { get; set; }
            public long Revision { get; set; }
        }

        private sealed class AccountProgressRow
        {
            public DateTime? IntroCompletedAt { get; set; }
            public int FoxTraces { get; set; }
            public long Revision { get; set; }
            public DateTime UpdatedAt { get; set; }
        }
    }
}

