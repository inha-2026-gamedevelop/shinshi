namespace Shinshi.Patch.Client
{
    /// <summary> 활성 설치, 런처는 VersionPath 아래 실행 파일로 게임을 시작한다 </summary>
    public sealed class InstalledRelease
    {
        /****************************************
        *              Properties
        ****************************************/

        public string          ReleaseId      { get; }
        public string          VersionPath    { get; }
        public ReleaseManifest Manifest       { get; }
        public string          ManifestSha256 { get; }

        /****************************************
        *                Methods
        ****************************************/

        internal InstalledRelease(string releaseId, string versionPath, ReleaseManifest manifest, string manifestSha256)
        {
            ReleaseId      = releaseId;
            VersionPath    = versionPath;
            Manifest       = manifest;
            ManifestSha256 = manifestSha256;
        }
    }
}
