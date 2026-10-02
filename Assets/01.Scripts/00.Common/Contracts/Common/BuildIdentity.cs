// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 빌드마다 StreamingAssets/build-identity.json에 쓰는 신원, 게임과 런처와 게시 도구가 같은 파일을 읽는다 </summary>
    [DataContract]
    public sealed class BuildIdentity
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string FILE_NAME        = "build-identity.json";
        public const string KIND_DEVELOPMENT = "Development";
        public const string KIND_RELEASE     = "Release";

        // 빌드마다 유일, 로그와 거절 응답과 API 허용 목록에 같은 값을 쓴다
        [DataMember(Name = "buildId")]         private string _buildId;

        // 접속 서버와 완전히 같아야 하는 두 값
        [DataMember(Name = "protocolVersion")] private int    _protocolVersion;
        [DataMember(Name = "contentHash")]     private string _contentHash;

        // 화면 표시용 bundleVersion, 호환 판정에 쓰지 않는다
        [DataMember(Name = "displayVersion")]  private string _displayVersion;

        // KIND_DEVELOPMENT 또는 KIND_RELEASE. stable 채널은 Release만 받는다
        [DataMember(Name = "buildKind")]       private string _buildKind;

        [DataMember(Name = "createdAtUnixMs")] private long   _createdAtUnixMs;
        [DataMember(Name = "commit")]          private string _commit;

        /****************************************
        *              Properties
        ****************************************/

        public string BuildId         => _buildId;
        public int    ProtocolVersion => _protocolVersion;
        public string ContentHash     => _contentHash;
        public string DisplayVersion  => _displayVersion;
        public string BuildKind       => _buildKind;
        public long   CreatedAtUnixMs => _createdAtUnixMs;
        public string Commit          => _commit;

        public bool IsRelease => _buildKind == KIND_RELEASE;

        /****************************************
        *                Methods
        ****************************************/

        public BuildIdentity(string buildId, int protocolVersion, string contentHash, string displayVersion,
            string buildKind, long createdAtUnixMs, string commit)
        {
            _buildId         = buildId;
            _protocolVersion = protocolVersion;
            _contentHash     = contentHash;
            _displayVersion  = displayVersion;
            _buildKind       = buildKind;
            _createdAtUnixMs = createdAtUnixMs;
            _commit          = commit;
        }

        private BuildIdentity() { }

        public ClientBuildInfo ToClientBuildInfo() { return new ClientBuildInfo(_buildId, _protocolVersion, _contentHash); }
    }
}
