// System
using System.Runtime.Serialization;

namespace Shinshi.Launcher.Config
{
    /// <summary> 실행 파일 옆 launcher.json 원문. 검사와 기본값 채우기는 LauncherConfigLoader가 한다 </summary>
    [DataContract]
    internal sealed class LauncherConfigDocument
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "patchSource")] private string?   _patchSource;
        [DataMember(Name = "channel")]     private string?   _channel;
        [DataMember(Name = "publicKeys")]  private string[]? _publicKeys;
        [DataMember(Name = "installRoot")] private string?   _installRoot;
        [DataMember(Name = "teamBuild")]   private bool      _teamBuild;
        [DataMember(Name = "apiBaseUrl")]  private string?   _apiBaseUrl;
        [DataMember(Name = "websiteUrl")]  private string?   _websiteUrl;

        /****************************************
        *              Properties
        ****************************************/

        public string?   PatchSource => _patchSource;
        public string?   Channel     => _channel;
        public string[]? PublicKeys  => _publicKeys;
        public string?   InstallRoot => _installRoot;
        public bool      TeamBuild   => _teamBuild;
        public string?   ApiBaseUrl  => _apiBaseUrl;
        public string?   WebsiteUrl  => _websiteUrl;

        /****************************************
        *                Methods
        ****************************************/

        public LauncherConfigDocument(string? patchSource, string? channel, string[]? publicKeys, string? installRoot, bool teamBuild,
            string? apiBaseUrl, string? websiteUrl)
        {
            _patchSource = patchSource;
            _channel     = channel;
            _publicKeys  = publicKeys;
            _installRoot = installRoot;
            _teamBuild   = teamBuild;
            _apiBaseUrl  = apiBaseUrl;
            _websiteUrl  = websiteUrl;
        }

        private LauncherConfigDocument() { }
    }
}
