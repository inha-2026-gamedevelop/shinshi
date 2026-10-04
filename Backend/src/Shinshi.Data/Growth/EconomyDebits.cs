// System
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Growth
{
    /// <summary> 플레이어가 비용을 내는 성장 작업의 정확 차감. 가용량(총량 - 예약량)이 모자라면 아무것도 바꾸지 않고 null이다.
    /// 운영자 회수처럼 있는 만큼만 빼는 RewardBalances.DebitAvailableAsync와 달리 부분 차감이 없다 </summary>
    public static class EconomyDebits
    {
        /****************************************
        *                Fields
        ****************************************/

        // 검사와 차감을 한 문장에 묶어 READ COMMITTED의 갱신 손실을 막는다. 열 이름은 매개변수로 넣을 수 없어 종류별로 고정한다
        private const string DEBIT_CURRENCY_SQL =
            """
            UPDATE account_wallets SET currency = currency - @Amount, revision = revision + 1
            WHERE account_id = @AccountId AND currency - currency_reserved >= @Amount
            RETURNING currency;
            """;
        private const string DEBIT_TICKETS_SQL =
            """
            UPDATE account_wallets SET tickets = tickets - @Amount, revision = revision + 1
            WHERE account_id = @AccountId AND tickets - tickets_reserved >= @Amount
            RETURNING tickets;
            """;
        private const string DEBIT_ESSENCE_SQL =
            """
            UPDATE characters SET essence = essence - @Amount, revision = revision + 1
            WHERE character_id = @CharacterId AND account_id = @AccountId AND essence >= @Amount
            RETURNING essence;
            """;
        private const string DEBIT_SHARDS_SQL =
            """
            UPDATE characters SET shards = shards - @Amount, revision = revision + 1
            WHERE character_id = @CharacterId AND account_id = @AccountId AND shards >= @Amount
            RETURNING shards;
            """;
        private const string DEBIT_ITEM_SQL =
            """
            UPDATE account_items SET quantity = quantity - @Amount
            WHERE account_id = @AccountId AND definition_id = @AssetId AND binding_kind = @BindingKind
                AND quantity - reserved >= @Amount
            RETURNING quantity;
            """;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> amount(양수)를 정확히 빼고 변경 뒤 잔액을 돌려준다. 모자라거나 행이 없으면 null이며 호출자가
        /// INSUFFICIENT_ITEM으로 거절하고 효과 전체를 되돌린다 </summary>
        public static async Task<long?> TryDebitExactAsync(DbConnection connection, DbTransaction transaction,
            EconomyAsset asset, long amount, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(asset);
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            string sql = asset.Kind switch
            {
                EconomyAsset.CURRENCY     => DEBIT_CURRENCY_SQL,
                EconomyAsset.TICKETS      => DEBIT_TICKETS_SQL,
                EconomyAsset.ESSENCE      => DEBIT_ESSENCE_SQL,
                EconomyAsset.SHARDS       => DEBIT_SHARDS_SQL,
                EconomyAsset.ACCOUNT_ITEM => DEBIT_ITEM_SQL,
                _ => throw new ArgumentException("Unsupported asset kind.", nameof(asset))
            };
            return await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(sql, new
            {
                asset.AccountId,
                asset.CharacterId,
                asset.AssetId,
                asset.BindingKind,
                Amount = amount
            }, transaction, cancellationToken: cancellationToken));
        }
    }
}
