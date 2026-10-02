namespace Shinshi.Patch.Client
{
    /// <summary> CheckAsync 결과. Failed가 아니면 대상 릴리스 정보가 채워진다 </summary>
    public sealed class PatchCheckResult
    {
        /****************************************
        *              Properties
        ****************************************/

        public EPatchCheckStatus Status             { get; init; }
        public EPatchClientError Error              { get; init; }
        public EPatchError       PatchError         { get; init; }
        public string?           ActiveReleaseId    { get; init; }
        public string?           TargetReleaseId    { get; init; }
        public string?           DisplayVersion     { get; init; }
        public string?           MinLauncherVersion { get; init; }
        public long              Sequence           { get; init; }
        public long              BytesToDownload    { get; init; }
        public long              TotalBytes         { get; init; }

        public bool IsSuccess => Error == EPatchClientError.None;

        /****************************************
        *                Methods
        ****************************************/

        internal static PatchCheckResult Fail(EPatchClientError error, EPatchError patchError = EPatchError.None)
        {
            return new PatchCheckResult { Status = EPatchCheckStatus.Failed, Error = error, PatchError = patchError };
        }
    }
}
