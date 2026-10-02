// System
using System.Runtime.Serialization;

namespace Shinshi.Patch
{
    /// <summary> 매니페스트의 파일 한 개, path는 "/" 구분 상대 경로 </summary>
    [DataContract]
    public sealed class ManifestFile
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "path")]   private string? _path;
        [DataMember(Name = "size")]   private long    _size;
        [DataMember(Name = "sha256")] private string? _sha256;

        /****************************************
        *              Properties
        ****************************************/

        public string Path   => _path ?? string.Empty;
        public long   Size   => _size;
        public string Sha256 => _sha256 ?? string.Empty;

        /****************************************
        *                Methods
        ****************************************/

        public ManifestFile(string path, long size, string sha256)
        {
            _path   = path;
            _size   = size;
            _sha256 = sha256;
        }

        private ManifestFile() { }
    }
}
