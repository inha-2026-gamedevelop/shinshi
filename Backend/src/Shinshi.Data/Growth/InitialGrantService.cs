// System
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Growth
{
    /// <summary> 캐릭터 생성 트랜잭션 안의 최초 지원. 계정 최초분은 계정에 한 번, 캐릭터분은 캐릭터마다 한 번이며
    /// 둘 다 부분 유일 인덱스에 먼저 넣은 쪽만 지급한다. 기존 계정, 캐릭터에는 소급하지 않는다 </summary>
    public sealed class InitialGrantService
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ACTOR_KIND        = "System";
        private const string FAMILY_ACCOUNT    = "AccountFirst";
        private const string FAMILY_CHARACTER  = "CharacterFirst";
        private const string PROFILE_TRIAL     = "Trial";
        private const string PROFILE_STANDARD  = "Standard";

        private readonly GrowthContent _content;
        private readonly IEconomyLedgerEntryWriter _ledger;
        private readonly PostgresItemHistoryWriter _history;

        /****************************************
        *              Properties
        ****************************************/

        public EGrowthContentProfile Profile { get; }
        public long InitialExperience => Profile == EGrowthContentProfile.Standard ? 0
            : Shinshi.Contracts.CharacterLevelRules.RequiredExperience(_content.CharacterLevel, _content.CharacterLevel.TrialStartLevel);

        /****************************************
        *                Methods
        ****************************************/

        public InitialGrantService(GrowthContent content, EGrowthContentProfile profile, IEconomyLedgerEntryWriter ledger,
            PostgresItemHistoryWriter history)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(ledger);
            ArgumentNullException.ThrowIfNull(history);
            _content = content;
            Profile  = profile;
            _ledger  = ledger;
            _history = history;
        }

        /// <summary> 캐릭터 행을 넣은 같은 작업 트랜잭션에서 부른다. 호출자가 계정 행을 먼저 잠가야 동시 생성이 계정분을 한 번만 받는다.
        /// actor는 생성 작업의 actor(계정)이고 원장 행위자 종류는 System이다 </summary>
        public async Task ApplyAsync(OperationExecutionContext context, Guid accountId, Guid characterId, int classId,
            Guid operationId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            GrowthEffectContext grant = new GrowthEffectContext(context, _content, _ledger, _history, accountId, ACTOR_KIND,
                operationId, accountId, characterId, classId, ContractCodes.EconomyReason.INITIAL_GRANT);

            GrowthContent.GrantBundle accountGrant = _content.GetAccountGrant(Profile);
            if (await ClaimAsync(connection, transaction, FAMILY_ACCOUNT, accountId, null, accountGrant.GrantId,
                    operationId, cancellationToken))
            {
                await GrantItemsAsync(grant, accountGrant, cancellationToken);

                // 행동력은 원장 자산이 아니다. 기준 시각을 지금으로 두어 회복 계산이 생성 시점부터 시작한다
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO account_wallets AS wallet (account_id, action_value, action_anchor)
                    VALUES (@AccountId, @Action, clock_timestamp())
                    ON CONFLICT (account_id) DO UPDATE
                        SET action_value = EXCLUDED.action_value, action_anchor = EXCLUDED.action_anchor,
                            revision = wallet.revision + 1;
                    """, new { AccountId = accountId, Action = accountGrant.ActionEnergy }, transaction,
                    cancellationToken: cancellationToken));
            }

            GrowthContent.GrantBundle characterGrant = _content.GetCharacterGrant(Profile);
            if (await ClaimAsync(connection, transaction, FAMILY_CHARACTER, accountId, characterId,
                    characterGrant.GrantId, operationId, cancellationToken))
            {
                await GrantItemsAsync(grant, characterGrant, cancellationToken);
                await GrantEquipmentAsync(grant, characterGrant, cancellationToken);
            }
        }

        private async Task<bool> ClaimAsync(DbConnection connection, DbTransaction transaction, string family,
            Guid accountId, Guid? characterId, string grantId, Guid operationId, CancellationToken cancellationToken)
        {
            string conflict = characterId.HasValue
                ? "ON CONFLICT (character_id) WHERE family = 'CharacterFirst' DO NOTHING"
                : "ON CONFLICT (account_id) WHERE family = 'AccountFirst' DO NOTHING";
            Guid? claimed = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                """
                INSERT INTO initial_grants (grant_row_id, family, account_id, character_id, grant_id, profile,
                    content_version, actor_id, operation_id)
                VALUES (@RowId, @Family, @AccountId, @CharacterId, @GrantId, @Profile, @ContentVersion, @AccountId, @OperationId)
                """ + "\n" + conflict + " RETURNING grant_row_id;", new
                {
                    RowId = Guid.NewGuid(),
                    Family = family,
                    AccountId = accountId,
                    CharacterId = characterId,
                    GrantId = grantId,
                    Profile = Profile == EGrowthContentProfile.Standard ? PROFILE_STANDARD : PROFILE_TRIAL,
                    _content.ContentVersion,
                    OperationId = operationId
                }, transaction, cancellationToken: cancellationToken));
            return claimed.HasValue;
        }

        private static async Task GrantItemsAsync(GrowthEffectContext grant, GrowthContent.GrantBundle bundle,
            CancellationToken cancellationToken)
        {
            foreach (GrowthContent.ItemGrant item in bundle.Items)
            {
                await grant.CreditAsync(ToAsset(grant, item.ItemId), item.Quantity, cancellationToken);
            }
        }

        private async Task GrantEquipmentAsync(GrowthEffectContext grant, GrowthContent.GrantBundle bundle,
            CancellationToken cancellationToken)
        {
            // TODO(민성): 체험판 캐릭터가 수련용 6부위도 함께 받는지는 기획 미정(docs/design/growth-economy.md 4장).
            // 확정 전에는 프로필 묶음의 장비만 착용 상태로 지급한다
            string origin = Profile == EGrowthContentProfile.Standard
                ? GrowthCodes.EquipmentOrigin.INITIAL_GRANT : GrowthCodes.EquipmentOrigin.TRIAL_GRANT;
            foreach (GrowthContent.EquipmentGrant equipment in bundle.Equipment)
            {
                if (!_content.TryGetEquipment(equipment.EquipmentId, out GrowthContent.EquipmentDefinition definition))
                {
                    throw new InvalidOperationException("Initial grant references unknown equipment.");
                }

                await grant.InsertEquipmentAsync(new EquipmentRecord
                {
                    ItemId = Guid.NewGuid(),
                    AccountId = grant.AccountId,
                    CharacterId = grant.CharacterId,
                    DefinitionId = definition.DefinitionId,
                    Slot = definition.Slot,
                    Grade = definition.Grade,
                    IsTraining = definition.IsTraining,
                    Origin = origin,
                    Enhancement = equipment.Enhancement,
                    Equipped = true,
                    Bound = true
                }, GrowthAssets.HISTORY_GRANTED, cancellationToken);
            }
        }

        // 지갑 자산 ID는 아이템 줄로 우회하지 않고 해당 잔액으로 지급한다
        private static EconomyAsset ToAsset(GrowthEffectContext grant, string itemId)
        {
            return itemId switch
            {
                GrowthAssets.ESSENCE_ITEM_ID  => grant.Essence(),
                GrowthAssets.SHARD_ITEM_ID    => grant.Shards(),
                GrowthAssets.TICKET_ITEM_ID   => EconomyAsset.Tickets(grant.AccountId),
                GrowthAssets.CURRENCY_ITEM_ID => EconomyAsset.Currency(grant.AccountId),
                _ => grant.AccountItem(itemId)
            };
        }
    }
}
