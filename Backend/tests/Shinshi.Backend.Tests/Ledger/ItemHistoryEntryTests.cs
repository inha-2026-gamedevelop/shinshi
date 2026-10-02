using System;
using System.Threading.Tasks;

using Xunit;

using Shinshi.Data;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests
{
    public sealed class ItemHistoryEntryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long NEGATIVE_SEQUENCE = -1L;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Construct_InvalidSequenceOrState_RejectsTransition()
        {
            Guid itemId = Guid.NewGuid();
            Guid accountId = Guid.NewGuid();
            Guid operationId = Guid.NewGuid();
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemHistoryEntry(itemId, NEGATIVE_SEQUENCE,
                "Created", null, "{}", accountId, accountId, operationId));
            Assert.Throws<ArgumentException>(() => new ItemHistoryEntry(itemId, 0,
                "Created", null, null, accountId, accountId, operationId));
            Assert.Throws<ArgumentException>(() => new ItemHistoryEntry(itemId, 0,
                "Created", null, "[]", accountId, accountId, operationId));
            Assert.Throws<ArgumentException>(() => new ItemHistoryEntry(Guid.Empty, 0,
                "Created", null, "{}", accountId, accountId, operationId));
        }

        [Fact]
        public async Task AppendAsync_ContextWithoutDatabase_RejectsBeforeWriting()
        {
            Guid accountId = Guid.NewGuid();
            ItemHistoryEntry entry = new ItemHistoryEntry(Guid.NewGuid(), 0, "Created", null, "{}",
                accountId, accountId, Guid.NewGuid());
            OperationExecutionContext context = new OperationExecutionContext(null, null, DateTimeOffset.UnixEpoch);
            PostgresItemHistoryWriter writer = new PostgresItemHistoryWriter();
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.AppendAsync(context, entry));
        }
    }
}


