using System;
using System.Threading.Tasks;

using Xunit;

using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests
{
    public sealed class EconomyLedgerEntryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long NEGATIVE_VALUE = -1L;
        private const string ENTRY_KEY    = "currency";
        private const string SOURCE_TYPE  = "InitialGrant";
        private const string SOURCE_ID    = "test-grant";
        private const string ACTOR_KIND   = "System";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void CreateAsset_EmptyOwnerOrCharacter_RejectsInvalidIdentity()
        {
            Guid accountId = Guid.NewGuid();
            Assert.Throws<ArgumentException>(() => EconomyAsset.Currency(Guid.Empty));
            Assert.Throws<ArgumentException>(() => EconomyAsset.Tickets(Guid.Empty));
            Assert.Throws<ArgumentException>(() => EconomyAsset.Essence(accountId, Guid.Empty));
            Assert.Throws<ArgumentException>(() => EconomyAsset.Shards(accountId, Guid.Empty));
            Assert.Throws<ArgumentException>(() => EconomyAsset.Essence(Guid.Empty, Guid.NewGuid()));
            Assert.Throws<ArgumentException>(() => EconomyAsset.AccountItem(Guid.Empty, "herb", "Account"));
        }

        [Fact]
        public void AccountItem_EmptyDefinitionOrBinding_RejectsInvalidStack()
        {
            Guid accountId = Guid.NewGuid();
            Assert.Throws<ArgumentException>(() => EconomyAsset.AccountItem(accountId, " ", "Account"));
            Assert.Throws<ArgumentException>(() => EconomyAsset.AccountItem(accountId, "herb", " "));
        }

        [Fact]
        public void Construct_ZeroDeltaNegativeBalanceOrMissingReason_RejectsInvalidAmounts()
        {
            EconomyAsset asset = EconomyAsset.Currency(Guid.NewGuid());
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateEntry(asset, delta: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateEntry(asset, balanceAfter: NEGATIVE_VALUE));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateEntry(asset, reasonCode: 0));
        }

        [Fact]
        public void Construct_MissingIdentityOrSource_RejectsInvalidEntry()
        {
            EconomyAsset asset = EconomyAsset.Currency(Guid.NewGuid());
            Assert.Throws<ArgumentException>(() => CreateEntry(asset, ledgerId: Guid.Empty));
            Assert.Throws<ArgumentException>(() => CreateEntry(asset, actorId: Guid.Empty));
            Assert.Throws<ArgumentException>(() => CreateEntry(asset, operationId: Guid.Empty));
            Assert.Throws<ArgumentException>(() => CreateEntry(asset, entryKey: " "));
            Assert.Throws<ArgumentException>(() => CreateEntry(asset, sourceType: " "));
            Assert.Throws<ArgumentException>(() => CreateEntry(asset, sourceId: " "));
            Assert.Throws<ArgumentException>(() => CreateEntry(asset, actorKind: "Client"));
            Assert.Throws<ArgumentNullException>(() => CreateEntry(null!));
        }

        [Fact]
        public void Construct_LongAmounts_PreservesExactCreditAndDebit()
        {
            EconomyAsset asset = EconomyAsset.Currency(Guid.NewGuid());
            EconomyLedgerEntry credit = CreateEntry(asset);
            EconomyLedgerEntry debit = CreateEntry(asset, delta: -LedgerTestSupport.GRANT_AMOUNT, balanceAfter: 0);
            Assert.Equal(LedgerTestSupport.GRANT_AMOUNT, credit.Delta);
            Assert.Equal(LedgerTestSupport.GRANT_AMOUNT, credit.BalanceAfter);
            Assert.Equal(-LedgerTestSupport.GRANT_AMOUNT, debit.Delta);
            Assert.Equal(0L, debit.BalanceAfter);
        }

        [Fact]
        public async Task AppendAsync_ContextWithoutDatabase_RejectsBeforeWriting()
        {
            OperationExecutionContext context = new OperationExecutionContext(null, null, DateTimeOffset.UnixEpoch);
            EconomyLedgerEntry entry = CreateEntry(EconomyAsset.Currency(Guid.NewGuid()));
            PostgresEconomyLedgerWriter writer = new PostgresEconomyLedgerWriter();
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.AppendAsync(context, entry));
        }

        private static EconomyLedgerEntry CreateEntry(EconomyAsset asset, Guid? ledgerId = null,
            Guid? actorId = null, Guid? operationId = null, long delta = LedgerTestSupport.GRANT_AMOUNT,
            long balanceAfter = LedgerTestSupport.GRANT_AMOUNT,
            int reasonCode = LedgerTestSupport.TEST_REASON_CODE, string entryKey = ENTRY_KEY,
            string sourceType = SOURCE_TYPE, string sourceId = SOURCE_ID, string actorKind = ACTOR_KIND)
        {
            return new EconomyLedgerEntry(ledgerId ?? Guid.NewGuid(), entryKey, asset, delta, balanceAfter,
                reasonCode, sourceType, sourceId, actorKind, actorId ?? Guid.NewGuid(), operationId ?? Guid.NewGuid());
        }
    }

}
