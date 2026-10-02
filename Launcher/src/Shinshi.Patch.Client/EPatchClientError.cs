namespace Shinshi.Patch.Client
{
    /// <summary> 확인, 업데이트, 복구가 실패한 이유. 문서 거절의 세부 이유는 결과의 PatchError에 함께 담는다 </summary>
    public enum EPatchClientError
    {
        None,
        Network,
        DocumentRejected,
        ManifestMismatch,
        SequenceRollback,
        LauncherOutdated,
        ReleaseConflict,
        NotInstalled,
        InvalidPath,
        DiskSpace,
        HashMismatch,
        GameRunning,
        Busy,
        Cancelled,
        IoFailure
    }
}
