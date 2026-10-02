// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Ledger;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Settlement
{
    /// <summary> 출정 원장 어댑터의 소유 범위, 사유 코드, 동일 트랜잭션 전달과 실패 경로를 검사한다 </summary>
    public sealed class LedgerWriterTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   LARGE_AMOUNT = 3_000_000_001L;
        private const long   LARGE_BALANCE = 6_000_000_002L;
        private const int    TWO_CALLS    = 2;
        private const int    THREE_CALLS  = 3;
        private const string ITEM_ID      = "potion-small";
        private const string BINDING      = "account";
        private const string SOURCE_TYPE  = "GameSession";
        private const string SOURCE_ID    = "recorded-session";
        private const string ACTOR_KIND   = "Server";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task AppendAsync_Tickets_PreservesAccountDebitAndAuthenticatedSource()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            OperationExecutionContext context = CreateContext();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            EconomyLedgerLine line = CreateLine(EconomyLedgerCodes.ASSET_TICKET,
                reason: EconomyLedgerCodes.REASON_SESSION_START_TICKET, delta: -LARGE_AMOUNT);

            await adapter.AppendAsync(context, new[] { line }, cancellation.Token);

            LedgerCall call = Assert.Single(target.Calls);
            AssertMappedLine(line, call.Entry, ContractCodes.EconomyReason.SESSION_START_TICKET);
            Assert.Equal(EconomyAsset.TICKETS, call.Entry.Asset.Kind);
            Assert.Null(call.Entry.Asset.CharacterId);
            Assert.Equal(string.Empty, call.Entry.Asset.AssetId);
            Assert.Equal(string.Empty, call.Entry.Asset.BindingKind);
            Assert.Same(context, call.Context);
            Assert.Equal(cancellation.Token, call.CancellationToken);
        }

        [Fact]
        public async Task AppendAsync_Essence_PreservesCharacterCreditAndSystemActor()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            EconomyLedgerLine line = CreateLine(EconomyLedgerCodes.ASSET_ESSENCE, Guid.NewGuid(),
                reason: EconomyLedgerCodes.REASON_SERVICE_CANCEL_GRANT, actorKind: "System");

            await adapter.AppendAsync(CreateContext(), new[] { line }, CancellationToken.None);

            EconomyLedgerEntry entry = Assert.Single(target.Calls).Entry;
            AssertMappedLine(line, entry, ContractCodes.EconomyReason.SERVICE_CANCEL_ESSENCE);
            Assert.Equal(EconomyAsset.ESSENCE, entry.Asset.Kind);
            Assert.Equal(line.CharacterId, entry.Asset.CharacterId);
            Assert.Equal(string.Empty, entry.Asset.AssetId);
            Assert.Equal(string.Empty, entry.Asset.BindingKind);
        }

        [Fact]
        public async Task AppendAsync_Shards_PreservesCharacterOwner()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            EconomyLedgerLine line = CreateLine(EconomyLedgerCodes.ASSET_SHARDS, Guid.NewGuid());

            await adapter.AppendAsync(CreateContext(), new[] { line }, CancellationToken.None);

            EconomyLedgerEntry entry = Assert.Single(target.Calls).Entry;
            AssertMappedLine(line, entry, ContractCodes.EconomyReason.SESSION_SETTLEMENT);
            Assert.Equal(EconomyAsset.SHARDS, entry.Asset.Kind);
            Assert.Equal(line.CharacterId, entry.Asset.CharacterId);
            Assert.Equal(string.Empty, entry.Asset.AssetId);
        }

        [Fact]
        public async Task AppendAsync_Currency_PreservesAccountOwnerWithoutCharacter()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            EconomyLedgerLine line = CreateLine(EconomyLedgerCodes.ASSET_CURRENCY);

            await adapter.AppendAsync(CreateContext(), new[] { line }, CancellationToken.None);

            EconomyLedgerEntry entry = Assert.Single(target.Calls).Entry;
            AssertMappedLine(line, entry, ContractCodes.EconomyReason.SESSION_SETTLEMENT);
            Assert.Equal(EconomyAsset.CURRENCY, entry.Asset.Kind);
            Assert.Null(entry.Asset.CharacterId);
            Assert.Equal(string.Empty, entry.Asset.BindingKind);
        }

        [Fact]
        public async Task AppendAsync_AccountItem_PreservesDefinitionBindingAndEntryKey()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            EconomyLedgerLine line = CreateLine(EconomyLedgerCodes.ASSET_ITEM, assetId: ITEM_ID,
                bindingKind: BINDING, reason: EconomyLedgerCodes.REASON_SESSION_POTION_USE,
                delta: -LARGE_AMOUNT);

            await adapter.AppendAsync(CreateContext(), new[] { line }, CancellationToken.None);

            EconomyLedgerEntry entry = Assert.Single(target.Calls).Entry;
            AssertMappedLine(line, entry, ContractCodes.EconomyReason.SESSION_POTION_USE);
            Assert.Equal(EconomyAsset.ACCOUNT_ITEM, entry.Asset.Kind);
            Assert.Null(entry.Asset.CharacterId);
            Assert.Equal(ITEM_ID, entry.Asset.AssetId);
            Assert.Equal(BINDING, entry.Asset.BindingKind);
        }

        [Theory]
        [InlineData(EconomyLedgerCodes.REASON_SESSION_START_TICKET, ContractCodes.EconomyReason.SESSION_START_TICKET)]
        [InlineData(EconomyLedgerCodes.REASON_SESSION_POTION_USE, ContractCodes.EconomyReason.SESSION_POTION_USE)]
        [InlineData(EconomyLedgerCodes.REASON_SESSION_SETTLEMENT, ContractCodes.EconomyReason.SESSION_SETTLEMENT)]
        [InlineData(EconomyLedgerCodes.REASON_SERVICE_CANCEL_REFUND, ContractCodes.EconomyReason.SERVICE_CANCEL_REFUND)]
        [InlineData(EconomyLedgerCodes.REASON_SERVICE_CANCEL_GRANT, ContractCodes.EconomyReason.SERVICE_CANCEL_ESSENCE)]
        public async Task AppendAsync_DocumentedSessionReason_UsesSharedNumericCode(string reason, int expectedCode)
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            EconomyLedgerLine line = CreateLine(EconomyLedgerCodes.ASSET_TICKET, reason: reason);

            await adapter.AppendAsync(CreateContext(), new[] { line }, CancellationToken.None);

            Assert.Equal(expectedCode, Assert.Single(target.Calls).Entry.ReasonCode);
        }

        [Fact]
        public async Task AppendAsync_MultipleAssets_UsesDistinctIdsAndTheSameContextInOrder()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            OperationExecutionContext context = CreateContext();
            EconomyLedgerLine[] lines = new[]
            {
                CreateLine(EconomyLedgerCodes.ASSET_TICKET),
                CreateLine(EconomyLedgerCodes.ASSET_ESSENCE, Guid.NewGuid()),
                CreateLine(EconomyLedgerCodes.ASSET_ITEM, assetId: ITEM_ID, bindingKind: BINDING)
            };

            await adapter.AppendAsync(context, lines, CancellationToken.None);

            Assert.Equal(THREE_CALLS, target.Calls.Count);
            Assert.Equal(THREE_CALLS, target.Calls.Select(call => call.Entry.LedgerId).Distinct().Count());
            for (int index = 0; index < lines.Length; ++index)
            {
                Assert.Same(context, target.Calls[index].Context);
                Assert.Equal(lines[index].EntryKey, target.Calls[index].Entry.EntryKey);
                Assert.NotEqual(Guid.Empty, target.Calls[index].Entry.LedgerId);
            }
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("1")]
        [InlineData("sessionstartticket")]
        [InlineData("SessionStartTicket ")]
        public async Task AppendAsync_UnsupportedReason_RejectsWholeBatchBeforeWriting(string reason)
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            EconomyLedgerLine[] lines = new[]
            {
                CreateLine(EconomyLedgerCodes.ASSET_TICKET),
                CreateLine(EconomyLedgerCodes.ASSET_TICKET, reason: reason)
            };

            await Assert.ThrowsAsync<ArgumentException>(() =>
                adapter.AppendAsync(CreateContext(), lines, CancellationToken.None));

            Assert.Empty(target.Calls);
        }

        [Theory]
        [InlineData("currency", false, "", "")]
        [InlineData("tickets", false, "", "")]
        [InlineData(EconomyLedgerCodes.ASSET_CURRENCY, true, "", "")]
        [InlineData(EconomyLedgerCodes.ASSET_CURRENCY, false, ITEM_ID, "")]
        [InlineData(EconomyLedgerCodes.ASSET_SHARDS, false, "", "")]
        [InlineData(EconomyLedgerCodes.ASSET_SHARDS, true, "", BINDING)]
        [InlineData(EconomyLedgerCodes.ASSET_TICKET, true, "", "")]
        [InlineData(EconomyLedgerCodes.ASSET_TICKET, false, ITEM_ID, "")]
        [InlineData(EconomyLedgerCodes.ASSET_TICKET, false, "", BINDING)]
        [InlineData(EconomyLedgerCodes.ASSET_ESSENCE, false, "", "")]
        [InlineData(EconomyLedgerCodes.ASSET_ESSENCE, true, ITEM_ID, "")]
        [InlineData(EconomyLedgerCodes.ASSET_ESSENCE, true, "", BINDING)]
        [InlineData(EconomyLedgerCodes.ASSET_ITEM, true, ITEM_ID, BINDING)]
        [InlineData(EconomyLedgerCodes.ASSET_ITEM, false, "", BINDING)]
        [InlineData(EconomyLedgerCodes.ASSET_ITEM, false, " ", BINDING)]
        [InlineData(EconomyLedgerCodes.ASSET_ITEM, false, ITEM_ID, "")]
        [InlineData(EconomyLedgerCodes.ASSET_ITEM, false, ITEM_ID, " ")]
        public async Task AppendAsync_InvalidAssetShape_RejectsWholeBatchBeforeWriting(string assetKind,
            bool hasCharacter, string assetId, string bindingKind)
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            EconomyLedgerLine[] lines = new[]
            {
                CreateLine(EconomyLedgerCodes.ASSET_TICKET),
                CreateLine(assetKind, hasCharacter ? Guid.NewGuid() : null, assetId, bindingKind)
            };

            await Assert.ThrowsAnyAsync<ArgumentException>(() =>
                adapter.AppendAsync(CreateContext(), lines, CancellationToken.None));

            Assert.Empty(target.Calls);
        }

        [Fact]
        public async Task AppendAsync_EmptyOwnerOrCharacter_RejectsBeforeWriting()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            EconomyLedgerLine missingAccount = CreateLine(EconomyLedgerCodes.ASSET_TICKET, accountId: Guid.Empty);
            EconomyLedgerLine missingCharacter = CreateLine(EconomyLedgerCodes.ASSET_ESSENCE, Guid.Empty);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                adapter.AppendAsync(CreateContext(), new[] { missingAccount }, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                adapter.AppendAsync(CreateContext(), new[] { missingCharacter }, CancellationToken.None));

            Assert.Empty(target.Calls);
        }

        [Fact]
        public async Task AppendAsync_NullArgumentOrLine_RejectsBeforeWriting()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);

            Assert.Throws<ArgumentNullException>(() => new LedgerWriter(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                adapter.AppendAsync(null!, Array.Empty<EconomyLedgerLine>(), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                adapter.AppendAsync(CreateContext(), null!, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                adapter.AppendAsync(CreateContext(), new[] { CreateLine(EconomyLedgerCodes.ASSET_TICKET), null! },
                    CancellationToken.None));

            Assert.Empty(target.Calls);
        }

        [Fact]
        public async Task AppendAsync_EmptyBatch_DoesNotCallEntryWriter()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);

            await adapter.AppendAsync(CreateContext(), Array.Empty<EconomyLedgerLine>(), CancellationToken.None);

            Assert.Empty(target.Calls);
        }

        [Fact]
        public async Task AppendAsync_CancelledToken_DoesNotCallEntryWriter()
        {
            CapturingLedgerWriter target = new CapturingLedgerWriter();
            LedgerWriter adapter = new LedgerWriter(target);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                adapter.AppendAsync(CreateContext(), new[] { CreateLine(EconomyLedgerCodes.ASSET_TICKET) },
                    cancellation.Token));

            Assert.Empty(target.Calls);
        }

        [Fact]
        public async Task AppendAsync_WriterFailure_PropagatesAndStopsRemainingWrites()
        {
            InvalidOperationException failure = new InvalidOperationException("Simulated append failure.");
            CapturingLedgerWriter target = new CapturingLedgerWriter
            {
                FailureCall = TWO_CALLS,
                Failure     = failure
            };
            LedgerWriter adapter = new LedgerWriter(target);
            EconomyLedgerLine[] lines = new[]
            {
                CreateLine(EconomyLedgerCodes.ASSET_TICKET),
                CreateLine(EconomyLedgerCodes.ASSET_ESSENCE, Guid.NewGuid()),
                CreateLine(EconomyLedgerCodes.ASSET_ITEM, assetId: ITEM_ID, bindingKind: BINDING)
            };

            InvalidOperationException observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                adapter.AppendAsync(CreateContext(), lines, CancellationToken.None));

            Assert.Same(failure, observed);
            Assert.Equal(TWO_CALLS, target.Calls.Count);
            Assert.Equal(lines[0].EntryKey, target.Calls[0].Entry.EntryKey);
            Assert.Equal(lines[1].EntryKey, target.Calls[1].Entry.EntryKey);
        }

        private static EconomyLedgerLine CreateLine(string assetKind, Guid? characterId = null,
            string assetId = "", string bindingKind = "",
            string reason = EconomyLedgerCodes.REASON_SESSION_SETTLEMENT, long delta = LARGE_AMOUNT,
            Guid? accountId = null, string actorKind = ACTOR_KIND)
        {
            return new EconomyLedgerLine(accountId ?? Guid.NewGuid(), characterId, assetKind, assetId, bindingKind,
                delta, LARGE_BALANCE, reason, SOURCE_TYPE, SOURCE_ID, Guid.NewGuid(), actorKind, Guid.NewGuid());
        }

        private static OperationExecutionContext CreateContext()
        {
            return new OperationExecutionContext(null, null, DateTimeOffset.UnixEpoch);
        }

        private static void AssertMappedLine(EconomyLedgerLine line, EconomyLedgerEntry entry, int reasonCode)
        {
            Assert.NotEqual(Guid.Empty, entry.LedgerId);
            Assert.Equal(line.EntryKey, entry.EntryKey);
            Assert.Equal(line.AccountId, entry.Asset.AccountId);
            Assert.Equal(line.Delta, entry.Delta);
            Assert.Equal(line.BalanceAfter, entry.BalanceAfter);
            Assert.Equal(reasonCode, entry.ReasonCode);
            Assert.Equal(line.SourceType, entry.SourceType);
            Assert.Equal(line.SourceId, entry.SourceId);
            Assert.Equal(line.ActorKind, entry.ActorKind);
            Assert.Equal(line.ActorId, entry.ActorId);
            Assert.Equal(line.OperationId, entry.OperationId);
        }

        private sealed class CapturingLedgerWriter : IEconomyLedgerEntryWriter
        {
            /****************************************
            *              Properties
            ****************************************/

            public List<LedgerCall> Calls { get; } = new List<LedgerCall>();
            public int FailureCall { get; init; }
            public Exception? Failure { get; init; }

            /****************************************
            *                Methods
            ****************************************/

            public Task<bool> AppendAsync(OperationExecutionContext context, EconomyLedgerEntry entry,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(new LedgerCall(context, entry, cancellationToken));
                if ((Calls.Count == FailureCall) && (Failure != null))
                {
                    return Task.FromException<bool>(Failure);
                }

                return Task.FromResult(true);
            }
        }

        private sealed class LedgerCall
        {
            /****************************************
            *              Properties
            ****************************************/

            public OperationExecutionContext Context { get; }
            public EconomyLedgerEntry Entry { get; }
            public CancellationToken CancellationToken { get; }

            /****************************************
            *                Methods
            ****************************************/

            public LedgerCall(OperationExecutionContext context, EconomyLedgerEntry entry,
                CancellationToken cancellationToken)
            {
                Context           = context;
                Entry             = entry;
                CancellationToken = cancellationToken;
            }
        }
    }
}
