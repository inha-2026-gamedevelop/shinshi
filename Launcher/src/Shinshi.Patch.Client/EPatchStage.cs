namespace Shinshi.Patch.Client
{
    /// <summary> 진행률 보고의 현재 단계 </summary>
    public enum EPatchStage
    {
        Checking,
        Preparing,
        Downloading,
        Verifying,
        Finalizing,
        Completed
    }
}
