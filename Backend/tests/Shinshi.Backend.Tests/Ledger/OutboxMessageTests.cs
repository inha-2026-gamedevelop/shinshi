using System;
using System.Threading.Tasks;

using Xunit;

using Shinshi.Data;
using Shinshi.Data.Ledger;

namespace Shinshi.Backend.Tests
{
    public sealed class OutboxMessageTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Construct_InvalidJsonOrMissingIdentity_RejectsMessage()
        {
            OperationRequest request = LedgerTestSupport.CreateRequest(Guid.NewGuid());
            Assert.Throws<ArgumentException>(() => LedgerTestSupport.CreateMessage(request, Guid.Empty));
            Assert.Throws<ArgumentException>(() => LedgerTestSupport.CreateMessage(request, Guid.NewGuid(), consumerKey: " "));
            Assert.Throws<ArgumentException>(() => LedgerTestSupport.CreateMessage(request, Guid.NewGuid(), payloadJson: "[]"));
            Assert.Throws<ArgumentException>(() => LedgerTestSupport.CreateMessage(request, Guid.NewGuid(), payloadJson: "{"));
            Assert.Throws<ArgumentNullException>(() => LedgerTestSupport.CreateMessage(request, Guid.NewGuid(), payloadJson: null!));
        }

        [Fact]
        public async Task EnqueueAsync_ContextWithoutDatabase_RejectsBeforeWriting()
        {
            OperationRequest request = LedgerTestSupport.CreateRequest(Guid.NewGuid());
            OperationExecutionContext context = new OperationExecutionContext(null, null, DateTimeOffset.UnixEpoch);
            PostgresOutboxWriter writer = new PostgresOutboxWriter();
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.EnqueueAsync(context,
                LedgerTestSupport.CreateMessage(request, Guid.NewGuid())));
        }
    }

}
