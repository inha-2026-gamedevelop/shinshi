namespace Shinshi.Data.Ledger
{
    public sealed class OutboxBatchResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public int DeliveredCount { get; }
        public int FailedCount { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal OutboxBatchResult(int deliveredCount, int failedCount)
        {
            DeliveredCount = deliveredCount;
            FailedCount    = failedCount;
        }
    }
}
