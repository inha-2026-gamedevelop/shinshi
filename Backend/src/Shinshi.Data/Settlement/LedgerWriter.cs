// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Settlement
{
    /// <summary> 출정 원장 줄을 공용 원장 계약으로 바꾸고 호출자의 같은 트랜잭션에 기록한다 </summary>
    public sealed class LedgerWriter : IEconomyLedgerWriter
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IEconomyLedgerEntryWriter _writer;

        /****************************************
        *                Methods
        ****************************************/

        public LedgerWriter(IEconomyLedgerEntryWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            _writer = writer;
        }

        public async Task AppendAsync(OperationExecutionContext context, IReadOnlyList<EconomyLedgerLine> entries,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(entries);
            cancellationToken.ThrowIfCancellationRequested();
            int entryCount = entries.Count;
            if (entryCount == 0)
            {
                return;
            }

            // 잘못된 줄이 뒤에 있어도 앞의 원장 쓰기를 시작하지 않도록 전체 계약을 먼저 검사한다
            EconomyLedgerEntry[] mappedEntries = new EconomyLedgerEntry[entryCount];
            for (int index = 0; index < entryCount; ++index)
            {
                EconomyLedgerLine line = entries[index];
                ArgumentNullException.ThrowIfNull(line);
                mappedEntries[index] = new EconomyLedgerEntry(Guid.NewGuid(), line.EntryKey, MapAsset(line),
                    line.Delta, line.BalanceAfter, MapReason(line.Reason), line.SourceType, line.SourceId,
                    line.ActorKind, line.ActorId, line.OperationId);
            }

            // 같은 연결의 쓰기를 병렬화하지 않는다. 예외는 executor로 전파해 전체 경제 효과를 되돌린다
            for (int index = 0; index < mappedEntries.Length; ++index)
            {
                await _writer.AppendAsync(context, mappedEntries[index], cancellationToken);
            }
        }

        private static EconomyAsset MapAsset(EconomyLedgerLine line)
        {
            switch (line.AssetKind)
            {
                case EconomyLedgerCodes.ASSET_TICKET:
                    RequireSharedAsset(line);
                    RequireEmptyDefinition(line);
                    return EconomyAsset.Tickets(line.AccountId);
                case EconomyLedgerCodes.ASSET_ESSENCE:
                    if (!line.CharacterId.HasValue)
                    {
                        throw new ArgumentException("Essence requires a character owner.", nameof(line));
                    }
                    RequireEmptyDefinition(line);
                    return EconomyAsset.Essence(line.AccountId, line.CharacterId.Value);
                case EconomyLedgerCodes.ASSET_SHARDS:
                    if (!line.CharacterId.HasValue)
                    {
                        throw new ArgumentException("Shards require a character owner.", nameof(line));
                    }
                    RequireEmptyDefinition(line);
                    return EconomyAsset.Shards(line.AccountId, line.CharacterId.Value);
                case EconomyLedgerCodes.ASSET_CURRENCY:
                    RequireSharedAsset(line);
                    RequireEmptyDefinition(line);
                    return EconomyAsset.Currency(line.AccountId);
                case EconomyLedgerCodes.ASSET_ITEM:
                    RequireSharedAsset(line);
                    return EconomyAsset.AccountItem(line.AccountId, line.AssetId, line.BindingKind);
                default:
                    throw new ArgumentException("Unsupported session ledger asset kind.", nameof(line));
            }
        }

        private static void RequireSharedAsset(EconomyLedgerLine line)
        {
            if (line.CharacterId.HasValue)
            {
                throw new ArgumentException("Account-shared assets must not have a character owner.", nameof(line));
            }
        }

        private static void RequireEmptyDefinition(EconomyLedgerLine line)
        {
            if ((line.AssetId.Length != 0) || (line.BindingKind.Length != 0))
            {
                throw new ArgumentException("Wallet assets must not contain an item definition or binding.", nameof(line));
            }
        }

        private static int MapReason(string reason)
        {
            switch (reason)
            {
                case EconomyLedgerCodes.REASON_SESSION_START_TICKET:
                    return ContractCodes.EconomyReason.SESSION_START_TICKET;
                case EconomyLedgerCodes.REASON_SESSION_POTION_USE:
                    return ContractCodes.EconomyReason.SESSION_POTION_USE;
                case EconomyLedgerCodes.REASON_SESSION_SETTLEMENT:
                    return ContractCodes.EconomyReason.SESSION_SETTLEMENT;
                case EconomyLedgerCodes.REASON_SERVICE_CANCEL_REFUND:
                    return ContractCodes.EconomyReason.SERVICE_CANCEL_REFUND;
                case EconomyLedgerCodes.REASON_SERVICE_CANCEL_GRANT:
                    return ContractCodes.EconomyReason.SERVICE_CANCEL_ESSENCE;
                default:
                    throw new ArgumentException("Unsupported session ledger reason.", nameof(reason));
            }
        }
    }
}
