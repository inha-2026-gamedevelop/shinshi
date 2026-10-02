namespace Shinshi.Patch.Client
{
    /// <summary> UpdateAsync, VerifyAndRepairAsync 결과. 실패해도 기존 활성 설치는 그대로 실행 가능하다 </summary>
    public sealed class PatchOperationResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public EPatchClientError Error                    { get; init; }
        public EPatchError       PatchError               { get; init; }
        public string?           ReleaseId                { get; init; }
        public bool              WasUpToDate              { get; init; }
        public int               DownloadedFiles          { get; init; }
        public long              DownloadedBytes          { get; init; }
        public int               CopiedFiles              { get; init; }
        public int               RepairedFiles            { get; init; }
        public int               RemovedFiles             { get; init; }
        public int               RemovedVersions          { get; init; }

        // 새 버전은 활성으로 바뀌었지만 옛 버전 폴더 일부를 지우지 못했다. 다음 업데이트에서 다시 지운다
        public bool              IsRetentionCleanupFailed { get; init; }

        public bool IsSuccess => Error == EPatchClientError.None;

        /****************************************
        *                Methods
        ****************************************/

        internal static PatchOperationResult Fail(EPatchClientError error, EPatchError patchError = EPatchError.None)
        {
            return new PatchOperationResult { Error = error, PatchError = patchError };
        }
    }
}
