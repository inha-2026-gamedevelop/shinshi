// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Growth
{
    /// <summary> 계정 영구 진행(안내 완료, 여우의 흔적, 지역 성공, 보스 첫 처치, 명시 해금) 기록과 조회 </summary>
    public sealed class PostgresAccountProgressRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string INTRO_KIND = "account.progress.intro";

        private readonly NpgsqlDataSource _dataSource;
        private readonly GrowthContent _content;
        private readonly IdempotentOperationExecutor? _executor;

        /****************************************
        *              Properties
        ****************************************/

        public EGrowthContentProfile Profile { get; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> executor가 없으면 안내 완료 기록(CompleteIntroAsync)을 쓸 수 없다. 정산 안 기록과 조회만 쓰는 호스트용이다 </summary>
        public PostgresAccountProgressRepository(NpgsqlDataSource dataSource, GrowthContent content,
            EGrowthContentProfile profile, IdempotentOperationExecutor? executor = null)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(content);
            _dataSource = dataSource;
            _content    = content;
            _executor   = executor;
            Profile     = profile;
        }

        /// <summary> 마을 서버가 중계한 신시 안내 완료를 기록한다. 멱등 actor는 계정이고, 계정의 살아 있는 접속 임대가 호출 마을에
        /// 없으면 SESSION_MISMATCH, 없는 계정은 INVALID_TARGET이다. 이미 완료한 계정도 확정이며 committedRevision은 진행 revision이다 </summary>
        public Task<OperationResult> CompleteIntroAsync(Guid townInstanceId, Guid accountId, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            if ((townInstanceId == Guid.Empty) || (accountId == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Town, account and operation IDs are required.");
            }
            if (_executor == null)
            {
                throw new InvalidOperationException("Intro recording needs an operation executor.");
            }

            // 마을 인스턴스는 해시에 넣지 않는다. 재접속으로 다른 마을이 같은 operationId를 다시 보내도 처음 결과를 받는다
            OperationRequest request = new OperationRequest(accountId, operationId, INTRO_KIND,
                SessionDb.ToRequestJson(new { accountId = accountId.ToString("D") }));
            return _executor.ExecuteAsync(request, (context, token) => CompleteIntroEffectAsync(context, townInstanceId,
                accountId, operationId, token), cancellationToken);
        }

        private async Task<OperationDecision> CompleteIntroEffectAsync(OperationExecutionContext context,
            Guid townInstanceId, Guid accountId, Guid operationId, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken) != 1)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.INVALID_TARGET);
            }

            // 계정 행을 잠근 뒤 임대를 공유 잠금으로 확인한다. 확인과 기록 사이에 임대가 다른 서버로 옮겨 가지 못한다
            bool leased = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM account_leases
                    WHERE account_id = @AccountId AND instance_id = @Town AND role = @TownRole
                        AND expires_at > clock_timestamp()
                    FOR SHARE);
                """, new { AccountId = accountId, Town = townInstanceId, TownRole = ContractCodes.ServerRole.TOWN },
                transaction, cancellationToken: cancellationToken));
            if (!leased)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            await RecordAsync(context, accountId, EAccountProgressKind.IntroCompleted, string.Empty, 0, operationId,
                cancellationToken);
            long revision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                "SELECT revision FROM account_progress WHERE account_id = @AccountId;", new { AccountId = accountId },
                transaction, cancellationToken: cancellationToken));
            return OperationDecision.Committed(revision, accountId.ToString("D"));
        }

        /// <summary> 진행과 그로부터 계산한 지역, 보스전 해금. 기록이 없는 계정은 빈 진행이다 </summary>
        public async Task<AccountProgressResponse> ReadAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                SELECT (intro_completed_at IS NOT NULL) AS "IntroCompleted", fox_traces AS "FoxTraces", revision AS "Revision"
                FROM account_progress WHERE account_id = @AccountId;
                SELECT unlock_kind AS "Kind", target_id AS "TargetId" FROM account_unlocks WHERE account_id = @AccountId;
                SELECT clock_timestamp();
                """, new { AccountId = accountId }, cancellationToken: cancellationToken));
            ProgressRow? progress = await grid.ReadSingleOrDefaultAsync<ProgressRow>();
            List<UnlockRow> unlocks = (await grid.ReadAsync<UnlockRow>()).ToList();
            DateTime now = await grid.ReadSingleAsync<DateTime>();

            AccountProgressState state = new AccountProgressState(progress?.IntroCompleted ?? false,
                progress?.FoxTraces ?? 0, progress?.Revision ?? 0,
                unlocks.Where(row => Enum.TryParse(row.Kind, out EAccountProgressKind _))
                    .Select(row => (Enum.Parse<EAccountProgressKind>(row.Kind), row.TargetId)));
            return new AccountProgressResponse(state.IntroCompleted, state.FoxTraces, state.Revision,
                ProgressionRules.Regions(_content, Profile, state), ProgressionRules.BossBattles(_content, Profile, state),
                _content.ContentVersion,
                new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeMilliseconds());
        }

        /// <summary> 여러 계정의 진행 기록을 한 번에 읽는다. 기록이 없는 계정도 빈 진행으로 담는다.
        /// 출정 준비 확인(트랜잭션 없음)과 예약(계정을 잠근 트랜잭션)이 같은 읽기를 쓴다 </summary>
        public static async Task<Dictionary<Guid, AccountProgressState>> ReadStatesAsync(DbConnection connection,
            DbTransaction? transaction, IReadOnlyCollection<Guid> accounts, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(accounts);
            Dictionary<Guid, AccountProgressState> states = new Dictionary<Guid, AccountProgressState>(accounts.Count);
            if (accounts.Count == 0)
            {
                return states;
            }

            Guid[] ids = accounts.Distinct().ToArray();
            using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                SELECT account_id AS "AccountId", (intro_completed_at IS NOT NULL) AS "IntroCompleted",
                    fox_traces AS "FoxTraces", revision AS "Revision"
                FROM account_progress WHERE account_id = ANY(@Accounts);
                SELECT account_id AS "AccountId", unlock_kind AS "Kind", target_id AS "TargetId"
                FROM account_unlocks WHERE account_id = ANY(@Accounts);
                """, new { Accounts = ids }, transaction, cancellationToken: cancellationToken));
            Dictionary<Guid, AccountProgressRow> progress = (await grid.ReadAsync<AccountProgressRow>())
                .ToDictionary(row => row.AccountId);
            ILookup<Guid, AccountUnlockRow> unlocks = (await grid.ReadAsync<AccountUnlockRow>())
                .ToLookup(row => row.AccountId);
            foreach (Guid accountId in ids)
            {
                progress.TryGetValue(accountId, out AccountProgressRow? row);
                // 모르는 종류 문자열은 건너뛴다. DB CHECK가 넷만 허용하지만 이후 종류가 늘어도 읽기가 깨지지 않게 한다
                List<(EAccountProgressKind, string)> records = new List<(EAccountProgressKind, string)>();
                foreach (AccountUnlockRow unlock in unlocks[accountId])
                {
                    if (Enum.TryParse(unlock.Kind, out EAccountProgressKind kind))
                    {
                        records.Add((kind, unlock.TargetId));
                    }
                }
                states[accountId] = new AccountProgressState(row?.IntroCompleted ?? false, row?.FoxTraces ?? 0,
                    row?.Revision ?? 0, records);
            }

            return states;
        }

        /// <summary> 호출자의 작업 트랜잭션에서 진행 하나를 기록한다. 이미 있는 기록이면 false이며 아무것도 바꾸지 않는다.
        /// 정산이나 안내 완료 같은 확정 경로가 계정 행을 잠근 뒤 부른다 </summary>
        public async Task<bool> RecordAsync(OperationExecutionContext context, Guid accountId, EAccountProgressKind kind,
            string targetId, int amount, Guid operationId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            if (accountId == Guid.Empty || operationId == Guid.Empty)
            {
                throw new ArgumentException("Account and operation IDs are required.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO account_progress (account_id) VALUES (@AccountId) ON CONFLICT (account_id) DO NOTHING;",
                new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
            int changed;
            switch (kind)
            {
                case EAccountProgressKind.IntroCompleted:
                    changed = await connection.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE account_progress SET intro_completed_at = clock_timestamp(), revision = revision + 1,
                            updated_at = clock_timestamp()
                        WHERE account_id = @AccountId AND intro_completed_at IS NULL;
                        """, new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
                    break;
                case EAccountProgressKind.FoxTraces:
                    if (amount <= 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(amount));
                    }
                    // 결계를 연 뒤에는 상한에서 더 쌓지 않는다
                    changed = await connection.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE account_progress SET fox_traces = LEAST(fox_traces + @Amount, @Cap), revision = revision + 1,
                            updated_at = clock_timestamp()
                        WHERE account_id = @AccountId AND fox_traces < @Cap;
                        """, new { AccountId = accountId, Amount = amount, Cap = _content.Progression.FoxTraceCap },
                        transaction, cancellationToken: cancellationToken));
                    break;
                default:
                    if (!ProgressionRules.IsKnownTarget(_content, kind, targetId))
                    {
                        throw new ArgumentException("Unknown progress target.", nameof(targetId));
                    }
                    changed = await connection.ExecuteAsync(new CommandDefinition(
                        """
                        WITH inserted AS (
                            INSERT INTO account_unlocks (account_id, unlock_kind, target_id, operation_id)
                            VALUES (@AccountId, @Kind, @TargetId, @OperationId)
                            ON CONFLICT (account_id, unlock_kind, target_id) DO NOTHING
                            RETURNING account_id
                        )
                        UPDATE account_progress AS progress SET revision = progress.revision + 1, updated_at = clock_timestamp()
                        FROM inserted WHERE progress.account_id = inserted.account_id;
                        """, new { AccountId = accountId, Kind = kind.ToString(), TargetId = targetId, OperationId = operationId },
                        transaction, cancellationToken: cancellationToken));
                    break;
            }

            return changed > 0;
        }

        /// <summary> 성공 정산 트랜잭션에서 출정을 끝까지 함께한 계정들의 클리어를 기록한다. 일반전은 battleId(지역 ID)의 지역 성공과
        /// 그 지역의 여우의 흔적(foxTracesPerClear, 상한 foxTraceCap), 보스전은 bossId와 난이도로 찾은 보스전의 첫 처치다.
        /// 콘텐츠에 없는 전장(테스트용 지도, 0012 전 출정)은 기록하지 않는다. 호출자가 계정 행을 먼저 잠가야 한다.
        /// 근거: docs/02-game-design.md 8장, growth-content.json progression </summary>
        public async Task RecordSessionClearAsync(OperationExecutionContext context, IReadOnlyCollection<Guid> accountIds,
            bool isBoss, string? battleId, string? bossId, short? difficulty, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(accountIds);
            if (operationId == Guid.Empty)
            {
                throw new ArgumentException("Operation ID is required.", nameof(operationId));
            }

            Guid[] accounts = accountIds.Distinct().ToArray();
            if (accounts.Length == 0)
            {
                return;
            }

            EAccountProgressKind kind;
            string targetId;
            int foxTraces = 0;
            if (isBoss)
            {
                GrowthContent.BossBattleDefinition? battle = _content.Progression.BossBattles.FirstOrDefault(entry =>
                    (entry.BossId == bossId) && difficulty.HasValue && (entry.Difficulty == difficulty.Value));
                if (battle == null)
                {
                    return;
                }

                kind     = EAccountProgressKind.BattleCleared;
                targetId = battle.DefinitionId;
            }
            else
            {
                GrowthContent.RegionDefinition? region = _content.Progression.Regions.FirstOrDefault(entry =>
                    entry.DefinitionId == battleId);
                if (region == null)
                {
                    return;
                }

                kind      = EAccountProgressKind.RegionCleared;
                targetId  = region.DefinitionId;
                foxTraces = region.FoxTracesPerClear;
            }

            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            // 참가자는 최대 4명이라 계정마다 반복하지 않고 배열 한 번으로 넣는다
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO account_progress (account_id)
                SELECT account_id FROM unnest(@Accounts::uuid[]) AS account_id ORDER BY account_id
                ON CONFLICT (account_id) DO NOTHING;

                WITH inserted AS (
                    INSERT INTO account_unlocks (account_id, unlock_kind, target_id, operation_id)
                    SELECT account_id, @Kind, @TargetId, @OperationId
                    FROM unnest(@Accounts::uuid[]) AS account_id ORDER BY account_id
                    ON CONFLICT (account_id, unlock_kind, target_id) DO NOTHING
                    RETURNING account_id
                )
                UPDATE account_progress AS progress SET revision = progress.revision + 1, updated_at = clock_timestamp()
                FROM inserted WHERE progress.account_id = inserted.account_id;
                """, new { Accounts = accounts, Kind = kind.ToString(), TargetId = targetId, OperationId = operationId },
                transaction, cancellationToken: cancellationToken));
            if (foxTraces <= 0)
            {
                return;
            }

            // 결계를 연 뒤에는 상한에서 더 쌓지 않는다(흔적은 소모하지 않는다)
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE account_progress SET fox_traces = LEAST(fox_traces + @Amount, @Cap), revision = revision + 1,
                    updated_at = clock_timestamp()
                WHERE account_id = ANY(@Accounts) AND fox_traces < @Cap;
                """, new { Accounts = accounts, Amount = foxTraces, Cap = _content.Progression.FoxTraceCap },
                transaction, cancellationToken: cancellationToken));
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class ProgressRow
        {
            public bool IntroCompleted { get; set; }
            public int  FoxTraces      { get; set; }
            public long Revision       { get; set; }
        }

        private sealed class UnlockRow
        {
            public string Kind     { get; set; } = string.Empty;
            public string TargetId { get; set; } = string.Empty;
        }

        private sealed class AccountProgressRow
        {
            public Guid AccountId      { get; set; }
            public bool IntroCompleted { get; set; }
            public int  FoxTraces      { get; set; }
            public long Revision       { get; set; }
        }

        private sealed class AccountUnlockRow
        {
            public Guid   AccountId { get; set; }
            public string Kind      { get; set; } = string.Empty;
            public string TargetId  { get; set; } = string.Empty;
        }
    }
}
