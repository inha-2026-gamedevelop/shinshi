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
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Prologue
{
    /// <summary> 계정의 성장 메뉴 안내 완료 기록. 경제 효과가 없어 operations 대신 (계정, 메뉴) 유일 키로 재전송을 받는다 </summary>
    public sealed class PostgresMenuGuideRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 목록 정렬을 DB 로캘과 무관한 바이트 순서로 고정한다. ID는 [a-z0-9_]라 C# 서수 정렬과 같다
        private const string LIST_SQL =
            """
            SELECT menu_id FROM account_menu_guides WHERE account_id = @AccountId ORDER BY menu_id COLLATE "C";
            """;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresMenuGuideRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 계정이 마친 메뉴 안내 ID를 정렬해 돌려준다 </summary>
        public async Task<MenuGuideOutcome> ListAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            RequireAccount(accountId);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            List<string> menuIds = (await connection.QueryAsync<string>(new CommandDefinition(LIST_SQL,
                new { AccountId = accountId }, cancellationToken: cancellationToken))).ToList();
            DateTime now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", cancellationToken: cancellationToken));
            return new MenuGuideOutcome(ContractCodes.RejectReason.NONE, menuIds, UnixMilliseconds(now));
        }

        /// <summary> 메뉴 안내 하나를 완료로 남긴다. 형식 불량은 MENU_GUIDE_INVALID, 상한을 넘는 새 ID는 MENU_GUIDE_LIMIT이다.
        /// 이미 남긴 메뉴는 아무것도 바꾸지 않고 같은 목록으로 성공한다 </summary>
        public Task<MenuGuideOutcome> CompleteAsync(Guid accountId, string? menuId,
            CancellationToken cancellationToken = default)
        {
            RequireAccount(accountId);
            if (!MenuGuideRules.IsValidMenuId(menuId))
            {
                return Task.FromResult(new MenuGuideOutcome(ContractCodes.RejectReason.MENU_GUIDE_INVALID,
                    Array.Empty<string>(), 0));
            }

            return SessionDb.RetryAsync(token => CompleteOnceAsync(accountId, menuId!, token), cancellationToken);
        }

        private async Task<MenuGuideOutcome> CompleteOnceAsync(Guid accountId, string menuId,
            CancellationToken cancellationToken)
        {
            (NpgsqlConnection connection, NpgsqlTransaction transaction) = await SessionDb.BeginAsync(_dataSource,
                cancellationToken);
            await using (connection)
            await using (transaction)
            {
                // 계정 행을 잠가 같은 계정의 동시 완료가 상한 검사와 삽입 사이에 끼어들지 못하게 한다.
                // FOR NO KEY UPDATE라 다른 표의 계정 외래 키 확인(KEY SHARE)은 막지 않는다
                Guid? locked = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                    "SELECT account_id FROM accounts WHERE account_id = @AccountId FOR NO KEY UPDATE;",
                    new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
                if (!locked.HasValue)
                {
                    return new MenuGuideOutcome(ContractCodes.RejectReason.INVALID_TARGET, Array.Empty<string>(), 0);
                }

                CountRow counts = await connection.QuerySingleAsync<CountRow>(new CommandDefinition(
                    """
                    SELECT count(*) AS "Total", count(*) FILTER (WHERE menu_id = @MenuId) AS "Same"
                    FROM account_menu_guides WHERE account_id = @AccountId;
                    """, new { AccountId = accountId, MenuId = menuId }, transaction, cancellationToken: cancellationToken));
                if ((counts.Same == 0) && (counts.Total >= PrologueCodes.MAX_MENU_GUIDES))
                {
                    return new MenuGuideOutcome(ContractCodes.RejectReason.MENU_GUIDE_LIMIT, Array.Empty<string>(), 0);
                }
                if (counts.Same == 0)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO account_menu_guides (account_id, menu_id) VALUES (@AccountId, @MenuId)
                        ON CONFLICT (account_id, menu_id) DO NOTHING;
                        """, new { AccountId = accountId, MenuId = menuId }, transaction,
                        cancellationToken: cancellationToken));
                }

                List<string> menuIds = (await connection.QueryAsync<string>(new CommandDefinition(LIST_SQL,
                    new { AccountId = accountId }, transaction, cancellationToken: cancellationToken))).ToList();
                DateTime now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
                    "SELECT clock_timestamp();", transaction: transaction, cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
                return new MenuGuideOutcome(ContractCodes.RejectReason.NONE, menuIds, UnixMilliseconds(now));
            }
        }

        private static void RequireAccount(Guid accountId)
        {
            if (accountId == Guid.Empty)
            {
                throw new ArgumentException("Account ID is required.", nameof(accountId));
            }
        }

        private static long UnixMilliseconds(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class CountRow
        {
            public long Total { get; set; }
            public long Same  { get; set; }
        }
    }
}
