// System
using System;

namespace Shinshi.Data.Ledger
{
    /// <summary> 잔액을 갱신한 호출자가 같은 operation의 원장 행을 만든다. 공용 계약으로 확정한 사유 코드를 전달한다 </summary>
    public sealed class EconomyLedgerEntry
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid LedgerId { get; }
        public string EntryKey { get; }
        public EconomyAsset Asset { get; }
        public long Delta { get; }
        public long BalanceAfter { get; }
        public int ReasonCode { get; }
        public string SourceType { get; }
        public string SourceId { get; }
        public string ActorKind { get; }
        public Guid ActorId { get; }
        public Guid OperationId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public EconomyLedgerEntry(Guid ledgerId, string entryKey, EconomyAsset asset, long delta, long balanceAfter,
            int reasonCode, string sourceType, string sourceId, string actorKind, Guid actorId, Guid operationId)
        {
            ArgumentNullException.ThrowIfNull(asset);
            ArgumentException.ThrowIfNullOrWhiteSpace(entryKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
            if ((ledgerId == Guid.Empty) || (actorId == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Ledger, actor and operation IDs are required.");
            }
            if (delta == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delta));
            }
            if (balanceAfter < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(balanceAfter));
            }
            // 호출자는 ContractCodes.EconomyReason의 공유 사유 값을 전달한다
            if (reasonCode <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reasonCode));
            }
            if ((actorKind != "Player") && (actorKind != "Server") && (actorKind != "Admin") && (actorKind != "System"))
            {
                throw new ArgumentException("Unsupported authenticated actor kind.", nameof(actorKind));
            }

            LedgerId     = ledgerId;
            EntryKey     = entryKey;
            Asset        = asset;
            Delta        = delta;
            BalanceAfter = balanceAfter;
            ReasonCode   = reasonCode;
            SourceType   = sourceType;
            SourceId     = sourceId;
            ActorKind    = actorKind;
            ActorId      = actorId;
            OperationId  = operationId;
        }
    }
}
