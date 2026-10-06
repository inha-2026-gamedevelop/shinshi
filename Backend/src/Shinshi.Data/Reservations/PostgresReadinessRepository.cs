// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Growth;
using Shinshi.Data.Inbox;

namespace Shinshi.Data.Reservations
{
    /// <summary> 마을 서버의 출정 준비 확인. 읽기만 하고 잠그지 않으며, 확정 판정은 예약이 같은 조건을 다시 본다 </summary>
    public sealed class PostgresReadinessRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // unnest WITH ORDINALITY는 1부터 센다
        private const int ORDINAL_BASE = 1;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresReadinessRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 파티 1~4명, 계정과 캐릭터가 겹치지 않고 빈 ID가 없어야 한다 </summary>
        public static bool IsValid(IReadOnlyList<(Guid AccountId, Guid CharacterId)> participants)
        {
            if ((participants == null) || (participants.Count < ReservationInput.MIN_PARTY_SIZE)
                || (participants.Count > ReservationInput.MAX_PARTY_SIZE))
            {
                return false;
            }
            if (participants.Any(entry => (entry.AccountId == Guid.Empty) || (entry.CharacterId == Guid.Empty)))
            {
                return false;
            }

            return (participants.Select(entry => entry.AccountId).Distinct().Count() == participants.Count)
                && (participants.Select(entry => entry.CharacterId).Distinct().Count() == participants.Count);
        }

        /// <summary> 요청 순서대로 상태를 돌려준다. 이 마을에 살아 있는 접속 임대가 없는 계정은 다른 값을 알려 주지 않는다
        /// (마을 서버가 접속하지 않은 계정의 인벤토리를 엿보지 못하게 한다) </summary>
        public async Task<IReadOnlyList<ReadinessParticipantStatus>> ReadAsync(Guid townInstanceId,
            IReadOnlyList<(Guid AccountId, Guid CharacterId)> participants, CancellationToken cancellationToken = default)
        {
            if ((townInstanceId == Guid.Empty) || !IsValid(participants))
            {
                throw new ArgumentException("Readiness request is invalid.");
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            List<ParticipantRow> rows = (await connection.QueryAsync<ParticipantRow>(new CommandDefinition(
                """
                SELECT wanted.ordinal AS "Ordinal", hero.loadout_revision AS "Revision",
                    EXISTS (SELECT 1 FROM account_leases AS lease
                        WHERE lease.account_id = wanted.account_id AND lease.instance_id = @Town
                            AND lease.expires_at > clock_timestamp()) AS "Leased",
                    EXISTS (SELECT 1 FROM session_participants AS participant
                        WHERE participant.account_id = wanted.account_id
                            AND participant.state IN ('Reserved', 'Active')) AS "Busy",
                    COALESCE(wallet.tickets - wallet.tickets_reserved, 0) AS "AvailableTickets"
                FROM unnest(@Accounts::uuid[], @Characters::uuid[]) WITH ORDINALITY
                    AS wanted(account_id, character_id, ordinal)
                LEFT JOIN characters AS hero ON hero.character_id = wanted.character_id
                    AND hero.account_id = wanted.account_id AND hero.deleted_at IS NULL
                LEFT JOIN account_wallets AS wallet ON wallet.account_id = wanted.account_id
                ORDER BY wanted.ordinal;
                """, new
                {
                    Town = townInstanceId,
                    Accounts = participants.Select(entry => entry.AccountId).ToArray(),
                    Characters = participants.Select(entry => entry.CharacterId).ToArray()
                }, cancellationToken: cancellationToken))).ToList();

            Guid[] visible = rows.Where(row => row.Leased && row.Revision.HasValue)
                .Select(row => participants[(int)row.Ordinal - ORDINAL_BASE].AccountId).ToArray();
            ILookup<Guid, PotionRow> potions = (visible.Length == 0)
                ? Enumerable.Empty<PotionRow>().ToLookup(row => row.AccountId)
                : (await connection.QueryAsync<PotionRow>(new CommandDefinition(
                    """
                    SELECT account_id AS "AccountId", definition_id AS "DefinitionId", binding_kind AS "BindingKind",
                        quantity - reserved AS "Available"
                    FROM account_items
                    WHERE account_id = ANY(@Accounts) AND definition_id = ANY(@Potions) AND quantity > reserved
                    ORDER BY account_id, definition_id, binding_kind;
                    """, new { Accounts = visible, Potions = RewardStorageLimits.PotionIds() },
                    cancellationToken: cancellationToken))).ToLookup(row => row.AccountId);
            // 진행도 보이는 계정만 읽는다. 마을은 이 값으로 해금을 미리 보고, 예약이 같은 기록으로 다시 판정한다
            Dictionary<Guid, AccountProgressState> progress = await PostgresAccountProgressRepository.ReadStatesAsync(
                connection, null, visible, cancellationToken);

            List<ReadinessParticipantStatus> statuses = new List<ReadinessParticipantStatus>(rows.Count);
            foreach (ParticipantRow row in rows)
            {
                (Guid accountId, Guid characterId) = participants[(int)row.Ordinal - ORDINAL_BASE];
                string account = accountId.ToString("D");
                string character = characterId.ToString("D");
                if (!row.Leased || !row.Revision.HasValue)
                {
                    statuses.Add(new ReadinessParticipantStatus(account, character,
                        ContractCodes.RejectReason.SESSION_MISMATCH, 0, 0, Array.Empty<ReservationPotion>()));
                    continue;
                }

                // characterRevision은 예약이 대조하는 characters.loadout_revision이다(전투 준비 변경만 올린다)
                statuses.Add(new ReadinessParticipantStatus(account, character,
                    row.Busy ? ContractCodes.RejectReason.RESERVATION_CONFLICT : ContractCodes.RejectReason.NONE,
                    row.Revision.Value, Math.Max(0, row.AvailableTickets),
                    potions[accountId].Select(potion => new ReservationPotion(potion.DefinitionId, potion.BindingKind,
                        potion.Available)).ToArray(), ToProgress(progress[accountId])));
            }

            return statuses;
        }

        private static ReadinessProgress ToProgress(AccountProgressState state)
        {
            return new ReadinessProgress(state.IntroCompleted, state.FoxTraces,
                state.IdsOf(EAccountProgressKind.RegionCleared), state.IdsOf(EAccountProgressKind.RegionUnlocked),
                state.IdsOf(EAccountProgressKind.BattleCleared), state.IdsOf(EAccountProgressKind.BattleUnlocked));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class ParticipantRow
        {
            public long  Ordinal          { get; set; }
            public long? Revision         { get; set; }
            public bool  Leased           { get; set; }
            public bool  Busy             { get; set; }
            public long  AvailableTickets { get; set; }
        }

        private sealed class PotionRow
        {
            public Guid   AccountId    { get; set; }
            public string DefinitionId { get; set; } = string.Empty;
            public string BindingKind  { get; set; } = string.Empty;
            public long   Available    { get; set; }
        }
    }
}
