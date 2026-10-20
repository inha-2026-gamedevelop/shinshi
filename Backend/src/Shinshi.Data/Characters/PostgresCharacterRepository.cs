// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Growth;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;
using Shinshi.Data.Social;

namespace Shinshi.Data.Characters
{
    /// <summary> 캐릭터 생성은 계정 actor의 멱등 작업이고 영수증 ID가 만든 캐릭터 ID다. 재전송은 영수증으로 같은 캐릭터를 다시 읽는다 </summary>
    public sealed class PostgresCharacterRepository : ICharacterRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CREATE_OPERATION_KIND = "character.create";
        public const string DELETE_OPERATION_KIND = "character.delete";
        public const string DELETE_REQUEST_OPERATION_KIND = "character.delete-request";
        public const string DELETE_CANCEL_OPERATION_KIND  = "character.delete-cancel";

        // 삭제 예약 뒤 최종 삭제까지의 유예 시간. DB 시각으로 판정한다
        public const int DELETE_GRACE_MINUTES = 10;

        // 접속 판정은 마을 입장 교환과 같은 heartbeat 기준이다. 비정상 종료한 서버의 남은 임대는 삭제를 막지 않는다
        private const int HEARTBEAT_SECONDS = 15;

        // TODO(민성): 계정당 캐릭터 수는 기획 미정(docs/design/account-and-website.md 8장). 확정 전 임시값이다
        public const int MAX_CHARACTERS_PER_ACCOUNT = 3;

        // TODO(민성): 계정당 하루 삭제 수는 기획 미정(docs/design/account-and-website.md 8장, 잠정 결정 9 소프트 삭제).
        // 삭제와 생성을 반복해 이름 키와 캐릭터 행을 무한히 소모하지 못하게 슬롯 수만큼으로 임시로 막는다
        public const int MAX_DELETES_PER_WINDOW = MAX_CHARACTERS_PER_ACCOUNT;
        public const int DELETE_WINDOW_HOURS    = 24;

        // 캐릭터 생성은 계정 revision 계약이 없어 0으로 확정한다
        private const long NO_REVISION = 0;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        private readonly InitialGrantService _grants;

        /****************************************
        *                Methods
        ****************************************/

        // 초기 지급을 따로 구성하지 않은 호스트(테스트, 도구)는 내장 콘텐츠의 체험판 지급을 쓴다
        public PostgresCharacterRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor)
            : this(dataSource, executor, new InitialGrantService(GrowthContent.LoadEmbedded(), EGrowthContentProfile.Trial,
                new PostgresEconomyLedgerWriter(), new PostgresItemHistoryWriter()))
        {
        }

        public PostgresCharacterRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            InitialGrantService grants)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(grants);
            _dataSource = dataSource;
            _executor   = executor;
            _grants     = grants;
        }

        public async Task<CharacterCreateOutcome> CreateAsync(Guid accountId, Guid operationId, string name,
            string nameKey, int classId, string appearanceJson, string requestJson,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentException.ThrowIfNullOrEmpty(nameKey);
            ArgumentException.ThrowIfNullOrEmpty(appearanceJson);
            ArgumentNullException.ThrowIfNull(requestJson);
            OperationRequest request = new OperationRequest(accountId, operationId, CREATE_OPERATION_KIND, requestJson);
            OperationResult result = await _executor.ExecuteAsync(request, (context, token) => CreateEffectAsync(context,
                accountId, operationId, name, nameKey, classId, appearanceJson, token), cancellationToken);
            if ((result.Status != ContractCodes.OperationStatus.COMMITTED)
                || !Guid.TryParse(result.ReceiptId, out Guid characterId))
            {
                return new CharacterCreateOutcome(result, null);
            }

            return new CharacterCreateOutcome(result, await ReadAsync(accountId, characterId, cancellationToken));
        }

        public async Task<bool> IsNameTakenAsync(string nameKey, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(nameKey);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM characters WHERE name_key = @NameKey);",
                new { NameKey = nameKey }, cancellationToken: cancellationToken));
        }

        public async Task<IReadOnlyList<CharacterRecord>> ListAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            IEnumerable<CharacterRow> rows = await connection.QueryAsync<CharacterRow>(new CommandDefinition(
                """
                SELECT character_id AS "CharacterId", account_id AS "AccountId", name AS "Name", class_id AS "ClassId",
                    revision AS "Revision", appearance::text AS "Appearance", total_experience AS "TotalExperience",
                    delete_requested_at IS NOT NULL AS "DeletePending",
                    CASE WHEN delete_requested_at IS NULL THEN 0 ELSE GREATEST(0, CEIL(EXTRACT(EPOCH FROM
                        delete_requested_at + make_interval(mins => @GraceMinutes) - clock_timestamp())))::bigint END
                        AS "DeleteRemainingSeconds"
                FROM characters WHERE account_id = @AccountId AND deleted_at IS NULL ORDER BY character_id;
                """, new { AccountId = accountId, GraceMinutes = DELETE_GRACE_MINUTES },
                cancellationToken: cancellationToken));
            return rows.Select(row => new CharacterRecord(row.CharacterId, row.AccountId, row.Name, row.ClassId,
                row.Revision, row.Appearance, row.TotalExperience, row.DeletePending, row.DeleteRemainingSeconds)).ToArray();
        }

        public Task<OperationResult> DeleteAsync(Guid accountId, Guid operationId, Guid characterId,
            long expectedRevision, CancellationToken cancellationToken = default)
        {
            if ((accountId == Guid.Empty) || (operationId == Guid.Empty) || (characterId == Guid.Empty))
            {
                throw new ArgumentException("Account, operation and character IDs are required.");
            }

            OperationRequest request = new OperationRequest(accountId, operationId, DELETE_OPERATION_KIND,
                SessionDb.ToRequestJson(new { characterId = characterId.ToString("D"), expectedRevision }));
            return _executor.ExecuteAsync(request, (context, token) => DeleteEffectAsync(context, accountId, operationId,
                characterId, expectedRevision, token), cancellationToken);
        }

        private static async Task<OperationDecision> DeleteEffectAsync(OperationExecutionContext context, Guid accountId,
            Guid operationId, Guid characterId, long expectedRevision, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 잠금 순서: 계정 -> 캐릭터 -> 길드. 생성, 예약, 마을 입장 교환과 같은 계정 행에서 줄을 선다.
            // 소프트 삭제는 키를 바꾸지 않아 캐릭터 행을 FOR NO KEY UPDATE로 잡는다. 소셜 작업과는 같은 강도로 줄을 서고,
            // 다른 삭제가 넣는 알림 행의 FK 검사(FOR KEY SHARE)와는 충돌하지 않아 서로 길드원인 두 캐릭터의 동시 삭제가 교착되지 않는다
            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken) != 1)
            {
                throw new InvalidOperationException("Character owner account is missing.");
            }

            long? revision = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                """
                SELECT revision FROM characters
                WHERE character_id = @CharacterId AND account_id = @AccountId AND deleted_at IS NULL
                FOR NO KEY UPDATE;
                """, new { CharacterId = characterId, AccountId = accountId }, transaction,
                cancellationToken: cancellationToken));
            if (!revision.HasValue)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_NOT_FOUND);
            }

            bool connected = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM account_leases AS lease JOIN server_instances AS instance
                    ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                    WHERE lease.account_id = @AccountId AND lease.expires_at > clock_timestamp()
                        AND instance.state <> 'Offline'
                        AND instance.heartbeat_at > clock_timestamp() - @Heartbeat);
                """, new { AccountId = accountId, Heartbeat = TimeSpan.FromSeconds(HEARTBEAT_SECONDS) }, transaction,
                cancellationToken: cancellationToken));
            if (connected)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.ALREADY_CONNECTED, revision.Value);
            }

            bool sortie = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM session_participants
                    WHERE character_id = @CharacterId AND state IN ('Reserved', 'Active'));
                """, new { CharacterId = characterId }, transaction, cancellationToken: cancellationToken));
            if (sortie)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.RESERVATION_CONFLICT, revision.Value);
            }

            // 계정 행을 잠근 뒤 보므로 등록과 겹치지 않는다. 보관 중인 장비는 이 캐릭터 행에 있어 삭제하면 반환할 곳이 없다
            // (수량형 등록은 계정 귀속이라 캐릭터 삭제와 무관하다)
            bool hasListing = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM market_listings
                    WHERE seller_character_id = @CharacterId AND asset_kind = 'Equipment' AND state = 'Active');
                """, new { CharacterId = characterId }, transaction, cancellationToken: cancellationToken));
            if (hasListing)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_HAS_MARKET_LISTING, revision.Value);
            }
            if (revision.Value != expectedRevision)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.STALE_REVISION, revision.Value);
            }

            // 예약 없는 삭제와 유예 중 삭제는 거절한다. 유예는 DB 시각으로 판정하고 캐릭터 행은 위에서 잠갔다
            bool? graceElapsed = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
                """
                SELECT delete_requested_at <= clock_timestamp() - make_interval(mins => @GraceMinutes)
                FROM characters WHERE character_id = @CharacterId;
                """, new { CharacterId = characterId, GraceMinutes = DELETE_GRACE_MINUTES }, transaction,
                cancellationToken: cancellationToken));
            if (!graceElapsed.HasValue)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_DELETE_NOT_REQUESTED, revision.Value);
            }
            if (!graceElapsed.Value)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_DELETE_PENDING, revision.Value);
            }

            // 계정 행을 잠근 뒤 세므로 동시 삭제가 함께 상한을 넘지 못한다. 시각은 DB 기준이다(ix_characters_account_deleted)
            long recentDeletes = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                SELECT COUNT(*) FROM characters
                WHERE account_id = @AccountId AND deleted_at IS NOT NULL
                    AND deleted_at > clock_timestamp() - @Window;
                """, new { AccountId = accountId, Window = TimeSpan.FromHours(DELETE_WINDOW_HOURS) }, transaction,
                cancellationToken: cancellationToken));
            if (recentDeletes >= MAX_DELETES_PER_WINDOW)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.RATE_LIMITED, revision.Value);
            }

            // 길드원이 있는 길드장은 위임 뒤 삭제한다. 그 밖에는 탈퇴(혼자면 해체)와 친구, 요청, 전달함을 정리한다
            int social = await SocialCharacterCleanup.ApplyAsync(connection, transaction, characterId, cancellationToken);
            if (social != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(social, revision.Value);
            }

            // 삭제 전에 받은 미교환 입장 토큰(마을, 출정)은 더 쓸 수 없게 한다
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE auth_join_tokens SET revoked_at = clock_timestamp()
                WHERE account_id = @AccountId AND character_id = @CharacterId
                    AND consumed_at IS NULL AND revoked_at IS NULL;
                """, new { AccountId = accountId, CharacterId = characterId }, transaction,
                cancellationToken: cancellationToken));
            long deleted = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                UPDATE characters
                SET deleted_at = clock_timestamp(), delete_operation_id = @OperationId, revision = revision + 1
                WHERE character_id = @CharacterId AND account_id = @AccountId AND deleted_at IS NULL
                RETURNING revision;
                """, new { AccountId = accountId, CharacterId = characterId, OperationId = operationId }, transaction,
                cancellationToken: cancellationToken));
            return OperationDecision.Committed(deleted, characterId.ToString("D"));
        }

        public Task<OperationResult> RequestDeleteAsync(Guid accountId, Guid operationId, Guid characterId,
            long expectedRevision, CancellationToken cancellationToken = default)
        {
            return ExecuteDeleteReservationAsync(accountId, operationId, characterId, expectedRevision,
                DELETE_REQUEST_OPERATION_KIND, true, cancellationToken);
        }

        public Task<OperationResult> CancelDeleteAsync(Guid accountId, Guid operationId, Guid characterId,
            long expectedRevision, CancellationToken cancellationToken = default)
        {
            return ExecuteDeleteReservationAsync(accountId, operationId, characterId, expectedRevision,
                DELETE_CANCEL_OPERATION_KIND, false, cancellationToken);
        }

        private Task<OperationResult> ExecuteDeleteReservationAsync(Guid accountId, Guid operationId, Guid characterId,
            long expectedRevision, string kind, bool requesting, CancellationToken cancellationToken)
        {
            if ((accountId == Guid.Empty) || (operationId == Guid.Empty) || (characterId == Guid.Empty))
            {
                throw new ArgumentException("Account, operation and character IDs are required.");
            }

            OperationRequest request = new OperationRequest(accountId, operationId, kind,
                SessionDb.ToRequestJson(new { characterId = characterId.ToString("D"), expectedRevision }));
            return _executor.ExecuteAsync(request, (context, token) => DeleteReservationEffectAsync(context, accountId,
                characterId, expectedRevision, requesting, token), cancellationToken);
        }

        private static async Task<OperationDecision> DeleteReservationEffectAsync(OperationExecutionContext context,
            Guid accountId, Guid characterId, long expectedRevision, bool requesting, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 잠금 순서는 삭제와 같다: 계정 -> 캐릭터
            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken) != 1)
            {
                throw new InvalidOperationException("Character owner account is missing.");
            }

            CharacterLockRow? row = await connection.QuerySingleOrDefaultAsync<CharacterLockRow>(new CommandDefinition(
                """
                SELECT revision AS "Revision", delete_requested_at IS NOT NULL AS "Requested" FROM characters
                WHERE character_id = @CharacterId AND account_id = @AccountId AND deleted_at IS NULL
                FOR NO KEY UPDATE;
                """, new { CharacterId = characterId, AccountId = accountId }, transaction,
                cancellationToken: cancellationToken));
            if (row == null)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_NOT_FOUND);
            }

            // 이미 예약했으면 시각을 유지하는 확정 no-op이다(revision도 올리지 않는다)
            if (requesting && row.Requested)
            {
                return OperationDecision.Committed(row.Revision, characterId.ToString("D"));
            }
            if (row.Revision != expectedRevision)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.STALE_REVISION, row.Revision);
            }
            if (!requesting && !row.Requested)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_DELETE_NOT_REQUESTED, row.Revision);
            }

            string setClause = requesting ? "delete_requested_at = clock_timestamp()" : "delete_requested_at = NULL";
            long updated = await connection.QuerySingleAsync<long>(new CommandDefinition(
                $"UPDATE characters SET {setClause}, revision = revision + 1 WHERE character_id = @CharacterId RETURNING revision;",
                new { CharacterId = characterId }, transaction, cancellationToken: cancellationToken));
            return OperationDecision.Committed(updated, characterId.ToString("D"));
        }

        private async Task<OperationDecision> CreateEffectAsync(OperationExecutionContext context, Guid accountId,
            Guid operationId, string name, string nameKey, int classId, string appearanceJson,
            CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 같은 계정의 동시 생성이 개수 상한을 함께 넘지 않게 계정 행을 먼저 잠근다(계정 -> 캐릭터 순서)
            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken) != 1)
            {
                throw new InvalidOperationException("Character owner account is missing.");
            }

            long count = await connection.QuerySingleAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM characters WHERE account_id = @AccountId AND deleted_at IS NULL;",
                new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
            if (count >= MAX_CHARACTERS_PER_ACCOUNT)
            {
                // TODO(민성): 캐릭터 수 상한 전용 거절 코드는 기획 미정이라 기존 NO_CAPACITY를 쓴다
                return OperationDecision.Rejected(ContractCodes.RejectReason.NO_CAPACITY);
            }

            // 다른 계정이 같은 비교 키를 동시에 넣으면 먼저 커밋한 쪽을 기다린 뒤 아무것도 넣지 않는다.
            // 예외가 아니라 결과로 판정하므로 트랜잭션이 깨지지 않고 거절 결과를 저장할 수 있다
            Guid characterId = Guid.NewGuid();
            Guid? inserted = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                """
                INSERT INTO characters (character_id, account_id, class_id, name, name_key, appearance, initial_experience, total_experience)
                VALUES (@CharacterId, @AccountId, @ClassId, @Name, @NameKey, CAST(@Appearance AS jsonb), @Experience, @Experience)
                ON CONFLICT (name_key) DO NOTHING
                RETURNING character_id;
                """, new { CharacterId = characterId, AccountId = accountId, ClassId = classId, Name = name,
                    NameKey = nameKey, Appearance = appearanceJson, Experience = _grants.InitialExperience }, transaction, cancellationToken: cancellationToken));
            if (!inserted.HasValue)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_NAME_TAKEN);
            }

            // 계정 행을 잠근 같은 트랜잭션이라 동시 생성 중 한쪽만 계정 최초분을 받는다. 지급 실패는 생성 전체를 되돌린다
            await _grants.ApplyAsync(context, accountId, characterId, classId, operationId, cancellationToken);
            return OperationDecision.Committed(NO_REVISION, characterId.ToString("D"));
        }

        private async Task<CharacterRecord?> ReadAsync(Guid accountId, Guid characterId,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            CharacterRow? row = await connection.QuerySingleOrDefaultAsync<CharacterRow>(new CommandDefinition(
                """
                SELECT character_id AS "CharacterId", account_id AS "AccountId", name AS "Name", class_id AS "ClassId",
                    revision AS "Revision", appearance::text AS "Appearance", total_experience AS "TotalExperience",
                    delete_requested_at IS NOT NULL AS "DeletePending",
                    CASE WHEN delete_requested_at IS NULL THEN 0 ELSE GREATEST(0, CEIL(EXTRACT(EPOCH FROM
                        delete_requested_at + make_interval(mins => @GraceMinutes) - clock_timestamp())))::bigint END
                        AS "DeleteRemainingSeconds"
                FROM characters WHERE character_id = @CharacterId AND account_id = @AccountId AND deleted_at IS NULL;
                """, new { CharacterId = characterId, AccountId = accountId, GraceMinutes = DELETE_GRACE_MINUTES },
                cancellationToken: cancellationToken));
            return row == null ? null : new CharacterRecord(row.CharacterId, row.AccountId, row.Name, row.ClassId,
                row.Revision, row.Appearance, row.TotalExperience, row.DeletePending, row.DeleteRemainingSeconds);
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
            public long Revision { get; set; }
            public long TotalExperience { get; set; }
            public string? Appearance { get; set; }
            public bool DeletePending { get; set; }
            public long DeleteRemainingSeconds { get; set; }
        }

        private sealed class CharacterLockRow
        {
            public long Revision { get; set; }
            public bool Requested { get; set; }
        }
    }
}
