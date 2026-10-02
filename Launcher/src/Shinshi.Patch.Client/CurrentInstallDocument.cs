// System
using System.Runtime.Serialization;

namespace Shinshi.Patch.Client
{
    /// <summary> current.json, 활성 releaseId 하나만 담는다 </summary>
    [DataContract]
    internal sealed class CurrentInstallDocument
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "formatVersion")] private int     _formatVersion;
        [DataMember(Name = "releaseId")]     private string? _releaseId;

        /****************************************
        *              Properties
        ****************************************/

        public int    FormatVersion => _formatVersion;
        public string ReleaseId     => _releaseId ?? string.Empty;

        /****************************************
        *                Methods
        ****************************************/

        public CurrentInstallDocument(string releaseId)
        {
            _formatVersion = PatchFormat.FORMAT_VERSION;
            _releaseId     = releaseId;
        }

        private CurrentInstallDocument() { }
    }
}
