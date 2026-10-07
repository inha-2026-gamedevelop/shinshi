// System
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Data.Growth;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Market
{
    /// <summary> 경매장, 1:1 거래 효과가 쓰는 같은 트랜잭션의 잔액 변경과 원장 한 줄. 원장 줄의 계정은 자산 소유 계정이고
    /// actor는 작업의 멱등 actor다. 같은 자산을 한 작업에서 여러 번 바꿀 수 있어 scope(방향, 역할)로 entry_key를 나눈다 </summary>
    internal sealed class EconomyMover
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly OperationExecutionContext _context;
        private readonly DbConnection _connection;
        private readonly DbTransaction _transaction;
        private readonly IEconomyLedgerEntryWriter _ledger;
        private readonly string _actorKind;
        private readonly Guid _actorId;
        private readonly Guid _operationId;
        private readonly string _sourceType;
        private readonly string _sourceId;

        /****************************************
        *                Methods
        ****************************************/

        public EconomyMover(OperationExecutionContext context, IEconomyLedgerEntryWriter ledger, string actorKind,
            Guid actorId, Guid operationId, string sourceType, string sourceId)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(ledger);
            (_connection, _transaction) = Sessions.SessionDb.Require(context);
            _context     = context;
            _ledger      = ledger;
            _actorKind   = actorKind;
            _actorId     = actorId;
            _operationId = operationId;
            _sourceType  = sourceType;
            _sourceId    = sourceId;
        }

        /// <summary> 가용량(총량 - 예약량)에서 정확히 amount를 빼고 원장을 쓴다. 모자라면 아무것도 바꾸지 않고 false다. 0이면 줄을 만들지 않는다 </summary>
        public async Task<bool> TryDebitAsync(EconomyAsset asset, long amount, int reasonCode, string scope,
            CancellationToken cancellationToken)
        {
            if (amount == 0)
            {
                return true;
            }

            long? after = await EconomyDebits.TryDebitExactAsync(_connection, _transaction, asset, amount, cancellationToken);
            if (!after.HasValue)
            {
                return false;
            }

            await AppendAsync(asset, -amount, after.Value, reasonCode, scope, cancellationToken);
            return true;
        }

        /// <summary> amount를 더하고 원장을 쓴다. 지갑, 아이템 행이 없으면 만든다. 0이면 줄을 만들지 않는다 </summary>
        public async Task CreditAsync(EconomyAsset asset, long amount, int reasonCode, string scope,
            CancellationToken cancellationToken)
        {
            if (amount == 0)
            {
                return;
            }

            long after = await RewardBalances.CreditAsync(_connection, _transaction, asset, amount, cancellationToken);
            await AppendAsync(asset, amount, after, reasonCode, scope, cancellationToken);
        }

        private Task<bool> AppendAsync(EconomyAsset asset, long delta, long balanceAfter, int reasonCode, string scope,
            CancellationToken cancellationToken)
        {
            string entryKey = scope + ":" + asset.Kind + ":" + asset.AccountId.ToString("N") + ":"
                + (asset.CharacterId?.ToString("N") ?? string.Empty) + ":" + asset.AssetId + ":" + asset.BindingKind;
            return _ledger.AppendAsync(_context, new EconomyLedgerEntry(Guid.NewGuid(), entryKey, asset, delta,
                balanceAfter, reasonCode, _sourceType, _sourceId, _actorKind, _actorId, _operationId), cancellationToken);
        }
    }
}
