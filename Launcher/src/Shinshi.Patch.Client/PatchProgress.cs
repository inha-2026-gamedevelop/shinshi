namespace Shinshi.Patch.Client
{
    /// <summary> 진행률 한 번의 보고, 바이트는 단계마다 0부터 다시 센다 </summary>
    public readonly struct PatchProgress
    {
        /****************************************
        *              Properties
        ****************************************/

        public EPatchStage Stage          { get; }
        public long        CompletedBytes { get; }
        public long        TotalBytes     { get; }

        /****************************************
        *                Methods
        ****************************************/

        public PatchProgress(EPatchStage stage, long completedBytes, long totalBytes)
        {
            Stage          = stage;
            CompletedBytes = completedBytes;
            TotalBytes     = totalBytes;
        }
    }
}
