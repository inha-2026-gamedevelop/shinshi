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
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Characters
{
    /// <summary> 캐릭터 생성은 계정 actor의 멱등 작업이고 영수증 ID가 만든 캐릭터 ID다. 재전송은 영수증으로 같은 캐릭터를 다시 읽는다 </summary>
    public sealed class PostgresCharacterRepository : ICharacterRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CREATE_OPERATION_KIND = "character.create";

        // TODO(민성): 계정당 캐릭터 수는 기획 미정(docs/design/account-and-website.md 8장). 확정 전 임시값이다
        public const int MAX_CHARACTERS_PER_ACCOUNT = 3;

        // 캐릭터 생성은 계정 revision 계약이 없어 0으로 확정한다
        private const long NO_REVISION = 0;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresCharacterRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            _dataSource = dataSource;
            _executor   = executor;
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
                accountId, name, nameKey, classId, appearanceJson, token), cancellationToken);
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
                    revision AS "Revision", appearance::text AS "Appearance"
                FROM characters WHERE account_id = @AccountId ORDER BY character_id;
                """, new { AccountId = accountId }, cancellationToken: cancellationToken));
            return rows.Select(row => new CharacterRecord(row.CharacterId, row.AccountId, row.Name, row.ClassId,
                row.Revision, row.Appearance)).ToArray();
        }

        private static async Task<OperationDecision> CreateEffectAsync(OperationExecutionContext context, Guid accountId,
            string name, string nameKey, int classId, string appearanceJson, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 같은 계정의 동시 생성이 개수 상한을 함께 넘지 않게 계정 행을 먼저 잠근다(계정 -> 캐릭터 순서)
            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken) != 1)
            {
                throw new InvalidOperationException("Character owner account is missing.");
            }

            long count = await connection.QuerySingleAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM characters WHERE account_id = @AccountId;",
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
                INSERT INTO characters (character_id, account_id, class_id, name, name_key, appearance)
                VALUES (@CharacterId, @AccountId, @ClassId, @Name, @NameKey, CAST(@Appearance AS jsonb))
                ON CONFLICT (name_key) DO NOTHING
                RETURNING character_id;
                """, new { CharacterId = characterId, AccountId = accountId, ClassId = classId, Name = name,
                    NameKey = nameKey, Appearance = appearanceJson }, transaction, cancellationToken: cancellationToken));
            if (!inserted.HasValue)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_NAME_TAKEN);
            }

            return OperationDecision.Committed(NO_REVISION, characterId.ToString("D"));
        }

        private async Task<CharacterRecord?> ReadAsync(Guid accountId, Guid characterId,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            CharacterRow? row = await connection.QuerySingleOrDefaultAsync<CharacterRow>(new CommandDefinition(
                """
                SELECT character_id AS "CharacterId", account_id AS "AccountId", name AS "Name", class_id AS "ClassId",
                    revision AS "Revision", appearance::text AS "Appearance"
                FROM characters WHERE character_id = @CharacterId AND account_id = @AccountId;
                """, new { CharacterId = characterId, AccountId = accountId }, cancellationToken: cancellationToken));
            return row == null ? null : new CharacterRecord(row.CharacterId, row.AccountId, row.Name, row.ClassId,
                row.Revision, row.Appearance);
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
            public string? Appearance { get; set; }
        }
    }
}
