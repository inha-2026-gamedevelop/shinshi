// System
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Inbox
{
    /// <summary> 우편 수령과 운영자 조정이 잔액 행 하나를 바꾸는 공용 경로. 호출자가 계정 행을 먼저 잠그고 같은 트랜잭션에서 원장을 쓴다.
    /// 잠금 순서는 정산과 같은 계정 -> 지갑 -> 캐릭터 -> 아이템이다 </summary>
    public static class RewardBalances
    {
        /****************************************
        *                Fields
        ****************************************/

        // 열 이름은 매개변수로 넣을 수 없어 자산 종류별 문장을 고정해 둔다(입력 문자열을 SQL에 붙이지 않는다)
        private const string CREDIT_CURRENCY_SQL =
            """
            INSERT INTO account_wallets AS wallet (account_id, currency) VALUES (@AccountId, @Amount)
            ON CONFLICT (account_id) DO UPDATE
                SET currency = wallet.currency + EXCLUDED.currency, revision = wallet.revision + 1
            RETURNING wallet.currency;
            """;
        private const string CREDIT_TICKETS_SQL =
            """
            INSERT INTO account_wallets AS wallet (account_id, tickets) VALUES (@AccountId, @Amount)
            ON CONFLICT (account_id) DO UPDATE
                SET tickets = wallet.tickets + EXCLUDED.tickets, revision = wallet.revision + 1
            RETURNING wallet.tickets;
            """;
        private const string CREDIT_ESSENCE_SQL =
            """
            UPDATE characters SET essence = essence + @Amount, revision = revision + 1
            WHERE character_id = @CharacterId AND account_id = @AccountId
            RETURNING essence;
            """;
        private const string CREDIT_SHARDS_SQL =
            """
            UPDATE characters SET shards = shards + @Amount, revision = revision + 1
            WHERE character_id = @CharacterId AND account_id = @AccountId
            RETURNING shards;
            """;
        private const string CREDIT_ITEM_SQL =
            """
            INSERT INTO account_items AS item (account_id, definition_id, binding_kind, quantity)
            VALUES (@AccountId, @AssetId, @BindingKind, @Amount)
            ON CONFLICT (account_id, definition_id, binding_kind) DO UPDATE SET quantity = item.quantity + EXCLUDED.quantity
            RETURNING item.quantity;
            """;

        // 차감은 예약분을 남긴다. 출정 티켓(tickets_reserved), 경매 입찰금(currency_reserved), 지참 물약(account_items.reserved)은
        // 이미 다른 작업이 쓰기로 확정한 양이라 운영자 회수가 가져가면 그 작업의 정산이 CHECK 위반으로 실패한다
        private const string READ_CURRENCY_SQL =
            "SELECT currency AS \"Balance\", currency_reserved AS \"Reserved\" FROM account_wallets WHERE account_id = @AccountId FOR UPDATE;";
        private const string READ_TICKETS_SQL =
            "SELECT tickets AS \"Balance\", tickets_reserved AS \"Reserved\" FROM account_wallets WHERE account_id = @AccountId FOR UPDATE;";
        private const string READ_ESSENCE_SQL =
            """
            SELECT essence AS "Balance", 0::bigint AS "Reserved" FROM characters
            WHERE character_id = @CharacterId AND account_id = @AccountId FOR UPDATE;
            """;
        private const string READ_SHARDS_SQL =
            """
            SELECT shards AS "Balance", 0::bigint AS "Reserved" FROM characters
            WHERE character_id = @CharacterId AND account_id = @AccountId FOR UPDATE;
            """;
        private const string READ_ITEM_SQL =
            """
            SELECT quantity AS "Balance", reserved AS "Reserved" FROM account_items
            WHERE account_id = @AccountId AND definition_id = @AssetId AND binding_kind = @BindingKind FOR UPDATE;
            """;

        private const string DEBIT_CURRENCY_SQL =
            "UPDATE account_wallets SET currency = currency - @Amount, revision = revision + 1 WHERE account_id = @AccountId RETURNING currency;";
        private const string DEBIT_TICKETS_SQL =
            "UPDATE account_wallets SET tickets = tickets - @Amount, revision = revision + 1 WHERE account_id = @AccountId RETURNING tickets;";
        private const string DEBIT_ESSENCE_SQL =
            """
            UPDATE characters SET essence = essence - @Amount, revision = revision + 1
            WHERE character_id = @CharacterId AND account_id = @AccountId RETURNING essence;
            """;
        private const string DEBIT_SHARDS_SQL =
            """
            UPDATE characters SET shards = shards - @Amount, revision = revision + 1
            WHERE character_id = @CharacterId AND account_id = @AccountId RETURNING shards;
            """;
        private const string DEBIT_ITEM_SQL =
            """
            UPDATE account_items SET quantity = quantity - @Amount
            WHERE account_id = @AccountId AND definition_id = @AssetId AND binding_kind = @BindingKind
            RETURNING quantity;
            """;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> amount(양수)만큼 늘리고 변경 뒤 잔액을 돌려준다. 지갑과 아이템 행이 없으면 만든다.
        /// 캐릭터 자산은 그 계정의 캐릭터가 없으면 예외다(호출자가 소유를 먼저 확인한다) </summary>
        public static async Task<long> CreditAsync(DbConnection connection, DbTransaction transaction, EconomyAsset asset,
            long amount, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(asset);
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            long? balance = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                SelectSql(asset.Kind, CREDIT_CURRENCY_SQL, CREDIT_TICKETS_SQL, CREDIT_ESSENCE_SQL, CREDIT_SHARDS_SQL,
                    CREDIT_ITEM_SQL),
                Parameters(asset, amount), transaction, cancellationToken: cancellationToken));
            return balance ?? throw new InvalidOperationException("Credit target character is missing.");
        }

        /// <summary> 예약분을 뺀 가용량 안에서만 줄인다. 잔액 행이 없으면 가용량 0으로 본다 </summary>
        public static async Task<BalanceDebit> DebitAvailableAsync(DbConnection connection, DbTransaction transaction,
            EconomyAsset asset, long amount, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(asset);
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            object parameters = Parameters(asset, amount);
            BalanceRow? row = await connection.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition(
                SelectSql(asset.Kind, READ_CURRENCY_SQL, READ_TICKETS_SQL, READ_ESSENCE_SQL, READ_SHARDS_SQL,
                    READ_ITEM_SQL),
                parameters, transaction, cancellationToken: cancellationToken));
            long balance = row?.Balance ?? 0;
            long reserved = row?.Reserved ?? 0;
            long available = Math.Max(0, balance - reserved);
            long applied = Math.Min(available, amount);
            if (applied == 0)
            {
                return new BalanceDebit(balance, reserved, 0, balance);
            }

            long after = await connection.QuerySingleAsync<long>(new CommandDefinition(
                SelectSql(asset.Kind, DEBIT_CURRENCY_SQL, DEBIT_TICKETS_SQL, DEBIT_ESSENCE_SQL, DEBIT_SHARDS_SQL,
                    DEBIT_ITEM_SQL),
                Parameters(asset, applied), transaction, cancellationToken: cancellationToken));
            return new BalanceDebit(balance, reserved, applied, after);
        }

        private static string SelectSql(string assetKind, string currency, string tickets, string essence, string shards,
            string item)
        {
            return assetKind switch
            {
                EconomyAsset.CURRENCY     => currency,
                EconomyAsset.TICKETS      => tickets,
                EconomyAsset.ESSENCE      => essence,
                EconomyAsset.SHARDS       => shards,
                EconomyAsset.ACCOUNT_ITEM => item,
                _ => throw new ArgumentException("Unsupported asset kind.", nameof(assetKind))
            };
        }

        private static object Parameters(EconomyAsset asset, long amount)
        {
            return new
            {
                asset.AccountId,
                asset.CharacterId,
                asset.AssetId,
                asset.BindingKind,
                Amount = amount
            };
        }

        private sealed class BalanceRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public long Balance  { get; set; }
            public long Reserved { get; set; }
        }
    }
}
