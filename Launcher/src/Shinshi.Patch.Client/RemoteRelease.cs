namespace Shinshi.Patch.Client
{
    /// <summary> 서명, sequence, 짝 검사를 모두 통과한 채널 포인터와 매니페스트 </summary>
    internal sealed class RemoteRelease
    {
        /****************************************
        *              Properties
        ****************************************/

        public ChannelPointer  Pointer          { get; }
        public ReleaseManifest Manifest         { get; }
        public byte[]          ManifestEnvelope { get; }
        public string          ManifestSha256   { get; }

        public string ReleaseId => Manifest.ReleaseId;

        /****************************************
        *                Methods
        ****************************************/

        public RemoteRelease(ChannelPointer pointer, ReleaseManifest manifest, byte[] manifestEnvelope, string manifestSha256)
        {
            Pointer          = pointer;
            Manifest         = manifest;
            ManifestEnvelope = manifestEnvelope;
            ManifestSha256   = manifestSha256;
        }
    }
}
